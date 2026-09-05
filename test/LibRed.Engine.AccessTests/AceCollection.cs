using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Groups the classes that drive the real ACE OLE DB provider, so xunit runs them one after another. ACE
/// faults under concurrent use — two of these classes running in parallel throw
/// <c>SEHException: External component has thrown an exception</c> at the same millisecond and take the test
/// process down with <c>0xC0000005</c>. Measured: the five classes alone crashed 3 of 3 back-to-back runs
/// without this, while the other ~950 tests were clean 3 of 3; with it, three back-to-back full-suite runs
/// passed.
/// </summary>
/// <remarks>This does <b>not</b> disable parallelism. Every other class in the assembly still runs in
/// parallel around this collection — only these five are serialized, and only against each other. Add the
/// attribute to any new class that opens an ACE OLE DB connection.</remarks>
[CollectionDefinition(Name)]
public sealed class AceCollection
{
    public const string Name = "ACE OLE DB";
}
