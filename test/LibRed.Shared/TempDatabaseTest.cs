// Explicit usings — see the note in TemporaryDatabase.cs.
using System;

namespace LibRed.Tests.Shared;

/// <summary>
/// Base class for tests that copy a database into <c>%TEMP%</c>. xunit builds a fresh instance of the test
/// class for every test and disposes it when that test ends, which is the only per-test hook available —
/// <c>TestContext</c> identifies the running test but offers no place to register a disposable. So this turns
/// "the test finished" into "release what it copied".
/// </summary>
/// <remarks>
/// Inherit this from any class whose helpers call <see cref="TemporaryDatabase.OpenTracked"/> or
/// <see cref="TemporaryDatabase.CopyPath"/> without keeping the handle — the static <c>Fresh()</c> shape that
/// returns only a <c>QueryEngine</c>, where the test body has neither a path to delete nor a database to
/// close. A class that already scopes its own copies with <c>using</c> does not need it. Without it the copies
/// survive until the process exits, which is fine for a single run and 22 GB of Northwind copies over many.
/// </remarks>
public abstract class TempDatabaseTest : IDisposable
{
    public virtual void Dispose()
    {
        TemporaryDatabase.ReleaseCurrentTest();
        GC.SuppressFinalize(this);
    }
}
