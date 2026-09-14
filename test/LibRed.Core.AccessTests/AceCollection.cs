using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Groups the classes that drive the real ACE OLE DB provider, so xunit runs them one after another. ACE
/// faults under concurrent use — two such classes running in parallel throw
/// <c>SEHException: External component has thrown an exception</c> at the same millisecond and take the test
/// process down with <c>0xC0000005</c>. The same collection as LibRed.Engine.AccessTests uses.
/// </summary>
/// <remarks>Every class in this project reaches ACE, so every class carries the attribute. Add it to any new
/// one.</remarks>
[CollectionDefinition(Name)]
public sealed class AceCollection
{
    public const string Name = "ACE OLE DB";
}
