using LibRed;
using LibRed.Engine;
using LibRed.Formats;
using Xunit;

namespace LibRed.Engine.Tests;

public class DdlDmlTests
{
    private static string CopyToTemp()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "libred-ddl-");
        return path;
    }

    // BIGINT (Large Number) and DATETIME2 (Date/Time Extended) arrived in DIFFERENT format versions: BIGINT
    // needs Access 2016 (ACE 16, version byte 0x05), DATETIME2 needs Access 2019+ (ACE 17, 0x06) — verified
    // against files authored with each feature. Northwind is ACE 12 (0x02), so using either type forces the
    // file's hand, and LibRed does what Access does: raise the version rather than refuse the DDL. Verified by
    // having ACE add a Date/Time Extended column to an ACE 12 database and diffing — the version byte moved,
    // and for DATETIME2 it is the whole upgrade (docs/format/page-00-database.md).
    [Theory]
    [InlineData("BIGINT", JetVersion.Version16_2016, 0x05)]
    [InlineData("DATETIME2", JetVersion.Version17_2019, 0x06)]
    public void Creating_a_column_of_a_newer_type_raises_the_file_format(
        string typeName, JetVersion expected, byte expectedByte)
    {
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.Equal(JetVersion.Version12_2007, db.Format.Version);

                new QueryEngine(db).ExecuteNonQuery($"CREATE TABLE `T` (`Id` INTEGER PRIMARY KEY, `V` {typeName})");

                Assert.Equal(expected, db.Format.Version);
                Assert.Equal(expectedByte, db.DefinitionPage.JetVersion);   // page 0 was re-read, not left stale
            }

            Assert.Equal(expectedByte, VersionByte(path));                  // and it reached the file
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Altering_a_column_to_bigint_raises_the_file_format()
    {
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("CREATE TABLE `T` (`Id` INTEGER PRIMARY KEY, `V` INTEGER)");
                Assert.Equal(JetVersion.Version12_2007, db.Format.Version);

                e.ExecuteNonQuery("ALTER TABLE `T` ALTER COLUMN `V` BIGINT");
                Assert.Equal(JetVersion.Version16_2016, db.Format.Version);
            }

            Assert.Equal(0x05, VersionByte(path));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The upgrade rides in the statement's own transaction, so a CREATE that fails after the format was
    // raised must take the raise back down with it — on disk AND in memory. Getting only the disk half right
    // would leave the open database claiming a version its file does not have, and the next DATETIME2 column
    // would then be written into a file that never got upgraded.
    [Fact]
    public void A_failed_statement_does_not_leave_the_format_raised()
    {
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("CREATE TABLE `T` (`Id` INTEGER PRIMARY KEY)");

                // Same statement needs the upgrade AND cannot succeed: the table already exists.
                Assert.ThrowsAny<Exception>(() =>
                    e.ExecuteNonQuery("CREATE TABLE `T` (`Id` INTEGER PRIMARY KEY, `V` DATETIME2)"));

                Assert.Equal(JetVersion.Version12_2007, db.Format.Version);
                Assert.Equal(0x02, db.DefinitionPage.JetVersion);
            }

            Assert.Equal(0x02, VersionByte(path));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static byte VersionByte(string path)
    {
        using var stream = File.OpenRead(path);
        stream.Seek(0x14, SeekOrigin.Begin);
        return (byte)stream.ReadByte();
    }

    // BIGINT written by LibRed rather than read from an ACE fixture, including through an index so the key
    // encoder runs on our own writes. Both extremes and both signs: the key transform is a sign-bit flip, so
    // positives alone would pass against almost any encoding.
    [Fact]
    public void Bigint_round_trips_through_libred_including_its_index()
    {
        long?[] values = [0L, 1L, -1L, 42L, -42L, long.MaxValue, long.MinValue, null];

        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("CREATE TABLE `B` (`Id` INTEGER PRIMARY KEY, `V` BIGINT NULL)");
                e.ExecuteNonQuery("CREATE INDEX `IX_B_V` ON `B` (`V`)");
                Assert.Equal(JetVersion.Version16_2016, db.Format.Version);

                for (int i = 0; i < values.Length; i++)
                    e.ExecuteNonQuery("INSERT INTO `B` (`Id`, `V`) VALUES (@id, @v)",
                        new Dictionary<string, object?> { ["id"] = i, ["v"] = values[i] });
            }

            using (var db = JetDatabase.Open(path))
            {
                var e = new QueryEngine(db);

                // ACE puts a BIGINT in the row's variable region; a column LibRed created must match, or the
                // value goes somewhere ACE would not read it from.
                Assert.False(db.OpenTable("B").Definition.FindColumn("V")!.IsFixedLength);

                var byId = e.ExecuteQuery("SELECT `Id`, `V` FROM `B` ORDER BY `Id`").Rows
                    .ToDictionary(r => Convert.ToInt32(r[0]), r => (long?)r[1]);
                Assert.Equal(values.Length, byId.Count);
                for (int i = 0; i < values.Length; i++)
                    Assert.Equal(values[i], byId[i]);

                // And the index orders them numerically rather than by raw two's-complement bytes, which is
                // the whole point of the sign-bit flip — MinValue first, not somewhere after MaxValue.
                Assert.Equal(
                    values.Where(v => v is not null).OrderBy(v => v).ToArray(),
                    e.ExecuteQuery("SELECT `V` FROM `B` WHERE `V` IS NOT NULL ORDER BY `V`")
                        .Rows.Select(r => (long?)r[0]).ToArray());
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Date/Time Extended end to end through LibRed alone — CREATE, INSERT, SELECT — on an ACE 17 file.
    // The fixture is Northwind (ACE 12) with its version byte raised to 0x06, which IS the whole upgrade: ACE
    // itself asks for nothing more (AceDateTime2UpgradeTests proves the byte is sufficient against the real
    // engine). Doing it that way rather than shipping a second fixture keeps this suite free of any Access
    // dependency, so it still runs on the Linux/macOS/ARM legs.
    //
    // The values are chosen for what the 42-byte encoding has to get right rather than for what an ordinary
    // 8-byte DATETIME could already do: 100-ns sub-second ticks, which are the reason the type exists and
    // which an OA double cannot hold; DateTime.MinValue, where both 19-digit fields pad to all zeros; the top
    // of the range at MaxValue; and a January date — the one month ACE's own OLE DB reader cannot return at
    // all. A NULL covers the null bitmap for a fixed-length column.
    [Fact]
    public void Datetime2_round_trips_through_libred_on_the_ace17_format()
    {
        (int Id, DateTime? Value)[] cases =
        [
            (1, new DateTime(2021, 3, 4, 5, 6, 7)),
            (2, new DateTime(2021, 3, 4, 5, 6, 7).AddTicks(1234567)),
            (3, new DateTime(2021, 1, 15, 5, 6, 7)),
            (4, DateTime.MinValue),
            (5, DateTime.MaxValue),
            (6, null),
        ];

        string path = CopyToTemp();
        try
        {
            SetVersionByte(path, 0x06);

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("CREATE TABLE `E` (`Id` INTEGER PRIMARY KEY, `V` DATETIME2 NULL)");
                foreach ((int id, DateTime? value) in cases)
                    e.ExecuteNonQuery("INSERT INTO `E` (`Id`, `V`) VALUES (@id, @v)",
                        new Dictionary<string, object?> { ["id"] = id, ["v"] = value });
            }

            // Reopened, so the values are read back off the page rather than out of anything still in memory.
            using (var db = JetDatabase.Open(path))
            {
                var rows = new QueryEngine(db).ExecuteQuery("SELECT `Id`, `V` FROM `E` ORDER BY `Id`").Rows
                    .ToDictionary(r => Convert.ToInt32(r[0]), r => (DateTime?)r[1]);

                Assert.Equal(cases.Length, rows.Count);
                foreach ((int id, DateTime? value) in cases)
                    Assert.Equal(value, rows[id]);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Raises a copied file to the ACE 17 format. Page 0 offset 0x14 is the entire upgrade — see
    /// docs/format/page-00-database.md and AceDateTime2UpgradeTests.</summary>
    private static void SetVersionByte(string path, byte version)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);
        stream.Seek(0x14, SeekOrigin.Begin);
        stream.WriteByte(version);
    }

    // Creating at a chosen format, rather than upgrading someone else's file. The default stays ACE 12 so an
    // ordinary database keeps opening in every Access from 2007; asking for ACE 17 up front is how you get a
    // DATETIME2 database without the file ever having been at an older version.
    //
    // Both arms end up able to hold the type — that is the point of the upgrade — but only one of them was
    // ever ACE 12, and a caller who says Version17_2019 should not have to rely on a later DDL side effect.
    [Theory]
    [InlineData(JetVersion.Version12_2007, 0x02, 0x06)]
    [InlineData(JetVersion.Version17_2019, 0x06, 0x06)]
    public void A_natively_created_database_is_stamped_at_the_requested_format(
        JetVersion version, byte createdByte, byte afterDatetime2)
    {
        var value = new DateTime(2021, 3, 4, 5, 6, 7).AddTicks(1234567);

        string path = TemporaryDatabase.CreatePath("libred-create-");
        File.Delete(path);   // CreateDatabase synthesises the file and refuses an existing one
        try
        {
            LibRed.Data.LibRedConnection.CreateDatabase($"Data Source={path}", version: version);
            Assert.Equal(createdByte, VersionByte(path));

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("CREATE TABLE `E` (`Id` INTEGER PRIMARY KEY, `V` DATETIME2 NULL)");
                e.ExecuteNonQuery("INSERT INTO `E` (`Id`, `V`) VALUES (1, @v)",
                    new Dictionary<string, object?> { ["v"] = value });

                Assert.Equal(value, e.ExecuteQuery("SELECT `V` FROM `E`").Rows.Single()[0]);
            }

            Assert.Equal(afterDatetime2, VersionByte(path));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Memo_column_round_trips_including_null_and_a_long_value()
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            engine.ExecuteNonQuery("CREATE TABLE `M` (`Id` INTEGER PRIMARY KEY, `Note` LONGCHAR NULL)");
            engine.ExecuteNonQuery("INSERT INTO `M` (`Id`, `Note`) VALUES (1, 'hello memo world')");
            engine.ExecuteNonQuery("INSERT INTO `M` (`Id`, `Note`) VALUES (2, NULL)");
            string longText = new string('x', 500); // still inline (fits the page), exercises >255
            engine.ExecuteNonQuery($"INSERT INTO `M` (`Id`, `Note`) VALUES (3, '{longText}')");

            var rows = engine.ExecuteQuery("SELECT `Id`, `Note` FROM `M` ORDER BY `Id`").Rows.ToList();
            Assert.Equal("hello memo world", rows[0][1]);
            Assert.Null(rows[1][1]);
            Assert.Equal(longText, rows[2][1]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Ef_generated_create_table_shape_parses_and_round_trips()
    {
        // The exact shape EF Core's Jet migrations generator emits: backtick identifiers, an explicit
        // NULL / NOT NULL nullability marker per column, and a named table-level PRIMARY KEY.
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            engine.ExecuteNonQuery(
                "CREATE TABLE `Widgets` (\n" +
                "    `Id` integer NOT NULL,\n" +
                "    `Name` varchar(255) NULL,\n" +
                "    CONSTRAINT `PK_Widgets` PRIMARY KEY (`Id`)\n" +
                ")");
            Assert.Equal(1, engine.ExecuteNonQuery("INSERT INTO `Widgets` (`Id`, `Name`) VALUES (1, 'a')"));

            var rows = engine.ExecuteQuery("SELECT `Id`, `Name` FROM `Widgets`").Rows.ToList();
            Assert.Single(rows);
            Assert.Equal(1, Convert.ToInt32(rows[0][0]));
            Assert.Equal("a", rows[0][1]);

            var pk = Assert.Single(db.Catalog.FindTable("Widgets")!.Indexes, i => i.IsPrimaryKey);
            Assert.Equal(["Id"], pk.Columns.Select(c => c.Column.Name));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Statements_with_a_trailing_semicolon_parse()
    {
        // EF Core terminates each statement with ';'. The grammar accepts an optional trailing one.
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            engine.ExecuteNonQuery("CREATE TABLE `S` (`Id` INTEGER PRIMARY KEY, `N` TEXT(10));");
            Assert.Equal(1, engine.ExecuteNonQuery("INSERT INTO `S` (`Id`, `N`) VALUES (1, 'a');"));

            var rows = engine.ExecuteQuery("SELECT `Id`, `N` FROM `S`;").Rows.ToList();
            Assert.Single(rows);
            Assert.Equal(1, Convert.ToInt32(rows[0][0]));
            Assert.Equal("a", rows[0][1]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Autonumber_is_generated_for_inserts_that_omit_the_column()
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            engine.ExecuteNonQuery("CREATE TABLE `Auto` (`Id` COUNTER PRIMARY KEY, `V` TEXT(20))");
            Assert.Equal(1, engine.ExecuteNonQuery("INSERT INTO `Auto` (`V`) VALUES ('a')")); // -> Id 1
            engine.ExecuteNonQuery("INSERT INTO `Auto` (`V`) VALUES ('b')");                  // -> 2
            engine.ExecuteNonQuery("INSERT INTO `Auto` (`Id`, `V`) VALUES (10, 'ten')");       // explicit, jumps
            engine.ExecuteNonQuery("INSERT INTO `Auto` (`V`) VALUES ('after')");              // -> 11 (continues)

            var ids = engine.ExecuteQuery("SELECT `Id` FROM `Auto` ORDER BY `Id`")
                .Rows.Select(r => Convert.ToInt32(r[0])).ToList();
            Assert.Equal([1, 2, 10, 11], ids);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Create_insert_select_round_trips_through_sql()
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            engine.ExecuteNonQuery(
                "CREATE TABLE `Widget` (`Id` INTEGER PRIMARY KEY, `Name` TEXT(100), `Price` CURRENCY)");

            Assert.Equal(1, engine.ExecuteNonQuery(
                "INSERT INTO `Widget` (`Id`, `Name`, `Price`) VALUES (1, 'Gizmo', 9.99)"));
            Assert.Equal(1, engine.ExecuteNonQuery(
                "INSERT INTO `Widget` (`Id`, `Name`, `Price`) VALUES (2, 'Sprocket', 14.5)"));

            var rs = engine.ExecuteQuery("SELECT `Id`, `Name`, `Price` FROM `Widget` ORDER BY `Id`");
            var rows = rs.Rows.ToList();

            Assert.Equal(["Id", "Name", "Price"], rs.ColumnNames);
            Assert.Equal(2, rows.Count);
            Assert.Equal(1, Convert.ToInt32(rows[0][0]));
            Assert.Equal("Gizmo", rows[0][1]);
            Assert.Equal(9.99m, Convert.ToDecimal(rows[0][2]));
            Assert.Equal("Sprocket", rows[1][1]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Insert_with_parameters_round_trips()
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            engine.ExecuteNonQuery("CREATE TABLE `P` (`Id` INTEGER PRIMARY KEY, `Name` TEXT(50))");
            engine.ExecuteNonQuery("INSERT INTO `P` (`Id`, `Name`) VALUES (@id, @name)",
                new Dictionary<string, object?> { ["@id"] = 7, ["@name"] = "param" });

            var only = Assert.Single(engine.ExecuteQuery("SELECT `Name` FROM `P` WHERE `Id` = 7").Rows);
            Assert.Equal("param", only[0]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Insert_round_trips_nulls_booleans_negative_numbers_and_double_quoted_text()
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            engine.ExecuteNonQuery(
                "CREATE TABLE `Mixed` (`Id` INTEGER PRIMARY KEY, `Name` TEXT(80), `Qty` INTEGER2, `Price` CURRENCY, `Flag` YESNO)");
            Assert.Equal(1, engine.ExecuteNonQuery(
                "INSERT INTO `Mixed` (`Id`, `Name`, `Qty`, `Price`, `Flag`) VALUES (1, \"O'Brien & Sons\", -12, -42.75, true)"));
            Assert.Equal(1, engine.ExecuteNonQuery(
                "INSERT INTO `Mixed` (`Id`, `Name`, `Flag`) VALUES (2, NULL, false)"));

            var rows = engine.ExecuteQuery(
                "SELECT `Id`, `Name`, `Qty`, `Price`, `Flag`, `Name` & '-checked' AS `Label` FROM `Mixed` ORDER BY `Id`")
                .Rows.ToList();

            Assert.Equal(2, rows.Count);
            Assert.Equal("O'Brien & Sons", rows[0][1]);
            Assert.Equal(-12, Convert.ToInt32(rows[0][2]));
            Assert.Equal(-42.75m, Convert.ToDecimal(rows[0][3]));
            Assert.True((bool)rows[0][4]!);
            Assert.Equal("O'Brien & Sons-checked", rows[0][5]);

            Assert.Null(rows[1][1]);
            Assert.Null(rows[1][2]);
            Assert.Null(rows[1][3]);
            Assert.False((bool)rows[1][4]!);
            Assert.Equal("-checked", rows[1][5]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Width_suffixed_and_two_word_type_aliases_work()
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            // integer4 = Int32, integer2 = Int16, integer1 = Byte, and the two-word CHARACTER VARYING.
            engine.ExecuteNonQuery(
                "CREATE TABLE `Aliased` (`Id` INTEGER4 PRIMARY KEY, `Small` INTEGER2, `Tiny` INTEGER1, `Label` CHARACTER VARYING(40))");
            engine.ExecuteNonQuery(
                "INSERT INTO `Aliased` (`Id`, `Small`, `Tiny`, `Label`) VALUES (1, 200, 7, 'hi')");

            var only = Assert.Single(engine.ExecuteQuery("SELECT `Id`, `Small`, `Tiny`, `Label` FROM `Aliased`").Rows);
            Assert.Equal(1, Convert.ToInt32(only[0]));
            Assert.Equal(200, Convert.ToInt32(only[1]));
            Assert.Equal(7, Convert.ToInt32(only[2]));
            Assert.Equal("hi", only[3]);

            var def = db.Catalog.FindTable("Aliased")!;
            Assert.Equal(LibRed.Catalog.JetDataType.Int16, def.FindColumn("Small")!.Type);
            Assert.Equal(LibRed.Catalog.JetDataType.Byte, def.FindColumn("Tiny")!.Type);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Boolean_alias_logical1_maps_to_boolean()
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            new QueryEngine(db).ExecuteNonQuery("CREATE TABLE `B` (`Id` INTEGER, `Flag` LOGICAL1)");
            Assert.Equal(LibRed.Catalog.JetDataType.Boolean, db.Catalog.FindTable("B")!.FindColumn("Flag")!.Type);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ANSI/SQL-Server type-name aliases EF Core's relational defaults emit that weren't handled:
    //   DOUBLE PRECISION → Double (two-word ANSI spelling), NTEXT → Memo (Unicode long text),
    //   DEC → FixedPoint (ANSI decimal), GENERAL → Ole (Access long-binary alias).
    [Theory]
    [InlineData("DOUBLE PRECISION", LibRed.Catalog.JetDataType.Double)]
    [InlineData("NTEXT", LibRed.Catalog.JetDataType.Memo)]
    [InlineData("DEC", LibRed.Catalog.JetDataType.FixedPoint)]
    [InlineData("GENERAL", LibRed.Catalog.JetDataType.Ole)]
    public void Ansi_type_aliases_map_to_the_right_storage_type(string typeName, LibRed.Catalog.JetDataType expected)
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            new QueryEngine(db).ExecuteNonQuery($"CREATE TABLE `D` (`Id` INTEGER, `V` {typeName})");
            Assert.Equal(expected, db.Catalog.FindTable("D")!.FindColumn("V")!.Type);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Create_without_primary_key_is_allowed()
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            engine.ExecuteNonQuery("CREATE TABLE `NoPk` (`A` INTEGER, `B` TEXT(20))");
            engine.ExecuteNonQuery("INSERT INTO `NoPk` (`A`, `B`) VALUES (1, 'x')");

            var only = Assert.Single(engine.ExecuteQuery("SELECT `A`, `B` FROM `NoPk`").Rows);
            Assert.Equal(1, Convert.ToInt32(only[0]));
            Assert.Equal("x", only[1]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
