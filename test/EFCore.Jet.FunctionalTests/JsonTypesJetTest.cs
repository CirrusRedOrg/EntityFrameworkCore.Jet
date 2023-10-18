// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Sdk;

namespace EntityFrameworkCore.Jet.FunctionalTests;

public class JsonTypesJetTest : JsonTypesRelationalTestBase
{
    public override void Can_read_write_ulong_enum_JSON_values(EnumU64 value, string json)
    {
        if (value == EnumU64.Max)
        {
            json = """{"Prop":-1}"""; // Because ulong is converted to long on Jet
        }

        base.Can_read_write_ulong_enum_JSON_values(value, json);
    }

    public override void Can_read_write_nullable_ulong_enum_JSON_values(object? value, string json)
    {
        if (Equals(value, ulong.MaxValue))
        {
            json = """{"Prop":-1}"""; // Because ulong is converted to long on Jet
        }

        base.Can_read_write_nullable_ulong_enum_JSON_values(value, json);
    }

    public override void Can_read_write_collection_of_ulong_enum_JSON_values()
        => Can_read_and_write_JSON_value<EnumU64CollectionType, List<EnumU64>>(
            nameof(EnumU64CollectionType.EnumU64),
            new List<EnumU64>
            {
                EnumU64.Min,
                EnumU64.Max,
                EnumU64.Default,
                EnumU64.One,
                (EnumU64)8
            },
            """{"Prop":[0,-1,0,1,8]}""", // Because ulong is converted to long on Jet
            mappedCollection: true);

    public override void Can_read_write_collection_of_nullable_ulong_enum_JSON_values()
        => Can_read_and_write_JSON_value<NullableEnumU64CollectionType, List<EnumU64?>>(
            nameof(NullableEnumU64CollectionType.EnumU64),
            new List<EnumU64?>
            {
                EnumU64.Min,
                null,
                EnumU64.Max,
                EnumU64.Default,
                EnumU64.One,
                (EnumU64?)8
            },
            """{"Prop":[0,null,-1,0,1,8]}""", // Because ulong is converted to long on Jet
            mappedCollection: true);

    public override void Can_read_write_point()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_point());

    public override void Can_read_write_point_with_Z()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_point_with_Z());

    public override void Can_read_write_point_with_M()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_point_with_M());

    public override void Can_read_write_point_with_Z_and_M()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_point_with_Z_and_M());

    public override void Can_read_write_line_string()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_line_string());

    public override void Can_read_write_multi_line_string()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_multi_line_string());

    public override void Can_read_write_polygon()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_polygon());

    public override void Can_read_write_polygon_typed_as_geometry()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_polygon_typed_as_geometry());

    public override void Can_read_write_point_as_GeoJson()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_point_as_GeoJson());

    public override void Can_read_write_point_with_Z_as_GeoJson()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_point_with_Z_as_GeoJson());

    public override void Can_read_write_point_with_M_as_GeoJson()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_point_with_M_as_GeoJson());

    public override void Can_read_write_point_with_Z_and_M_as_GeoJson()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_point_with_Z_and_M_as_GeoJson());

    public override void Can_read_write_line_string_as_GeoJson()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_line_string_as_GeoJson());

    public override void Can_read_write_multi_line_string_as_GeoJson()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_multi_line_string_as_GeoJson());

    public override void Can_read_write_polygon_as_GeoJson()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_polygon_as_GeoJson());

    public override void Can_read_write_polygon_typed_as_geometry_as_GeoJson()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_polygon_typed_as_geometry_as_GeoJson());

    public override void Can_read_write_nullable_point()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_point());

    public override void Can_read_write_nullable_line_string()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_line_string());

    public override void Can_read_write_nullable_multi_line_string()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_multi_line_string());

    public override void Can_read_write_nullable_polygon()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_polygon());

    public override void Can_read_write_nullable_point_as_GeoJson()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_point_as_GeoJson());

    public override void Can_read_write_nullable_line_string_as_GeoJson()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_line_string_as_GeoJson());

    public override void Can_read_write_nullable_multi_line_string_as_GeoJson()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_multi_line_string_as_GeoJson());

    public override void Can_read_write_nullable_polygon_as_GeoJson()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_polygon_as_GeoJson());

    public override void Can_read_write_polygon_typed_as_nullable_geometry()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_polygon_typed_as_nullable_geometry());

    public override void Can_read_write_polygon_typed_as_nullable_geometry_as_GeoJson()
        // No built-in JSON support for spatial types in the Jet provider
        => Assert.Throws<InvalidOperationException>(() => base.Can_read_write_polygon_typed_as_nullable_geometry_as_GeoJson());

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var store = JetTestStore.GetOrCreate(nameof(JsonTypesJetTest));
        base.OnConfiguring(optionsBuilder.UseJet(store.ConnectionString));
    }
}
