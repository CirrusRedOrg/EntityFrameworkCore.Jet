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

        // A test that drove DAO first may have left its temporaries to the finalizer; see ReleaseAbandonedComObjects.
        ReleaseAbandonedComObjects();

        Exception? last = null;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            foreach (string provider in Providers)
            {
                // Disposed when Open throws. The connection is a live native provider object — pooling is off
                // (OLE DB Services=-4), so nothing else reclaims it — and abandoning it to the finalizer meant
                // a test that cannot open its file leaked one per attempt, 24 across the retry loop. A suite
                // with several such tests leaks them by the hundred into a provider already known to fault
                // with 0xC0000005, which is worth not doing whether or not it is what crashes the run.
                OleDbConnection? connection = null;
                try
                {
                    string passwordPart = password is null ? "" : $"Jet OLEDB:Database Password={password};";
                    connection = new OleDbConnection(
                        $"Provider={provider};Data Source={path};{passwordPart}OLE DB Services=-4;");
                    connection.Open();

                    OleDbConnection opened = connection;
                    connection = null;   // ownership passes to the caller; the finally must not dispose it
                    return opened;
                }
                catch (Exception ex) when (ex is OleDbException or InvalidOperationException)
                {
                    last = ex;
                }
                finally
                {
                    connection?.Dispose();
                }
            }

            if (attempt + 1 < attempts)
                Thread.Sleep(40);
        }

        throw new InvalidOperationException("No Microsoft ACE OLE DB provider could open the test database.", last);
    }

    /// <summary>
    /// Releases every COM object — DAO's or the ACE OLE DB provider's — that code so far has abandoned to the
    /// finalizer, and returns only once that is done.
    /// </summary>
    /// <remarks>
    /// <para>ACE faults when two threads are inside it at once (see AceCollection), and serialising the tests
    /// does not stop that on its own. DAO is apartment-threaded, so an engine created from a test's thread lives
    /// on a COM-created thread of its own, and the probes release none of what they create. Each Workspace,
    /// Database, TableDef and Field is torn down when the finalizer gets to it — inside ACE, on that COM thread,
    /// at whatever moment a GC happens to run, which is usually in the middle of a later test that is itself
    /// inside ACE. An OLE DB object left undisposed does the same the other way round, from the finalizer thread
    /// into a later DAO call.</para>
    /// <para>What that looks like: <c>RPC_E_SERVERFAULT</c> out of a DAO call, then the next ACE test hanging
    /// until the blame collector kills the host — in a different test from run to run, because it depends on
    /// when the GC runs, and seen on CI's runners only. Doing the teardown here, at points where no test is
    /// inside ACE, is what takes it off that timing.</para>
    /// </remarks>
    public static void ReleaseAbandonedComObjects()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        // Finalizing an RCW can free what kept another one alive; the second pass collects those.
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    /// <summary>A DAO <c>DBEngine</c>, from the newest version registered in this bitness; null when there is none.
    /// Releases whatever the test has abandoned first (see <see cref="ReleaseAbandonedComObjects"/>).</summary>
    public static object? CreateDaoEngine()
    {
        ReleaseAbandonedComObjects();
        foreach (int version in new[] { 170, 160, 150, 140, 130, 120 })
        {
            Type? type = Type.GetTypeFromProgID($"DAO.DBEngine.{version}");
            if (type is null) continue;
            try { return Activator.CreateInstance(type); }
            catch (Exception) { /* registered but not instantiable in this bitness */ }
        }
        return null;
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
