using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;

namespace EntityFrameworkCore.LibRed.FunctionalTests.Query;

public class AdHocJsonQueryLibRedTest(NonSharedFixture fixture) : AdHocJsonQueryLibRedTestBase(fixture)
{
    public override async Task Read_enum_property_with_legacy_values(bool async)
    {
        var exception = await Assert.ThrowsAsync<Exception>(() => base.Read_enum_property_with_legacy_values_core(async));
    }

    protected override string JsonColumnType
        => "nvarchar(max)";
}