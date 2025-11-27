using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query;

public class AdHocJsonQueryJetTest(NonSharedFixture fixture) : AdHocJsonQueryJetTestBase(fixture)
{
    public override async Task Read_enum_property_with_legacy_values(bool async)
    {
        var exception = await Assert.ThrowsAsync<Exception>(() => base.Read_enum_property_with_legacy_values_core(async));
    }

    protected override string JsonColumnType
        => "nvarchar(max)";
}