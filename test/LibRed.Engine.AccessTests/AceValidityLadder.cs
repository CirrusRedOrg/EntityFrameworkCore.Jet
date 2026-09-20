using System.Data;
using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The oracle both write-validity arms share: given a file LibRed wrote, does Access's own engine accept it?
/// Five rungs, each a gate:
/// <list type="number">
///   <item>the provider connects at all (a refusal here is "Unrecognized database format")</item>
///   <item>Tables / Columns / Indexes / Foreign_Keys enumerate</item>
///   <item><c>SELECT *</c> over every user table, materialising every value (row decode + long-value chase)</item>
///   <item>ACE inserts, updates and deletes through its own allocator and index maintenance</item>
///   <item>LibRed reads the file back after ACE has written to it</item>
/// </list>
/// Rung 4 earns its place: ACE writing means ACE trusting page 1's free map enough to allocate against it, and
/// a file can pass 1-3 on a free map that is quietly wrong.
/// </summary>
/// <remarks>Shared so both arms judge by one standard — a finding from either has to be comparable.</remarks>
internal static class AceValidityLadder
{
    /// <summary><paramref name="Finding"/> is null when ACE accepted the file.</summary>
    /// <param name="TypedOnly">Tables whose TYPED read failed while the engine still returned the rows as text
    /// — an ACE provider defect, not a bad file. See the triage note in <see cref="Check"/>.</param>
    public sealed record Verdict(string? Finding, List<string> TypedOnly);

    /// <param name="writeTarget">An unconstrained table for rung 4, or null to skip it. It must be one ACE
    /// cannot refuse for a reason about the data rather than the file.</param>
    public static Verdict Check(string path, string? writeTarget)
    {
        var typedOnly = new List<string>();

        OleDbConnection connection;
        try
        {
            // Three attempts, not the helper's twelve: a file ACE genuinely rejects would otherwise burn 24
            // provider opens per case, and ACE's transient-open flakiness needs far less than that.
            connection = AceTestDatabase.Open(path, attempts: 3);
        }
        catch (Exception ex)
        {
            return new Verdict($"rung 1 (ACE cannot open the file): {Flatten(ex.Message)}", typedOnly);
        }

        using (connection)
        {
            var tables = new List<string>();
            try
            {
                foreach ((Guid guid, string name) in new[]
                {
                    (OleDbSchemaGuid.Tables, "Tables"),
                    (OleDbSchemaGuid.Columns, "Columns"),
                    (OleDbSchemaGuid.Indexes, "Indexes"),
                    (OleDbSchemaGuid.Foreign_Keys, "Foreign_Keys"),
                })
                {
                    using DataTable schema = connection.GetOleDbSchemaTable(guid, null)
                        ?? throw new InvalidOperationException($"{name} returned no schema table.");

                    if (guid != OleDbSchemaGuid.Tables) continue;
                    foreach (DataRow row in schema.Rows)
                        if ((string)row["TABLE_TYPE"] == "TABLE")
                            tables.Add((string)row["TABLE_NAME"]);
                }
            }
            catch (Exception ex)
            {
                return new Verdict(
                    $"rung 2 (ACE cannot enumerate the schema): {ex.GetType().Name}: {Flatten(ex.Message)}",
                    typedOnly);
            }

            foreach (string table in tables)
            {
                try
                {
                    using OleDbCommand command = connection.CreateCommand();
                    command.CommandText = $"SELECT * FROM [{table}]";
                    using OleDbDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                        for (int i = 0; i < reader.FieldCount; i++)
                            _ = reader.GetValue(i);   // materialise it: this is what chases a long value
                }
                catch (Exception ex)
                {
                    // A typed read failing is NOT evidence the file is bad — assuming otherwise cost this work
                    // two false findings. ACE's OLE DB provider breaks on values its own engine handles:
                    // DATETIME2 outright (docs/format/data-types.md's footnote) and a decimal wider than its
                    // declared precision, where the consumer buffer is sized from the declaration.
                    //
                    // So ask the ENGINE for the same rows as text first. `& ''` not CStr(), which raises
                    // "Invalid use of Null". If that works, only the typed marshalling is broken.
                    if (ReadsAsText(connection, table))
                    {
                        typedOnly.Add(
                            $"ACE's TYPED read of [{table}] fails though the engine returns the same rows as "
                            + $"text: {ex.GetType().Name}: {Flatten(ex.Message)}");
                        continue;
                    }

                    return new Verdict(
                        $"rung 3 (ACE cannot read [{table}]): {ex.GetType().Name}: {Flatten(ex.Message)}",
                        typedOnly);
                }
            }

            if (writeTarget is not null)
            {
                try
                {
                    Execute(connection, $"INSERT INTO [{writeTarget}] ([K], [V]) VALUES (9001, 'ace')");
                    Execute(connection, $"UPDATE [{writeTarget}] SET [V] = 'ace2' WHERE [K] = 9001");
                    Execute(connection, $"DELETE FROM [{writeTarget}] WHERE [K] = 9001");
                }
                catch (Exception ex)
                {
                    return new Verdict($"rung 4 (ACE cannot write): {ex.GetType().Name}: {Flatten(ex.Message)}",
                        typedOnly);
                }
            }
        }

        return new Verdict(LibRedReopen(path), typedOnly);
    }

    /// <summary>Every row of <paramref name="table"/> with each column converted to text, so no typed buffer is
    /// allocated. True means the engine can read the rows and any typed-path failure is the provider's.</summary>
    private static bool ReadsAsText(OleDbConnection connection, string table)
    {
        try
        {
            var columns = new List<string>();
            using (DataTable? schema = connection.GetOleDbSchemaTable(
                       OleDbSchemaGuid.Columns, [null, null, table, null]))
            {
                if (schema is null) return false;
                foreach (DataRow row in schema.Rows) columns.Add((string)row["COLUMN_NAME"]);
            }
            if (columns.Count == 0) return false;

            using OleDbCommand command = connection.CreateCommand();
            command.CommandText =
                $"SELECT {string.Join(", ", columns.Select(c => $"[{c}] & ''"))} FROM [{table}]";
            using OleDbDataReader reader = command.ExecuteReader();
            while (reader.Read())
                for (int i = 0; i < reader.FieldCount; i++)
                    _ = reader.GetValue(i);
            return true;
        }
        catch (Exception)
        {
            return false;   // the engine cannot produce the rows either — the file really is the problem
        }
    }

    /// <summary>Rung 5: LibRed reads back a file ACE has just written to.</summary>
    private static string? LibRedReopen(string path)
    {
        try
        {
            using var db = JetDatabase.Open(path, readOnly: true);
            foreach (TableDef table in db.Catalog.UserTables)
                foreach (object?[] row in db.OpenTable(table.Name).Rows())
                    _ = row.Length;
            return null;
        }
        catch (Exception ex)
        {
            return $"rung 5 (LibRed cannot reread what ACE wrote): {ex.GetType().Name}: {Flatten(ex.Message)}";
        }
    }

    private static void Execute(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static string Flatten(string message) =>
        string.Join(' ', message.Split('\n', '\r').Select(l => l.Trim()).Where(l => l.Length > 0));
}
