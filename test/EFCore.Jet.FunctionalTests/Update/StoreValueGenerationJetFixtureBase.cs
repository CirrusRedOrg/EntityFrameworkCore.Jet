// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;

namespace EntityFrameworkCore.Jet.FunctionalTests.Update;

#nullable enable

public abstract class StoreValueGenerationJetFixtureBase : StoreValueGenerationFixtureBase
{
    private string? _cleanDataSql;

    public override void CleanData()
    {
        using var context = CreateContext();
        context.Database.ExecuteSqlRaw(_cleanDataSql ??= GetCleanDataSql());
    }

    private string GetCleanDataSql()
    {
        var context = CreateContext();
        var builder = new StringBuilder();

        var helper = context.GetService<ISqlGenerationHelper>();
        var tables = context.Model.GetEntityTypes()
            .SelectMany(e => e.GetTableMappings().Select(m => helper.DelimitIdentifier(m.Table.Name, m.Table.Schema)));

        foreach (var table in tables)
        {
            builder.AppendLine($"TRUNCATE TABLE {table};");
        }

        foreach (var sequence in context.Model.GetSequences().Select(s => helper.DelimitIdentifier(s.Name, s.Schema)))
        {
            builder.AppendLine($"ALTER SEQUENCE {sequence} RESTART WITH 1;");
        }

        return builder.ToString();
    }
}
