// Explicit usings, not implicit ones: this file has been globbed wholesale into projects that build with
// ImplicitUsings disabled, and the cost of being defensive here is two lines.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using LibRed;
using Xunit;

namespace LibRed.Tests.Shared;

/// <summary>Owns a test database copy, its optional LibRed handle, and deterministic cleanup.</summary>
public sealed class TemporaryDatabase : IDisposable
{
    private static readonly ConcurrentDictionary<string, byte> TrackedPaths =
        new(StringComparer.OrdinalIgnoreCase);

    // Databases opened through OpenTracked, which the caller never disposes (a static Fresh()-style helper
    // that hands back only a QueryEngine has nowhere to put the handle). Windows will not delete a file with
    // an open handle, so without this the copy survives every cleanup path and leaks permanently.
    private static readonly ConcurrentBag<JetDatabase> TrackedDatabases = [];

    // Per-test buckets, so a copy is released when its test ends rather than at process exit. xunit gives a
    // per-test identity (TestContext.Current.Test) but no disposal hook, so the release is driven by the test
    // class's own Dispose — see TempDatabaseTest. Work outside a test (a fixture, a static initializer) finds
    // no current test and falls back to the process-exit sweep.
    private static readonly ConcurrentDictionary<object, TestResources> PerTest = [];

    private sealed class TestResources
    {
        public ConcurrentBag<JetDatabase> Databases { get; } = [];
        public ConcurrentDictionary<string, byte> Paths { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static TestResources? CurrentTest =>
        TestContext.Current.Test is object test ? PerTest.GetOrAdd(test, _ => new TestResources()) : null;

    /// <summary>Closes and deletes everything the currently running test copied or opened. Called from
    /// <see cref="TempDatabaseTest.Dispose"/>, which xunit runs after each test.</summary>
    public static void ReleaseCurrentTest()
    {
        if (TestContext.Current.Test is not object test) return;
        if (!PerTest.TryRemove(test, out TestResources? resources)) return;

        // Close before deleting: Windows will not delete a file that still has an open handle.
        foreach (JetDatabase database in resources.Databases)
            try { database.Dispose(); } catch (Exception) { /* already closed, or the test left it faulted */ }

        foreach (string path in resources.Paths.Keys)
            Delete(path);
    }

    private bool _preserve;
    private JetDatabase? _database;

    private TemporaryDatabase(string path) => Path = path;

    public string Path { get; }

    public JetDatabase Database => _database
        ?? throw new InvalidOperationException("The temporary database has not been opened.");

    public static TemporaryDatabase CopyOf(string source, string prefix)
    {
        string path = CopyPath(source, prefix);
        return new TemporaryDatabase(path);
    }

    /// <summary>Creates and tracks a database copy for legacy helpers that return a live object and therefore
    /// cannot hand a disposable lease back to the caller. Prefer <see cref="CopyOf"/> in ordinary test bodies.</summary>
    public static string CopyPath(string source, string prefix, bool overwrite = false)
    {
        string path = NewPath(prefix, System.IO.Path.GetExtension(source));
        File.Copy(source, path, overwrite);
        Track(path);
        return path;
    }

    /// <summary>Opens a tracked copy and keeps the handle, for the static <c>Fresh()</c>-style helpers that
    /// return only a <c>QueryEngine</c> and so have nowhere to keep the database. The handle is closed and the
    /// file deleted by <see cref="RegisterProcessCleanup"/>. Prefer <see cref="CopyOf"/> + <see cref="Open"/>
    /// in a <c>using</c> where the test body can hold the lease — this exists so an abandoned handle leaks for
    /// the length of the run rather than forever.</summary>
    public static JetDatabase OpenTracked(string path, bool readOnly = false, string? password = null)
    {
        JetDatabase database = JetDatabase.Open(path, readOnly, password);
        Track(path);
        // Per-test bucket AND the process-wide bag, exactly as Track does for paths: the bucket closes the
        // handle when the test ends, the bag is the backstop for a class that has not opted into
        // TempDatabaseTest — without it that handle would never close and its file could never be deleted.
        // Disposing a JetDatabase twice is a no-op, so the overlap is free.
        CurrentTest?.Databases.Add(database);
        TrackedDatabases.Add(database);
        return database;
    }

    /// <summary>Reserves and tracks a unique, currently nonexistent path for a database creator.</summary>
    public static string CreatePath(string prefix, string extension = ".accdb")
    {
        string path = NewPath(prefix, extension);
        Track(path);
        return path;
    }

    /// <summary>Best-effort deletion, for the <c>finally</c> blocks that clean a test up. It deliberately
    /// never throws: a `finally` that throws REPLACES the assertion failure that is the real result of the
    /// test, turning "expected 4, got 3" into "the file was locked". A copy that resists deletion stays
    /// tracked and is swept at process exit instead.</summary>
    public static void Delete(string path)
    {
        if (!File.Exists(path))
        {
            TrackedPaths.TryRemove(path, out _);
            return;
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                File.Delete(path);
                TrackedPaths.TryRemove(path, out _);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(25 * (attempt + 1));
            }
        }
    }

    public JetDatabase Open(bool readOnly = false, string? password = null)
    {
        if (_database is not null) throw new InvalidOperationException("The temporary database is already open.");
        return _database = JetDatabase.Open(Path, readOnly, password);
    }

    /// <summary>Leaves the file behind for post-failure inspection and returns its path.</summary>
    public string Preserve()
    {
        _preserve = true;
        return Path;
    }

    public void Dispose()
    {
        _database?.Dispose();
        _database = null;
        if (_preserve) TrackedPaths.TryRemove(Path, out _);
        else Delete(Path);   // best-effort by contract — see Delete
    }

    /// <summary>Records a path against the running test when there is one (released at the end of that test)
    /// and always against the process-wide set, which is the backstop for anything the per-test release
    /// misses.</summary>
    private static void Track(string path)
    {
        CurrentTest?.Paths.TryAdd(path, 0);
        TrackedPaths.TryAdd(path, 0);
    }

    private static string NewPath(string prefix, string extension) => System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), $"{prefix.TrimEnd('-')}-{Guid.NewGuid():N}{extension}");

    [ModuleInitializer]
    internal static void RegisterProcessCleanup()
        => AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            // Close first, delete second: a file with a live handle cannot be deleted on Windows, so skipping
            // this leaves every OpenTracked copy behind — which is how ~22 GB of Northwind copies once
            // accumulated in %TEMP%.
            foreach (JetDatabase database in TrackedDatabases)
                try { database.Dispose(); } catch (Exception) { /* already closed or mid-fault */ }

            foreach (string path in TrackedPaths.Keys)
                Delete(path);   // best-effort; process exit cannot report a failure anyway
        };
}
