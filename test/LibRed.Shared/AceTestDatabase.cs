// Explicit usings — see the note in TemporaryDatabase.cs.
using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Threading;

namespace LibRed.Tests.Shared;

/// <summary>Opens test databases through an installed ACE OLE DB provider with consistent retry behavior.</summary>
internal static class AceTestDatabase
{
    private static readonly string[] Providers = ["Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0"];

    public static OleDbConnection Open(string path, string? password = null, int attempts = 12)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (attempts < 1) throw new ArgumentOutOfRangeException(nameof(attempts));

        Exception? last = null;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            foreach (string provider in Providers)
            {
                try
                {
                    string passwordPart = password is null ? "" : $"Jet OLEDB:Database Password={password};";
                    var connection = new OleDbConnection(
                        $"Provider={provider};Data Source={path};{passwordPart}OLE DB Services=-4;");
                    connection.Open();
                    return connection;
                }
                catch (Exception ex) when (ex is OleDbException or InvalidOperationException)
                {
                    last = ex;
                }
            }

            if (attempt + 1 < attempts)
                Thread.Sleep(40);
        }

        throw new InvalidOperationException("No Microsoft ACE OLE DB provider could open the test database.", last);
    }

    private static readonly Dictionary<string, bool> ColumnTypeSupport = [];

    /// <summary>
    /// Whether the ACE installed on this machine can create a column of <paramref name="typeName"/>, asked by
    /// trying it once and caching the answer.
    /// </summary>
    /// <remarks>
    /// The new-format types are not available on every ACE: <c>DATETIME2</c> (Date/Time Extended) needs ACE 17
    /// / Access 2019+, <c>BIGINT</c> (Large Number) needs ACE 16 / Access 2016. CI installs the **2016**
    /// redistributable, so a test written against a developer machine running Microsoft 365 will fail there
    /// for a reason that says nothing about LibRed. Guard those with
    /// <c>Assert.SkipUnless(AceTestDatabase.SupportsColumnType(...), ...)</c>.
    /// <para>The probe runs against a throwaway copy and needs no particular format version: ACE raises the
    /// file itself when a column demands a newer one.</para>
    /// <para>It assumes an ACE that does not know a type name <em>rejects</em> it rather than silently
    /// coercing it to something else — reasonable, since ACE is strict enough about these names to reject
    /// even <c>DATETIME2(7)</c> and <c>DATETIMEEXTENDED</c> as syntax errors. If a guarded test ever fails on
    /// an older engine instead of skipping, this assumption is where to look.</para>
    /// </remarks>
    public static bool SupportsColumnType(string sourceDatabase, string typeName)
    {
        lock (ColumnTypeSupport)
        {
            if (ColumnTypeSupport.TryGetValue(typeName, out bool known)) return known;

            bool supported;
            string path = TemporaryDatabase.CopyPath(sourceDatabase, "ace-typeprobe-");
            try
            {
                using OleDbConnection connection = Open(path);
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = $"CREATE TABLE AceTypeProbe (V {typeName})";
                command.ExecuteNonQuery();
                supported = true;
            }
            catch (Exception)
            {
                supported = false;
            }
            finally { TemporaryDatabase.Delete(path); }

            ColumnTypeSupport[typeName] = supported;
            return supported;
        }
    }

    /// <summary>The skip reason for a type this ACE cannot create.</summary>
    public static string UnsupportedColumnTypeReason(string typeName) =>
        $"The installed ACE cannot create a {typeName} column - it predates the type. " +
        $"DATETIME2 needs ACE 17 (Access 2019+/365); BIGINT needs ACE 16 (Access 2016).";
}
