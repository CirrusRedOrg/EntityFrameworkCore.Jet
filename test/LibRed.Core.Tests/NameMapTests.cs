using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// Access's Name AutoCorrect map, in both of the layouts Access keeps it in: the NameMap column of an MSysNameMap
// row and the NameMap property in the object's LvProp. The engine never maintains either; these pin that a caller
// can read and edit them, and that a map read and written back is unchanged down to the bytes nobody understands.
public class NameMapTests
{
    // An Access-written table's MSysNameMap blob: Table1, then its columns ID (Long) and Field1 (Memo). The last
    // four fixed bytes of each record are uninitialised memory — 0 on Table1's, A6 on the others.
    private static readonly byte[] Row = Convert.FromHexString(
        "05000000300000003E0000000000000085276F677377BE40AC361424EE65776D000000004BDB0DFF959AE6400000000000000000" +
        "01000000000000005400610062006C006500310000003600000000000000E51D15FE90C25B49920D5E7939D2700807000000" +
        "85276F677377BE40AC361424EE65776D04000000A60000004900440000003E000000000000009BFDAC9F26E6A3458870B1AF" +
        "8834DED60700000085276F677377BE40AC361424EE65776D0C000000A60000004600690065006C00640031000000");

    // The same table's NameMap property: the same three records in the property layout, closed by a kind-12
    // record carrying version 5.
    private static readonly byte[] Property = Convert.FromHexString(
        "0ACC0E550000000085276F677377BE40AC361424EE65776D000000004BDB0DFF959AE64000000000000000005400610062006C00" +
        "65003100000000000000E51D15FE90C25B49920D5E7939D270080700000085276F677377BE40AC361424EE65776D4900440000" +
        "00000000009BFDAC9F26E6A3458870B1AF8834DED60700000085276F677377BE40AC361424EE65776D4600690065006C006400" +
        "3100000000000000000000000000000000000000000000000C000000050000000000000000000000000000000000");

    private static readonly Guid Table1 = Guid.Parse("676f2785-7773-40be-ac36-1424ee65776d");

