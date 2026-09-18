using System.Reflection;
using Xunit;
using Xunit.v3;

[assembly: LibRed.Core.Tests.ReleaseAceObjectsAfterEachTest]

namespace LibRed.Core.Tests;

/// <summary>
/// Groups the classes that drive the real ACE OLE DB provider, so xunit runs them one after another. ACE
/// faults under concurrent use — two such classes running in parallel throw
/// <c>SEHException: External component has thrown an exception</c> at the same millisecond and take the test
/// process down with <c>0xC0000005</c>. The same collection as LibRed.Engine.AccessTests uses.
/// </summary>
/// <remarks>Every class in this project reaches ACE, so every class carries the attribute. Add it to any new
/// one. Serialising the classes is not the whole of it: see <see cref="ReleaseAceObjectsAfterEachTestAttribute"/>.</remarks>
[CollectionDefinition(Name)]
public sealed class AceCollection
{
    public const string Name = "ACE OLE DB";
}

/// <summary>
/// Releases, as each test ends, the COM objects it left to the finalizer — the DAO objects the probes never
/// release, whose locals a Debug build keeps alive to the end of the test — so their teardown inside ACE happens
/// now, with no test running, rather than at the next GC in the middle of a later one. See
/// <see cref="AceTestDatabase.ReleaseAbandonedComObjects"/> for what that collision does.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ReleaseAceObjectsAfterEachTestAttribute : BeforeAfterTestAttribute
{
    public override void After(MethodInfo methodUnderTest, IXunitTest test) =>
        AceTestDatabase.ReleaseAbandonedComObjects();
}
