using System;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;

[Flags]
public enum AccessProviderTypeVariation
{
    None = 0,
    X86 = 1 << 0,
    X64 = 1 << 1,
    Odbc = 1 << 2,
    OleDb = 1 << 3,
    All = -1,
}