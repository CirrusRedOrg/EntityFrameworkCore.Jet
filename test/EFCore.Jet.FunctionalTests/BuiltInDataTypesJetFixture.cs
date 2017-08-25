using System;
using System.Linq;
using System.Reflection;
using EntityFramework.Jet.FunctionalTests.Utilities;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFramework.Jet.FunctionalTests
{
    public class BuiltInDataTypesJetFixture : BuiltInDataTypesFixtureBase
    {
        private readonly DbContextOptions _options;
        private readonly JetTestStore _testStore;

        public TestSqlLoggerFactory TestSqlLoggerFactory { get; } = new TestSqlLoggerFactory();

        public BuiltInDataTypesJetFixture()
        {
            _testStore = JetTestStore.CreateScratch(createDatabase: true);

            var serviceProvider = new ServiceCollection()
                .AddEntityFrameworkJet()
                .AddSingleton(TestModelSource.GetFactory(OnModelCreating))
                .AddSingleton<ILoggerFactory>(TestSqlLoggerFactory)
                .BuildServiceProvider();

            _options = new DbContextOptionsBuilder()
                .UseJet(_testStore.Connection, b => b.ApplyConfiguration())
                .EnableSensitiveDataLogging()
                .UseInternalServiceProvider(serviceProvider)
                .Options;

            using (var context = new DbContext(_options))
            {
                context.Database.EnsureCreated();
            }
        }

        public override DbContext CreateContext()
        {
            var context = new DbContext(_options);
            context.Database.UseTransaction(_testStore.Transaction);
            return context;
        }

        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            MakeRequired<MappedDataTypes>(modelBuilder);

            modelBuilder.Entity<BuiltInDataTypes>(b =>
            {
                b.Ignore(dt => dt.TestUnsignedInt16);
                b.Ignore(dt => dt.TestUnsignedInt32);
                b.Ignore(dt => dt.TestUnsignedInt64);
                b.Ignore(dt => dt.TestCharacter);
                b.Ignore(dt => dt.TestSignedByte);
                b.Ignore(dt => dt.TestDateTimeOffset);
                b.Ignore(dt => dt.TestTimeSpan);
                b.Property(dt => dt.TestDecimal).HasColumnType("decimal(18,2)");
            });

            modelBuilder.Entity<BuiltInNullableDataTypes>(b =>
            {
                b.Ignore(dt => dt.TestNullableUnsignedInt16);
                b.Ignore(dt => dt.TestNullableUnsignedInt32);
                b.Ignore(dt => dt.TestNullableUnsignedInt64);
                b.Ignore(dt => dt.TestNullableCharacter);
                b.Ignore(dt => dt.TestNullableSignedByte);
                b.Ignore(dt => dt.TestNullableDateTimeOffset);
                b.Ignore(dt => dt.TestNullableTimeSpan);
            });

            modelBuilder.Entity<MappedDataTypes>(b =>
            {
                b.HasKey(e => e.Int);
                b.Property(e => e.Int).ValueGeneratedNever();
            });

            modelBuilder.Entity<MappedNullableDataTypes>(b =>
            {
                b.HasKey(e => e.Int);
                b.Property(e => e.Int)
                    .ValueGeneratedNever();
            });

            modelBuilder.Entity<MappedSizedDataTypes>()
                .Property(e => e.Id)
                .ValueGeneratedNever();

            modelBuilder.Entity<MappedScaledDataTypes>()
                .Property(e => e.Id)
                .ValueGeneratedNever();

            modelBuilder.Entity<MappedPrecisionAndScaledDataTypes>()
                .Property(e => e.Id)
                .ValueGeneratedNever();

            MapColumnTypes<MappedDataTypes>(modelBuilder);
            MapColumnTypes<MappedNullableDataTypes>(modelBuilder);

            MapSizedColumnTypes<MappedSizedDataTypes>(modelBuilder);
            MapSizedColumnTypes<MappedScaledDataTypes>(modelBuilder);
            MapPreciseColumnTypes<MappedPrecisionAndScaledDataTypes>(modelBuilder);

            modelBuilder.Entity<MappedDataTypesWithIdentity>(b => { b.HasKey(e => e.Id); });

            modelBuilder.Entity<MappedNullableDataTypesWithIdentity>(b => { b.HasKey(e => e.Id); });

            modelBuilder.Entity<MappedSizedDataTypesWithIdentity>()
                .Property(e => e.Id);

            modelBuilder.Entity<MappedScaledDataTypesWithIdentity>()
                .Property(e => e.Id);

            modelBuilder.Entity<MappedPrecisionAndScaledDataTypesWithIdentity>()
                .Property(e => e.Id);

            MapColumnTypes<MappedDataTypesWithIdentity>(modelBuilder);
            MapColumnTypes<MappedNullableDataTypesWithIdentity>(modelBuilder);

            MapSizedColumnTypes<MappedSizedDataTypesWithIdentity>(modelBuilder);
            MapSizedColumnTypes<MappedScaledDataTypesWithIdentity>(modelBuilder);
            MapPreciseColumnTypes<MappedPrecisionAndScaledDataTypesWithIdentity>(modelBuilder);
        }

        private static void MapColumnTypes<TEntity>(ModelBuilder modelBuilder) where TEntity : class
        {
            var entityType = modelBuilder.Entity<TEntity>().Metadata;

            foreach (var propertyInfo in entityType.ClrType.GetTypeInfo().DeclaredProperties.Where(p => p.Name != "Id"))
            {
                var typeName = propertyInfo.Name;

                if (typeName.EndsWith("Max") && (typeName.StartsWith("N")))
                {
                    typeName = "ntext";
                }

                if (typeName.EndsWith("Max") && (typeName.StartsWith("V") || typeName.StartsWith("B")))
                {
                    typeName = "image";
                }

                typeName = typeName.Replace('_', ' ');

                entityType.GetOrAddProperty(propertyInfo).Relational().ColumnType = typeName;
            }
        }

        private static void MapSizedColumnTypes<TEntity>(ModelBuilder modelBuilder) where TEntity : class
        {
            var entityType = modelBuilder.Entity<TEntity>().Metadata;

            foreach (var propertyInfo in entityType.ClrType.GetTypeInfo().DeclaredProperties
                .Where(p => p.Name != "Id" && p.Name != "Int" && p.Name != "Float"))
            {
                entityType.GetOrAddProperty(propertyInfo).Relational().ColumnType = propertyInfo.Name.Replace('_', ' ') + "(3)";
            }
        }

        private static void MapPreciseColumnTypes<TEntity>(ModelBuilder modelBuilder) where TEntity : class
        {
            var entityType = modelBuilder.Entity<TEntity>().Metadata;

            foreach (var propertyInfo in entityType.ClrType.GetTypeInfo().DeclaredProperties
                .Where(p => p.Name != "Id" && p.Name != "Int"))
            {
                entityType.GetOrAddProperty(propertyInfo).Relational().ColumnType = propertyInfo.Name.Replace('_', ' ') + "(5, 2)";
            }
        }

        public override void Dispose() => _testStore.Dispose();

        public override bool SupportsBinaryKeys => true;

        public override DateTime DefaultDateTime => new DateTime(1753, 1, 1);
    }

    public class MappedDataTypes
    {
        public int Int { get; set; }
        public long Bigint { get; set; }
        public short Smallint { get; set; }
        public byte Tinyint { get; set; }
        public bool Bit { get; set; }
        public decimal Money { get; set; }
        public double Float { get; set; }
        public float Real { get; set; }
        public DateTime Datetime { get; set; }
        public string NvarcharMax { get; set; }
        public string National_char_varyingMax { get; set; }
        public string National_character_varyingMax { get; set; }
        public string Ntext { get; set; }
        public byte[] VarbinaryMax { get; set; }
        public byte[] Image { get; set; }
        public decimal Decimal { get; set; }
        public decimal Dec { get; set; }
        public decimal Numeric { get; set; }
    }

    public class MappedSizedDataTypes
    {
        public int Id { get; set; }
        public string Nchar { get; set; }
        public string National_character { get; set; }
        public string Nvarchar { get; set; }
        public string National_char_varying { get; set; }
        public string National_character_varying { get; set; }
        public byte[] Binary { get; set; }
        public byte[] Varbinary { get; set; }
    }

    public class MappedScaledDataTypes
    {
        public int Id { get; set; }
        public float Float { get; set; }
        public decimal Decimal { get; set; }
        public decimal Dec { get; set; }
        public decimal Numeric { get; set; }
    }

    public class MappedPrecisionAndScaledDataTypes
    {
        public int Id { get; set; }
        public decimal Decimal { get; set; }
        public decimal Dec { get; set; }
        public decimal Numeric { get; set; }
    }

    public class MappedNullableDataTypes
    {
        public int? Int { get; set; }
        public long? Bigint { get; set; }
        public short? Smallint { get; set; }
        public byte? Tinyint { get; set; }
        public bool? Bit { get; set; }
        public decimal? Money { get; set; }
        public double? Float { get; set; }
        public float? Real { get; set; }
        public DateTime? Datetime { get; set; }
        public string NvarcharMax { get; set; }
        public string National_char_varyingMax { get; set; }
        public string National_character_varyingMax { get; set; }
        public string Ntext { get; set; }
        public byte[] VarbinaryMax { get; set; }
        public byte[] Image { get; set; }
        public decimal? Decimal { get; set; }
        public decimal? Dec { get; set; }
        public decimal? Numeric { get; set; }
    }

    public class MappedDataTypesWithIdentity
    {
        public int Id { get; set; }

        public int Int { get; set; }
        public long Bigint { get; set; }
        public short Smallint { get; set; }
        public byte Tinyint { get; set; }
        public bool Bit { get; set; }
        public decimal Money { get; set; }
        public double Float { get; set; }
        public float Real { get; set; }
        public DateTime Datetime { get; set; }
        public string NvarcharMax { get; set; }
        public string National_char_varyingMax { get; set; }
        public string National_character_varyingMax { get; set; }
        public string Ntext { get; set; }
        public byte[] VarbinaryMax { get; set; }
        public byte[] Binary_varyingMax { get; set; }
        public byte[] Image { get; set; }
        public decimal Decimal { get; set; }
        public decimal Dec { get; set; }
        public decimal Numeric { get; set; }
    }

    public class MappedSizedDataTypesWithIdentity
    {
        public int Id { get; set; }
        public int Int { get; set; }

        public string Nchar { get; set; }
        public string National_character { get; set; }
        public string Nvarchar { get; set; }
        public string National_char_varying { get; set; }
        public string National_character_varying { get; set; }
        public byte[] Binary { get; set; }
        public byte[] Varbinary { get; set; }
    }

    public class MappedScaledDataTypesWithIdentity
    {
        public int Id { get; set; }
        public int Int { get; set; }

        public float Float { get; set; }
        public decimal Decimal { get; set; }
        public decimal Dec { get; set; }
        public decimal Numeric { get; set; }
    }

    public class MappedPrecisionAndScaledDataTypesWithIdentity
    {
        public int Id { get; set; }
        public int Int { get; set; }

        public decimal Decimal { get; set; }
        public decimal Dec { get; set; }
        public decimal Numeric { get; set; }
    }

    public class MappedNullableDataTypesWithIdentity
    {
        public int Id { get; set; }

        public int? Int { get; set; }
        public long? Bigint { get; set; }
        public short? Smallint { get; set; }
        public byte? Tinyint { get; set; }
        public bool? Bit { get; set; }
        public decimal? Money { get; set; }
        public double? Float { get; set; }
        public float? Real { get; set; }
        public DateTime? Datetime { get; set; }
        public string NvarcharMax { get; set; }
        public string National_char_varyingMax { get; set; }
        public string National_character_varyingMax { get; set; }
        public string Ntext { get; set; }
        public byte[] VarbinaryMax { get; set; }
        public byte[] Image { get; set; }
        public decimal? Decimal { get; set; }
        public decimal? Dec { get; set; }
        public decimal? Numeric { get; set; }
    }
}