    // An Access-written file with one MSysNameMap row and a NameMap property, both for its Table1. Its NameMap
    // column, like every Access-written one, has an owned-pages map and no free-pages map.
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "Data", "Hungarian.accdb");

    [Fact]
    public void A_row_map_reads_as_its_records_and_writes_back_unchanged()
    {
        NameMap map = NameMap.ReadRow(Row);

        Assert.Equal(5, map.Version);
        Assert.Equal(new[] { "Table1", "ID", "Field1" }, map.Records.Select(r => r.Name));
        Assert.Equal(new[] { 0, 7, 7 }, map.Records.Select(r => r.Kind));
        Assert.Equal(new[] { 1, 4, 12 }, map.Records.Select(r => r.TypeCode));
        Assert.Equal(new[] { 0, 0xA6, 0xA6 }, map.Records.Select(r => r.Unused));
        Assert.Equal(Table1, map.Records[0].ItemGuid);
        Assert.NotNull(map.Records[0].SlotDate);
        Assert.All(map.Records.Skip(1), r => Assert.Equal(Table1, r.SlotGuid));
        Assert.Equal(Row, map.WriteRow());
    }

    [Fact]
    public void A_property_map_reads_as_the_same_records_and_writes_back_unchanged()
    {
        NameMap property = NameMap.ReadProperty(Property);
        NameMap row = NameMap.ReadRow(Row);

        Assert.Equal(5, property.Version);
        Assert.Equal(
            row.Records.Select(r => (r.ItemGuid, r.Kind, Convert.ToHexString(r.Slot), r.Name)),
            property.Records.Select(r => (r.ItemGuid, r.Kind, Convert.ToHexString(r.Slot), r.Name)));
        Assert.All(property.Records, r => Assert.Equal(0, r.TypeCode));
        Assert.Equal(Property, property.WriteProperty());
    }

    // A null GUID is an ordinary record in the property layout; only the kind closes the list.
    [Fact]
    public void A_null_guid_record_does_not_end_a_property_map()
    {
        NameMap map = new()
        {
            Records = [new NameMapRecord(Guid.Empty, 0, "Unresolved"), new NameMapRecord(Table1, 0, "Table1")],
        };

        NameMap read = NameMap.ReadProperty(map.WriteProperty());

        Assert.Equal(new[] { "Unresolved", "Table1" }, read.Records.Select(r => r.Name));
        Assert.Equal(NameMap.RowVersion, read.Version);
    }

    [Fact]
    public void An_edited_map_keeps_every_record_it_did_not_touch()
    {
        NameMap map = NameMap.ReadRow(Row);
        NameMap renamed = map with
        {
            Records = [.. map.Records.Select(r => r.Name == "Field1" ? r with { Name = "Notes" } : r)],
        };

        NameMap read = NameMap.ReadRow(renamed.WriteRow());

        Assert.Equal(new[] { "Table1", "ID", "Notes" }, read.Records.Select(r => r.Name));
        Assert.Equal(map.Records.Take(2), read.Records.Take(2), NameMapRecordComparer.Instance);
        Assert.Equal(0xA6, read.Records[2].Unused);
        NameMap property = NameMap.ReadProperty(renamed.WriteProperty());
        Assert.Equal("Notes", property.Records[2].Name);
    }

    [Fact]
    public void A_blob_in_neither_layout_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => NameMap.ReadRow(Property));
        Assert.Throws<InvalidDataException>(() => NameMap.ReadProperty(Row));
    }

    [Fact]
    public void A_database_s_name_map_row_can_be_read_and_updated()
    {
        string path = TemporaryDatabase.CopyPath(Fixture, "namemap-row-");
        try
        {
            NameMapRow row;
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                row = Assert.Single(db.ReadNameMaps());
                Assert.Equal("Table1", row.Name);
                Assert.Equal(1, row.Type);
                NameMap map = Assert.IsType<NameMap>(row.Map);
                Assert.Equal("Table1", map.Records[0].Name);

                NameMap renamed = map with
                {
                    Records = [map.Records[0] with { Name = "Renamed" }, .. map.Records.Skip(1)],
                };
                Assert.True(db.UpdateNameMap(row.ObjectGuid, renamed, "Renamed"));
                Assert.False(db.UpdateNameMap(Guid.NewGuid(), renamed));
            }

            using (var db = JetDatabase.Open(path, readOnly: true))
            {
                NameMapRow after = Assert.Single(db.ReadNameMaps());
                Assert.Equal("Renamed", after.Name);
                Assert.Equal(row.Id, after.Id);
                Assert.Equal("Renamed", after.Map!.Records[0].Name);
                Assert.Equal(row.Map!.Records.Skip(1), after.Map.Records.Skip(1), NameMapRecordComparer.Instance);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_table_s_name_map_property_can_be_read_updated_and_removed_without_touching_the_others()
    {
        string path = TemporaryDatabase.CopyPath(Fixture, "namemap-prop-");
        try
        {
            NameMap original;
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                original = Assert.IsType<NameMap>(db.ReadNameMapProperty("Table1", 1));
                Assert.Equal("Table1", original.Records[0].Name);
                NameMap renamed = original with
                {
                    Records = [original.Records[0] with { Name = "Renamed" }, .. original.Records.Skip(1)],
                };
                db.UpdateNameMapProperty("Table1", 1, renamed);
                Assert.Throws<InvalidOperationException>(() => db.ReadNameMapProperty("NoSuchTable", 1));
            }

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                NameMap after = Assert.IsType<NameMap>(db.ReadNameMapProperty("Table1", 1));
                Assert.Equal("Renamed", after.Records[0].Name);
                Assert.Equal(original.Version, after.Version);
                Assert.NotNull(TableProperty(db, "GUID"));

                db.UpdateNameMapProperty("Table1", 1, null);
                Assert.Null(db.ReadNameMapProperty("Table1", 1));
                Assert.NotNull(TableProperty(db, "GUID"));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static PropertyBlob.Property? TableProperty(JetDatabase db, string name)
    {
        var objects = db.OpenTable("MSysObjects");
        var d = objects.Definition;
        object?[] row = objects.Rows().Single(r => (string)r[d.FindColumn("Name")!.Index]! == "Table1");
        return PropertyBlob.Read((byte[])row[d.FindColumn("LvProp")!.Index]!)
            .Cast<PropertyBlob.Property?>().FirstOrDefault(p => p!.Value.IsOwnedBy("") && p.Value.Name == name);
    }

    // Records hold their slot as an array, so record equality compares it by reference.
    private sealed class NameMapRecordComparer : IEqualityComparer<NameMapRecord>
    {
        public static readonly NameMapRecordComparer Instance = new();

        public bool Equals(NameMapRecord? x, NameMapRecord? y) =>
            x is not null && y is not null && x with { Slot = NoSlot } == y with { Slot = NoSlot } && x.Slot.AsSpan().SequenceEqual(y.Slot);

        private static readonly byte[] NoSlot = [];

        public int GetHashCode(NameMapRecord obj) => obj.ItemGuid.GetHashCode();
    }
}
