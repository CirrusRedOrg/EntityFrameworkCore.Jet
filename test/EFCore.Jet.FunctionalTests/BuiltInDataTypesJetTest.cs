using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EntityFramework.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

// ReSharper disable UnusedParameter.Local
// ReSharper disable PossibleInvalidOperationException
namespace EntityFramework.Jet.FunctionalTests
{
    [SqlServerCondition(SqlServerCondition.IsNotSqlAzure)]
    public class BuiltInDataTypesJetTest : BuiltInDataTypesTestBase<BuiltInDataTypesJetFixture>
    {
        public BuiltInDataTypesJetTest(BuiltInDataTypesJetFixture fixture)
            : base(fixture)
        {
            fixture.TestSqlLoggerFactory.Clear();
        }

        [Fact(Skip = "Unsupported by JET")]
        public virtual void Can_perform_query_with_ansi_strings()
        {
            //Can_perform_query_with_ansi_strings(supportsAnsi: false);
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void Can_perform_query_with_max_length()
        {
            //base.Can_perform_query_with_max_length();
        }

        public override void Can_query_using_any_nullable_data_type()
        {
            using (var context = CreateContext())
            {
                context.Set<BuiltInNullableDataTypes>().Add(
                    new BuiltInNullableDataTypes
                    {
                        Id = 11,
                        PartitionId = 1,
                        TestNullableInt16 = -1234,
                        TestNullableInt32 = -123456789,
                        TestNullableInt64 = -123456789,
                        TestNullableDouble = -1.23456789,
                        TestNullableDecimal = -1234567890.01M,
                        TestNullableDateTime = Fixture.DefaultDateTime,
                        TestNullableDateTimeOffset = new DateTimeOffset(new DateTime(), TimeSpan.FromHours(-8.0)),
                        TestNullableTimeSpan = new TimeSpan(0, 10, 9, 8, 7),
                        TestNullableSingle = -1.234F,
                        TestNullableBoolean = true,
                        TestNullableByte = 255,
                        TestNullableUnsignedInt16 = 1234,
                        TestNullableUnsignedInt32 = 1234565789U,
                        TestNullableUnsignedInt64 = 12345678901234567890UL,
                        TestNullableCharacter = 'a',
                        TestNullableSignedByte = -128,
                        Enum64 = Enum64.SomeValue,
                        Enum32 = Enum32.SomeValue,
                        Enum16 = Enum16.SomeValue,
                        Enum8 = Enum8.SomeValue
                    });

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                var entity = context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 11);
                var entityType = context.Model.FindEntityType(typeof(BuiltInNullableDataTypes));

                short? param1 = -1234;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 11 && e.TestNullableInt16 == param1));

                int? param2 = -123456789;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 11 && e.TestNullableInt32 == param2));

                long? param3 = -123456789;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 11 && e.TestNullableInt64 == param3));

                double? param4 = -1.23456789;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 11 && e.TestNullableDouble == param4));

                decimal? param5 = -1234567890.01M;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 11 && e.TestNullableDecimal == param5));

                DateTime? param6 = Fixture.DefaultDateTime;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 11 && e.TestNullableDateTime == param6));

                TimeSpan? param8 = new TimeSpan(0, 10, 9, 8, 7);
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 11 && e.TestNullableTimeSpan == param8));

                float? param9 = -1.234F;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 11 && e.TestNullableSingle == param9));

                bool? param10 = true;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 11 && e.TestNullableBoolean == param10));

                Enum64? param12 = Enum64.SomeValue;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 11 && e.Enum64 == param12));

                Enum32? param13 = Enum32.SomeValue;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 11 && e.Enum32 == param13));

                Enum16? param14 = Enum16.SomeValue;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 11 && e.Enum16 == param14));

                Enum8? param15 = Enum8.SomeValue;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 11 && e.Enum8 == param15));
            }
        }


        public override void Can_query_with_null_parameters_using_any_nullable_data_type()
        {
            using (var context = CreateContext())
            {
                context.Set<BuiltInNullableDataTypes>().Add(
                    new BuiltInNullableDataTypes
                    {
                        Id = 711
                    });

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                var entity = context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 711);

                short? param1 = null;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 711 && e.TestNullableInt16 == param1));
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 711 && (long?)(int?)e.TestNullableInt16 == param1));

                int? param2 = null;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 711 && e.TestNullableInt32 == param2));

                long? param3 = null;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 711 && e.TestNullableInt64 == param3));

                double? param4 = null;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 711 && e.TestNullableDouble == param4));

                decimal? param5 = null;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 711 && e.TestNullableDecimal == param5));

                DateTime? param6 = null;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 711 && e.TestNullableDateTime == param6));

                DateTimeOffset? param7 = null;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 711 && e.TestNullableDateTimeOffset == param7));

                TimeSpan? param8 = null;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 711 && e.TestNullableTimeSpan == param8));

                float? param9 = null;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 711 && e.TestNullableSingle == param9));

                byte? param11 = null;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 711 && e.TestNullableByte == param11));

                Enum64? param12 = null;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 711 && e.Enum64 == param12));

                Enum32? param13 = null;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 711 && e.Enum32 == param13));

                Enum16? param14 = null;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 711 && e.Enum16 == param14));

                Enum8? param15 = null;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 711 && e.Enum8 == param15));

                char? param19 = null;
                Assert.Same(entity, context.Set<BuiltInNullableDataTypes>().Single(e => e.Id == 711 && e.TestNullableCharacter == param19));
            }
        }


        [Fact]
        public virtual void Can_query_using_any_mapped_data_type()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedNullableDataTypes>().Add(
                    new MappedNullableDataTypes
                    {
                        Int = 999,
                        Smallint = 79,
                        Bit = true,
                        Money = 81.1m,
                        Float = 83.3,
                        Real = 84.4f,
                        Datetime = new DateTime(2019, 1, 2, 14, 11, 12),
                        NvarcharMax = "don't",
                        National_char_varyingMax = "help",
                        National_character_varyingMax = "anyone!",
                        VarbinaryMax = new byte[] { 89, 90, 91, 92 },
                        Image = new byte[] { 97, 98, 99, 100 },
                        Decimal = 101.7m,
                        Dec = 102.8m,
                        Numeric = 103.9m
                    });

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                var entity = context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999);


                short? param2 = 79;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.Smallint == param2));


                bool? param4 = true;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.Bit == param4));

                decimal? param5 = 81.1m;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.Money == param5));

                double? param7a = 83.3;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.Float == param7a));

                float? param7b = 84.4f;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.Real == param7b));

                DateTime? param11 = new DateTime(2019, 1, 2, 14, 11, 12);
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.Datetime == param11));

            }
        }

        [Fact]
        public virtual void Can_query_using_any_mapped_data_types_with_nulls()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedNullableDataTypes>().Add(
                    new MappedNullableDataTypes
                    {
                        Int = 911
                    });

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                var entity = context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911);

                short? param2 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.Smallint == param2));
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && (long?)(int?)e.Smallint == param2));

                decimal? param5 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.Money == param5));

                double? param7a = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.Float == param7a));

                float? param7b = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.Real == param7b));

                DateTime? param11 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.Datetime == param11));

                string param27 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.NvarcharMax == param27));

                string param28 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.National_char_varyingMax == param28));

                string param29 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.National_character_varyingMax == param29));

                byte[] param35 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.VarbinaryMax == param35));

                byte[] param37 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.Image == param37));

                decimal? param38 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.Decimal == param38));

                decimal? param39 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.Dec == param39));

                decimal? param40 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.Numeric == param40));
            }
        }

        public override void Can_query_using_any_data_type()
        {
            using (var context = CreateContext())
            {
                context.Set<BuiltInDataTypes>().Add(
                    new BuiltInDataTypes
                    {
                        Id = 11,
                        PartitionId = 1,
                        TestInt16 = -1234,
                        TestInt32 = -123456789,
                        TestInt64 = -123456789,
                        TestDouble = -1.23456789,
                        TestDecimal = -1234567890.01M,
                        TestDateTime = Fixture.DefaultDateTime,
                        TestDateTimeOffset = new DateTimeOffset(new DateTime(), TimeSpan.FromHours(-8.0)),
                        TestTimeSpan = new TimeSpan(0, 10, 9, 8, 7),
                        TestSingle = -1.234F,
                        TestBoolean = true,
                        TestByte = 255,
                        TestUnsignedInt16 = 1234,
                        TestUnsignedInt32 = 1234565789U,
                        TestUnsignedInt64 = 12345678901234567890UL,
                        TestCharacter = 'a',
                        TestSignedByte = -128,
                        Enum64 = Enum64.SomeValue,
                        Enum32 = Enum32.SomeValue,
                        Enum16 = Enum16.SomeValue,
                        Enum8 = Enum8.SomeValue
                    });

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                var entity = context.Set<BuiltInDataTypes>().Single(e => e.Id == 11);
                var entityType = context.Model.FindEntityType(typeof(BuiltInDataTypes));

                var param1 = (short)-1234;
                Assert.Same(entity, context.Set<BuiltInDataTypes>().Single(e => e.Id == 11 && e.TestInt16 == param1));

                var param2 = -123456789;
                Assert.Same(entity, context.Set<BuiltInDataTypes>().Single(e => e.Id == 11 && e.TestInt32 == param2));

                var param3 = -123456789;
                Assert.Same(entity, context.Set<BuiltInDataTypes>().Single(e => e.Id == 11 && e.TestInt64 == param3));

                var param4 = -1.23456789;
                Assert.Same(entity, context.Set<BuiltInDataTypes>().Single(e => e.Id == 11 && e.TestDouble == param4));

                var param5 = -1234567890.01M;
                Assert.Same(entity, context.Set<BuiltInDataTypes>().Single(e => e.Id == 11 && e.TestDecimal == param5));

                var param6 = Fixture.DefaultDateTime;
                Assert.Same(entity, context.Set<BuiltInDataTypes>().Single(e => e.Id == 11 && e.TestDateTime == param6));

                if (entityType.FindProperty("TestDateTimeOffset") != null)
                {
                    var param7 = new DateTimeOffset(new DateTime(), TimeSpan.FromHours(-8.0));
                    Assert.Same(entity, context.Set<BuiltInDataTypes>().Single(e => e.Id == 11 && e.TestDateTimeOffset == param7));
                }

                if (entityType.FindProperty("TestTimeSpan") != null)
                {
                    var param8 = new TimeSpan(0, 10, 9, 8, 7);
                    Assert.Same(entity, context.Set<BuiltInDataTypes>().Single(e => e.Id == 11 && e.TestTimeSpan == param8));
                }

                var param9 = -1.234F;
                Assert.Same(entity, context.Set<BuiltInDataTypes>().Single(e => e.Id == 11 && e.TestSingle == param9));

                var param10 = true;
                Assert.Same(entity, context.Set<BuiltInDataTypes>().Single(e => e.Id == 11 && e.TestBoolean == param10));

                if (entityType.FindProperty("TestByte") != null)
                {
                    var param11 = (byte)255;
                    Assert.Same(entity, context.Set<BuiltInDataTypes>().Single(e => e.Id == 11 && e.TestByte == param11));
                }

                var param12 = Enum64.SomeValue;
                Assert.Same(entity, context.Set<BuiltInDataTypes>().Single(e => e.Id == 11 && e.Enum64 == param12));

                var param13 = Enum32.SomeValue;
                Assert.Same(entity, context.Set<BuiltInDataTypes>().Single(e => e.Id == 11 && e.Enum32 == param13));

                var param14 = Enum16.SomeValue;
                Assert.Same(entity, context.Set<BuiltInDataTypes>().Single(e => e.Id == 11 && e.Enum16 == param14));


                var param15 = Enum8.SomeValue;
                Assert.Same(entity, context.Set<BuiltInDataTypes>().Single(e => e.Id == 11 && e.Enum8 == param15));
            }
        }



        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types()
        {
            Fixture.TestSqlLoggerFactory.Clear();
            using (var context = CreateContext())
            {
                context.Set<MappedDataTypes>().Add(CreateMappedDataTypes(77));

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertMappedDataTypes(context.Set<MappedDataTypes>().Single(e => e.Int == 77), 77);
            }
        }

        private string DumpParameters()
            => Fixture.TestSqlLoggerFactory.Parameters.First().Replace(", ", FileLineEnding);

        private static void AssertMappedDataTypes(MappedDataTypes entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Equal(true, entity.Bit);
            Assert.Equal(83.3, entity.Float);
            Assert.Equal(84.4f, entity.Real);
            Assert.Equal(new DateTime(2019, 1, 2, 14, 11, 12), entity.Datetime);
            Assert.Equal("don't", entity.NvarcharMax);
            Assert.Equal("help", entity.National_char_varyingMax);
            Assert.Equal("anyone!", entity.National_character_varyingMax);
            Assert.Equal("Gumball Rules OK!", entity.Ntext);
            Assert.Equal(new byte[] { 89, 90, 91, 92 }, entity.VarbinaryMax);
            Assert.Equal(new byte[] { 97, 98, 99, 100 }, entity.Image);
            Assert.Equal(101m, entity.Decimal);
            Assert.Equal(102m, entity.Dec);
            Assert.Equal(103m, entity.Numeric);
        }

        private static MappedDataTypes CreateMappedDataTypes(int id)
            => new MappedDataTypes
            {
                Int = id,
                Bit = true,
                Money = 81.1m,
                Float = 83.3,
                Real = 84.4f,
                Datetime = new DateTime(2019, 1, 2, 14, 11, 12),
                NvarcharMax = "don't",
                National_char_varyingMax = "help",
                National_character_varyingMax = "anyone!",
                Ntext = "Gumball Rules OK!",
                VarbinaryMax = new byte[] { 89, 90, 91, 92 },
                Image = new byte[] { 97, 98, 99, 100 },
                Decimal = 101m,
                Dec = 102m,
                Numeric = 103m
            };

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_nullable_data_types()
        {
            Fixture.TestSqlLoggerFactory.Clear();
            using (var context = CreateContext())
            {
                context.Set<MappedNullableDataTypes>().Add(CreateMappedNullableDataTypes(77));

                Assert.Equal(1, context.SaveChanges());
            }


            using (var context = CreateContext())
            {
                AssertMappedNullableDataTypes(context.Set<MappedNullableDataTypes>().Single(e => e.Int == 77), 77);
            }
        }

        private static void AssertMappedNullableDataTypes(MappedNullableDataTypes entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Equal(79, entity.Smallint.Value);
            Assert.Equal(true, entity.Bit);
            Assert.Equal(81m, entity.Money);
            Assert.Equal(83, entity.Float);
            Assert.Equal(84f, entity.Real);
            Assert.Equal(new DateTime(2019, 1, 2, 14, 11, 12), entity.Datetime);
            Assert.Equal("don't", entity.NvarcharMax);
            Assert.Equal("help", entity.National_char_varyingMax);
            Assert.Equal("anyone!", entity.National_character_varyingMax);
            Assert.Equal(new byte[] { 89, 90, 91, 92 }, entity.VarbinaryMax);
            Assert.Equal(new byte[] { 97, 98, 99, 100 }, entity.Image);
            Assert.Equal(101m, entity.Decimal);
            Assert.Equal(102m, entity.Dec);
            Assert.Equal(103m, entity.Numeric);
        }

        private static MappedNullableDataTypes CreateMappedNullableDataTypes(int id)
            => new MappedNullableDataTypes
            {
                Int = id,
                Smallint = 79,
                Bit = true,
                Money = 81m,
                Float = 83,
                Real = 84f,
                Datetime = new DateTime(2019, 1, 2, 14, 11, 12),
                NvarcharMax = "don't",
                National_char_varyingMax = "help",
                National_character_varyingMax = "anyone!",
                VarbinaryMax = new byte[] { 89, 90, 91, 92 },
                Image = new byte[] { 97, 98, 99, 100 },
                Decimal = 101m,
                Dec = 102m,
                Numeric = 103m
            };

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_set_to_null()
        {
            Fixture.TestSqlLoggerFactory.Clear();
            using (var context = CreateContext())
            {
                context.Set<MappedNullableDataTypes>().Add(new MappedNullableDataTypes { Int = 78 });

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertNullMappedNullableDataTypes(context.Set<MappedNullableDataTypes>().Single(e => e.Int == 78), 78);
            }
        }

        private static void AssertNullMappedNullableDataTypes(MappedNullableDataTypes entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Null(entity.Smallint);
            Assert.Null(entity.Money);
            Assert.Null(entity.Float);
            Assert.Null(entity.Real);
            Assert.Null(entity.Datetime);
            Assert.Null(entity.NvarcharMax);
            Assert.Null(entity.National_char_varyingMax);
            Assert.Null(entity.National_character_varyingMax);
            Assert.Null(entity.VarbinaryMax);
            Assert.Null(entity.Image);
            Assert.Null(entity.Decimal);
            Assert.Null(entity.Dec);
            Assert.Null(entity.Numeric);
        }

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_sized_data_types()
        {
            Fixture.TestSqlLoggerFactory.Clear();
            using (var context = CreateContext())
            {
                context.Set<MappedSizedDataTypes>().Add(CreateMappedSizedDataTypes(77));

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertMappedSizedDataTypes(context.Set<MappedSizedDataTypes>().Single(e => e.Id == 77), 77);
            }
        }

        private static void AssertMappedSizedDataTypes(MappedSizedDataTypes entity, int id)
        {
            Assert.Equal(id, entity.Id);
            Assert.Equal("Won", entity.Nchar);
            Assert.Equal("Squ", entity.National_character);
            Assert.Equal("Int", entity.Nvarchar);
            Assert.Equal("The", entity.National_char_varying);
            Assert.Equal("Col", entity.National_character_varying);
            Assert.Equal(new byte[] { 10, 11, 12 }, entity.Binary);
            Assert.Equal(new byte[] { 11, 12, 13 }, entity.Varbinary);
        }

        private static MappedSizedDataTypes CreateMappedSizedDataTypes(int id)
            => new MappedSizedDataTypes
            {
                Id = id,
                Nchar = "Won",
                National_character = "Squ",
                Nvarchar = "Int",
                National_char_varying = "The",
                National_character_varying = "Col",
                Binary = new byte[] { 10, 11, 12 },
                Varbinary = new byte[] { 11, 12, 13 },
            };

        [Fact]
        public virtual void Can_insert_and_read_back_nulls_for_all_mapped_sized_data_types()
        {
            Fixture.TestSqlLoggerFactory.Clear();
            using (var context = CreateContext())
            {
                context.Set<MappedSizedDataTypes>().Add(new MappedSizedDataTypes { Id = 78 });

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertNullMappedSizedDataTypes(context.Set<MappedSizedDataTypes>().Single(e => e.Id == 78), 78);
            }
        }

        private static void AssertNullMappedSizedDataTypes(MappedSizedDataTypes entity, int id)
        {
            Assert.Equal(id, entity.Id);
            Assert.Null(entity.Nchar);
            Assert.Null(entity.National_character);
            Assert.Null(entity.Nvarchar);
            Assert.Null(entity.National_char_varying);
            Assert.Null(entity.National_character_varying);
            Assert.Null(entity.Binary);
            Assert.Null(entity.Varbinary);
        }

        [Fact(Skip = "Unsupported by JET: Data type unsupported")]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_with_scale()
        {
            Fixture.TestSqlLoggerFactory.Clear();
            using (var context = CreateContext())
            {
                context.Set<MappedScaledDataTypes>().Add(CreateMappedScaledDataTypes(77));

                Assert.Equal(1, context.SaveChanges());
            }

            var parameters = DumpParameters();
            Assert.Equal(
                @"@p0='77'
@p1='102.2'
@p2='101.1'
@p3='83.3'
@p4='103.3'",
                parameters);

            using (var context = CreateContext())
            {
                AssertMappedScaledDataTypes(context.Set<MappedScaledDataTypes>().Single(e => e.Id == 77), 77);
            }
        }

        private static void AssertMappedScaledDataTypes(MappedScaledDataTypes entity, int id)
        {
            Assert.Equal(id, entity.Id);
            Assert.Equal(83.3f, entity.Float);
            Assert.Equal(101m, entity.Decimal);
            Assert.Equal(102m, entity.Dec);
            Assert.Equal(103m, entity.Numeric);
        }

        private static MappedScaledDataTypes CreateMappedScaledDataTypes(int id)
            => new MappedScaledDataTypes
            {
                Id = id,
                Float = 83.3f,
                Decimal = 101.1m,
                Dec = 102.2m,
                Numeric = 103.3m
            };

        [Fact(Skip = "Unsupported by JET: Data type unsupported")]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_with_precision_and_scale()
        {
            Fixture.TestSqlLoggerFactory.Clear();
            using (var context = CreateContext())
            {
                context.Set<MappedPrecisionAndScaledDataTypes>().Add(CreateMappedPrecisionAndScaledDataTypes(77));

                Assert.Equal(1, context.SaveChanges());
            }

            var parameters = DumpParameters();
            Assert.Equal(
                @"@p0='77'
@p1='102.2'
@p2='101.1'
@p3='103.3'",
                parameters);

            using (var context = CreateContext())
            {
                AssertMappedPrecisionAndScaledDataTypes(context.Set<MappedPrecisionAndScaledDataTypes>().Single(e => e.Id == 77), 77);
            }
        }

        private static void AssertMappedPrecisionAndScaledDataTypes(MappedPrecisionAndScaledDataTypes entity, int id)
        {
            Assert.Equal(id, entity.Id);
            Assert.Equal(101.1m, entity.Decimal);
            Assert.Equal(102.2m, entity.Dec);
            Assert.Equal(103.3m, entity.Numeric);
        }

        private static MappedPrecisionAndScaledDataTypes CreateMappedPrecisionAndScaledDataTypes(int id)
            => new MappedPrecisionAndScaledDataTypes
            {
                Id = id,
                Decimal = 101.1m,
                Dec = 102.2m,
                Numeric = 103.3m
            };

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_with_identity()
        {
            Fixture.TestSqlLoggerFactory.Clear();
            using (var context = CreateContext())
            {
                context.Set<MappedDataTypesWithIdentity>().Add(CreateMappedDataTypesWithIdentity(77));

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertMappedDataTypesWithIdentity(context.Set<MappedDataTypesWithIdentity>().Single(e => e.Int == 77), 77);
            }
        }

        private static void AssertMappedDataTypesWithIdentity(MappedDataTypesWithIdentity entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Equal(79, entity.Smallint);
            Assert.Equal(true, entity.Bit);
            Assert.Equal(81, entity.Money);
            Assert.Equal(83.3, entity.Float);
            Assert.Equal(84.4f, entity.Real);
            Assert.Equal(new DateTime(2019, 1, 2, 14, 11, 12), entity.Datetime);
            Assert.Equal("don't", entity.NvarcharMax);
            Assert.Equal("help", entity.National_char_varyingMax);
            Assert.Equal("anyone!", entity.National_character_varyingMax);
            Assert.Equal("Gumball Rules OK!", entity.Ntext);
            Assert.Equal(new byte[] { 89, 90, 91, 92 }, entity.VarbinaryMax);
            Assert.Equal(new byte[] { 93, 94, 95, 96 }, entity.Binary_varyingMax);
            Assert.Equal(new byte[] { 97, 98, 99, 100 }, entity.Image);
            Assert.Equal(101m, entity.Decimal);
            Assert.Equal(102m, entity.Dec);
            Assert.Equal(103m, entity.Numeric);
        }

        private static MappedDataTypesWithIdentity CreateMappedDataTypesWithIdentity(int id)
            => new MappedDataTypesWithIdentity
            {
                Int = id,
                Smallint = 79,
                Bit = true,
                Money = 81m,
                Float = 83.3,
                Real = 84.4f,
                Datetime = new DateTime(2019, 1, 2, 14, 11, 12),
                NvarcharMax = "don't",
                National_char_varyingMax = "help",
                National_character_varyingMax = "anyone!",
                Ntext = "Gumball Rules OK!",
                VarbinaryMax = new byte[] { 89, 90, 91, 92 },
                Binary_varyingMax = new byte[] { 93, 94, 95, 96 },
                Image = new byte[] { 97, 98, 99, 100 },
                Decimal = 101m,
                Dec = 102m,
                Numeric = 103m
            };

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_nullable_data_types_with_identity()
        {
            Fixture.TestSqlLoggerFactory.Clear();
            using (var context = CreateContext())
            {
                context.Set<MappedNullableDataTypesWithIdentity>().Add(CreateMappedNullableDataTypesWithIdentity(77));

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertMappedNullableDataTypesWithIdentity(context.Set<MappedNullableDataTypesWithIdentity>().Single(e => e.Int == 77), 77);
            }
        }

        private static void AssertMappedNullableDataTypesWithIdentity(MappedNullableDataTypesWithIdentity entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Equal(79, entity.Smallint.Value);
            Assert.Equal(true, entity.Bit);
            Assert.Equal(81, entity.Money);
            Assert.Equal(83.3, entity.Float);
            Assert.Equal(84.4f, entity.Real);
            Assert.Equal(new DateTime(2019, 1, 2, 14, 11, 12), entity.Datetime);
            Assert.Equal("don't", entity.NvarcharMax);
            Assert.Equal("help", entity.National_char_varyingMax);
            Assert.Equal("anyone!", entity.National_character_varyingMax);
            Assert.Equal("Gumball Rules OK!", entity.Ntext);
            Assert.Equal(new byte[] { 89, 90, 91, 92 }, entity.VarbinaryMax);
            Assert.Equal(new byte[] { 97, 98, 99, 100 }, entity.Image);
            Assert.Equal(101m, entity.Decimal);
            Assert.Equal(102m, entity.Dec);
            Assert.Equal(103m, entity.Numeric);
        }

        private static MappedNullableDataTypesWithIdentity CreateMappedNullableDataTypesWithIdentity(int id)
            => new MappedNullableDataTypesWithIdentity
            {
                Int = id,
                Smallint = 79,
                Bit = true,
                Money = 81,
                Float = 83.3,
                Real = 84.4f,
                Datetime = new DateTime(2019, 1, 2, 14, 11, 12),
                NvarcharMax = "don't",
                National_char_varyingMax = "help",
                National_character_varyingMax = "anyone!",
                Ntext = "Gumball Rules OK!",
                VarbinaryMax = new byte[] { 89, 90, 91, 92 },
                Image = new byte[] { 97, 98, 99, 100 },
                Decimal = 101m,
                Dec = 102m,
                Numeric = 103m
            };

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_set_to_null_with_identity()
        {
            Fixture.TestSqlLoggerFactory.Clear();
            using (var context = CreateContext())
            {
                context.Set<MappedNullableDataTypesWithIdentity>().Add(new MappedNullableDataTypesWithIdentity { Int = 78 });

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertNullMappedNullableDataTypesWithIdentity(context.Set<MappedNullableDataTypesWithIdentity>().Single(e => e.Int == 78), 78);
            }
        }

        private static void AssertNullMappedNullableDataTypesWithIdentity(
            MappedNullableDataTypesWithIdentity entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Null(entity.Smallint);
            Assert.Null(entity.Money);
            Assert.Null(entity.Float);
            Assert.Null(entity.Real);
            Assert.Null(entity.Datetime);
            Assert.Null(entity.NvarcharMax);
            Assert.Null(entity.National_char_varyingMax);
            Assert.Null(entity.National_character_varyingMax);
            Assert.Null(entity.Ntext);
            Assert.Null(entity.VarbinaryMax);
            Assert.Null(entity.Image);
            Assert.Null(entity.Decimal);
            Assert.Null(entity.Dec);
            Assert.Null(entity.Numeric);
        }

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_sized_data_types_with_identity()
        {
            Fixture.TestSqlLoggerFactory.Clear();
            using (var context = CreateContext())
            {
                context.Set<MappedSizedDataTypesWithIdentity>().Add(CreateMappedSizedDataTypesWithIdentity(77));

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertMappedSizedDataTypesWithIdentity(context.Set<MappedSizedDataTypesWithIdentity>().Single(e => e.Int == 77), 77);
            }
        }

        private static void AssertMappedSizedDataTypesWithIdentity(MappedSizedDataTypesWithIdentity entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Equal("Won", entity.Nchar);
            Assert.Equal("Squ", entity.National_character);
            Assert.Equal("Int", entity.Nvarchar);
            Assert.Equal("The", entity.National_char_varying);
            Assert.Equal("Col", entity.National_character_varying);
            Assert.Equal(new byte[] { 10, 11, 12 }, entity.Binary);
            Assert.Equal(new byte[] { 11, 12, 13 }, entity.Varbinary);
        }

        private static MappedSizedDataTypesWithIdentity CreateMappedSizedDataTypesWithIdentity(int id)
            => new MappedSizedDataTypesWithIdentity
            {
                Int = id,
                Nchar = "Won",
                National_character = "Squ",
                Nvarchar = "Int",
                National_char_varying = "The",
                National_character_varying = "Col",
                Binary = new byte[] { 10, 11, 12 },
                Varbinary = new byte[] { 11, 12, 13 },
            };

        [Fact]
        public virtual void Can_insert_and_read_back_nulls_for_all_mapped_sized_data_types_with_identity()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedSizedDataTypesWithIdentity>().Add(new MappedSizedDataTypesWithIdentity { Int = 78 });

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertNullMappedSizedDataTypesWithIdentity(context.Set<MappedSizedDataTypesWithIdentity>().Single(e => e.Int == 78), 78);
            }
        }

        private static void AssertNullMappedSizedDataTypesWithIdentity(MappedSizedDataTypesWithIdentity entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Null(entity.Nchar);
            Assert.Null(entity.National_character);
            Assert.Null(entity.Nvarchar);
            Assert.Null(entity.National_char_varying);
            Assert.Null(entity.National_character_varying);
            Assert.Null(entity.Binary);
            Assert.Null(entity.Varbinary);
        }

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_with_scale_with_identity()
        {
            Fixture.TestSqlLoggerFactory.Clear();
            using (var context = CreateContext())
            {
                context.Set<MappedScaledDataTypesWithIdentity>().Add(CreateMappedScaledDataTypesWithIdentity(77));

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertMappedScaledDataTypesWithIdentity(context.Set<MappedScaledDataTypesWithIdentity>().Single(e => e.Int == 77), 77);
            }
        }

        private static void AssertMappedScaledDataTypesWithIdentity(MappedScaledDataTypesWithIdentity entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Equal(83.3f, entity.Float);
            Assert.Equal(101m, entity.Decimal);
            Assert.Equal(102m, entity.Dec);
            Assert.Equal(103m, entity.Numeric);
        }

        private static MappedScaledDataTypesWithIdentity CreateMappedScaledDataTypesWithIdentity(int id)
            => new MappedScaledDataTypesWithIdentity
            {
                Int = id,
                Float = 83.3f,
                Decimal = 101m,
                Dec = 102m,
                Numeric = 103m
            };

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_with_precision_and_scale_with_identity()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedPrecisionAndScaledDataTypesWithIdentity>().Add(
                    CreateMappedPrecisionAndScaledDataTypesWithIdentity(77));

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertMappedPrecisionAndScaledDataTypesWithIdentity(
                    context.Set<MappedPrecisionAndScaledDataTypesWithIdentity>().Single(e => e.Int == 77), 77);
            }
        }

        private static void AssertMappedPrecisionAndScaledDataTypesWithIdentity(MappedPrecisionAndScaledDataTypesWithIdentity entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Equal(101m, entity.Decimal);
            Assert.Equal(102m, entity.Dec);
            Assert.Equal(103m, entity.Numeric);
        }

        private static MappedPrecisionAndScaledDataTypesWithIdentity CreateMappedPrecisionAndScaledDataTypesWithIdentity(int id)
            => new MappedPrecisionAndScaledDataTypesWithIdentity
            {
                Int = id,
                Decimal = 101m,
                Dec = 102m,
                Numeric = 103m
            };

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_in_batch()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedDataTypes>().Add(CreateMappedDataTypes(177));
                context.Set<MappedDataTypes>().Add(CreateMappedDataTypes(178));
                context.Set<MappedDataTypes>().Add(CreateMappedDataTypes(179));

                Assert.Equal(3, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertMappedDataTypes(context.Set<MappedDataTypes>().Single(e => e.Int == 177), 177);
                AssertMappedDataTypes(context.Set<MappedDataTypes>().Single(e => e.Int == 178), 178);
                AssertMappedDataTypes(context.Set<MappedDataTypes>().Single(e => e.Int == 179), 179);
            }
        }

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_nullable_data_types_in_batch()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedNullableDataTypes>().Add(CreateMappedNullableDataTypes(177));
                context.Set<MappedNullableDataTypes>().Add(CreateMappedNullableDataTypes(178));
                context.Set<MappedNullableDataTypes>().Add(CreateMappedNullableDataTypes(179));

                Assert.Equal(3, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertMappedNullableDataTypes(context.Set<MappedNullableDataTypes>().Single(e => e.Int == 177), 177);
                AssertMappedNullableDataTypes(context.Set<MappedNullableDataTypes>().Single(e => e.Int == 178), 178);
                AssertMappedNullableDataTypes(context.Set<MappedNullableDataTypes>().Single(e => e.Int == 179), 179);
            }
        }

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_set_to_null_in_batch()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedNullableDataTypes>().Add(new MappedNullableDataTypes { Int = 278 });
                context.Set<MappedNullableDataTypes>().Add(new MappedNullableDataTypes { Int = 279 });
                context.Set<MappedNullableDataTypes>().Add(new MappedNullableDataTypes { Int = 280 });

                Assert.Equal(3, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertNullMappedNullableDataTypes(context.Set<MappedNullableDataTypes>().Single(e => e.Int == 278), 278);
                AssertNullMappedNullableDataTypes(context.Set<MappedNullableDataTypes>().Single(e => e.Int == 279), 279);
                AssertNullMappedNullableDataTypes(context.Set<MappedNullableDataTypes>().Single(e => e.Int == 280), 280);
            }
        }

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_sized_data_types_in_batch()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedSizedDataTypes>().Add(CreateMappedSizedDataTypes(177));
                context.Set<MappedSizedDataTypes>().Add(CreateMappedSizedDataTypes(178));
                context.Set<MappedSizedDataTypes>().Add(CreateMappedSizedDataTypes(179));

                Assert.Equal(3, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertMappedSizedDataTypes(context.Set<MappedSizedDataTypes>().Single(e => e.Id == 177), 177);
                AssertMappedSizedDataTypes(context.Set<MappedSizedDataTypes>().Single(e => e.Id == 178), 178);
                AssertMappedSizedDataTypes(context.Set<MappedSizedDataTypes>().Single(e => e.Id == 179), 179);
            }
        }

        [Fact]
        public virtual void Can_insert_and_read_back_nulls_for_all_mapped_sized_data_types_in_batch()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedSizedDataTypes>().Add(new MappedSizedDataTypes { Id = 278 });
                context.Set<MappedSizedDataTypes>().Add(new MappedSizedDataTypes { Id = 279 });
                context.Set<MappedSizedDataTypes>().Add(new MappedSizedDataTypes { Id = 280 });

                Assert.Equal(3, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertNullMappedSizedDataTypes(context.Set<MappedSizedDataTypes>().Single(e => e.Id == 278), 278);
                AssertNullMappedSizedDataTypes(context.Set<MappedSizedDataTypes>().Single(e => e.Id == 279), 279);
                AssertNullMappedSizedDataTypes(context.Set<MappedSizedDataTypes>().Single(e => e.Id == 280), 280);
            }
        }

        [Fact(Skip = "Unsupported by JET: Data type unsupported")]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_with_scale_in_batch()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedScaledDataTypes>().Add(CreateMappedScaledDataTypes(177));
                context.Set<MappedScaledDataTypes>().Add(CreateMappedScaledDataTypes(178));
                context.Set<MappedScaledDataTypes>().Add(CreateMappedScaledDataTypes(179));

                Assert.Equal(3, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertMappedScaledDataTypes(context.Set<MappedScaledDataTypes>().Single(e => e.Id == 177), 177);
                AssertMappedScaledDataTypes(context.Set<MappedScaledDataTypes>().Single(e => e.Id == 178), 178);
                AssertMappedScaledDataTypes(context.Set<MappedScaledDataTypes>().Single(e => e.Id == 179), 179);
            }
        }

        [Fact(Skip = "Unsupported by JET: Data type unsupported")]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_with_precision_and_scale_in_batch()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedPrecisionAndScaledDataTypes>().Add(CreateMappedPrecisionAndScaledDataTypes(177));
                context.Set<MappedPrecisionAndScaledDataTypes>().Add(CreateMappedPrecisionAndScaledDataTypes(178));
                context.Set<MappedPrecisionAndScaledDataTypes>().Add(CreateMappedPrecisionAndScaledDataTypes(179));

                Assert.Equal(3, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertMappedPrecisionAndScaledDataTypes(context.Set<MappedPrecisionAndScaledDataTypes>().Single(e => e.Id == 177), 177);
                AssertMappedPrecisionAndScaledDataTypes(context.Set<MappedPrecisionAndScaledDataTypes>().Single(e => e.Id == 178), 178);
                AssertMappedPrecisionAndScaledDataTypes(context.Set<MappedPrecisionAndScaledDataTypes>().Single(e => e.Id == 179), 179);
            }
        }

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_with_identity_in_batch()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedDataTypesWithIdentity>().Add(CreateMappedDataTypesWithIdentity(177));
                context.Set<MappedDataTypesWithIdentity>().Add(CreateMappedDataTypesWithIdentity(178));
                context.Set<MappedDataTypesWithIdentity>().Add(CreateMappedDataTypesWithIdentity(179));

                Assert.Equal(3, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertMappedDataTypesWithIdentity(context.Set<MappedDataTypesWithIdentity>().Single(e => e.Int == 177), 177);
                AssertMappedDataTypesWithIdentity(context.Set<MappedDataTypesWithIdentity>().Single(e => e.Int == 178), 178);
                AssertMappedDataTypesWithIdentity(context.Set<MappedDataTypesWithIdentity>().Single(e => e.Int == 179), 179);
            }
        }

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_nullable_data_types_with_identity_in_batch()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedNullableDataTypesWithIdentity>().Add(CreateMappedNullableDataTypesWithIdentity(177));
                context.Set<MappedNullableDataTypesWithIdentity>().Add(CreateMappedNullableDataTypesWithIdentity(178));
                context.Set<MappedNullableDataTypesWithIdentity>().Add(CreateMappedNullableDataTypesWithIdentity(179));

                Assert.Equal(3, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertMappedNullableDataTypesWithIdentity(context.Set<MappedNullableDataTypesWithIdentity>().Single(e => e.Int == 177), 177);
                AssertMappedNullableDataTypesWithIdentity(context.Set<MappedNullableDataTypesWithIdentity>().Single(e => e.Int == 178), 178);
                AssertMappedNullableDataTypesWithIdentity(context.Set<MappedNullableDataTypesWithIdentity>().Single(e => e.Int == 179), 179);
            }
        }

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_set_to_null_with_identity_in_batch()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedNullableDataTypesWithIdentity>().Add(new MappedNullableDataTypesWithIdentity { Int = 278 });
                context.Set<MappedNullableDataTypesWithIdentity>().Add(new MappedNullableDataTypesWithIdentity { Int = 279 });
                context.Set<MappedNullableDataTypesWithIdentity>().Add(new MappedNullableDataTypesWithIdentity { Int = 280 });

                Assert.Equal(3, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertNullMappedNullableDataTypesWithIdentity(context.Set<MappedNullableDataTypesWithIdentity>().Single(e => e.Int == 278), 278);
                AssertNullMappedNullableDataTypesWithIdentity(context.Set<MappedNullableDataTypesWithIdentity>().Single(e => e.Int == 279), 279);
                AssertNullMappedNullableDataTypesWithIdentity(context.Set<MappedNullableDataTypesWithIdentity>().Single(e => e.Int == 280), 280);
            }
        }

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_sized_data_types_with_identity_in_batch()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedSizedDataTypesWithIdentity>().Add(CreateMappedSizedDataTypesWithIdentity(177));
                context.Set<MappedSizedDataTypesWithIdentity>().Add(CreateMappedSizedDataTypesWithIdentity(178));
                context.Set<MappedSizedDataTypesWithIdentity>().Add(CreateMappedSizedDataTypesWithIdentity(179));

                Assert.Equal(3, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertMappedSizedDataTypesWithIdentity(context.Set<MappedSizedDataTypesWithIdentity>().Single(e => e.Int == 177), 177);
                AssertMappedSizedDataTypesWithIdentity(context.Set<MappedSizedDataTypesWithIdentity>().Single(e => e.Int == 178), 178);
                AssertMappedSizedDataTypesWithIdentity(context.Set<MappedSizedDataTypesWithIdentity>().Single(e => e.Int == 179), 179);
            }
        }

        [Fact(Skip = "Unsupported by JET: Data type unsupported")]
        public virtual void Can_insert_and_read_back_nulls_for_all_mapped_sized_data_types_with_identity_in_batch()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedSizedDataTypesWithIdentity>().Add(new MappedSizedDataTypesWithIdentity { Int = 278 });
                context.Set<MappedSizedDataTypesWithIdentity>().Add(new MappedSizedDataTypesWithIdentity { Int = 279 });
                context.Set<MappedSizedDataTypesWithIdentity>().Add(new MappedSizedDataTypesWithIdentity { Int = 280 });

                Assert.Equal(3, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertNullMappedSizedDataTypesWithIdentity(context.Set<MappedSizedDataTypesWithIdentity>().Single(e => e.Int == 278), 278);
                AssertNullMappedSizedDataTypesWithIdentity(context.Set<MappedSizedDataTypesWithIdentity>().Single(e => e.Int == 279), 279);
                AssertNullMappedSizedDataTypesWithIdentity(context.Set<MappedSizedDataTypesWithIdentity>().Single(e => e.Int == 280), 280);
            }
        }

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_with_scale_with_identity_in_batch()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedScaledDataTypesWithIdentity>().Add(CreateMappedScaledDataTypesWithIdentity(177));
                context.Set<MappedScaledDataTypesWithIdentity>().Add(CreateMappedScaledDataTypesWithIdentity(178));
                context.Set<MappedScaledDataTypesWithIdentity>().Add(CreateMappedScaledDataTypesWithIdentity(179));

                Assert.Equal(3, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertMappedScaledDataTypesWithIdentity(context.Set<MappedScaledDataTypesWithIdentity>().Single(e => e.Int == 177), 177);
                AssertMappedScaledDataTypesWithIdentity(context.Set<MappedScaledDataTypesWithIdentity>().Single(e => e.Int == 178), 178);
                AssertMappedScaledDataTypesWithIdentity(context.Set<MappedScaledDataTypesWithIdentity>().Single(e => e.Int == 179), 179);
            }
        }

        [Fact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_with_precision_and_scale_with_identity_in_batch()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedPrecisionAndScaledDataTypesWithIdentity>().Add(CreateMappedPrecisionAndScaledDataTypesWithIdentity(177));
                context.Set<MappedPrecisionAndScaledDataTypesWithIdentity>().Add(CreateMappedPrecisionAndScaledDataTypesWithIdentity(178));
                context.Set<MappedPrecisionAndScaledDataTypesWithIdentity>().Add(CreateMappedPrecisionAndScaledDataTypesWithIdentity(179));

                Assert.Equal(3, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                AssertMappedPrecisionAndScaledDataTypesWithIdentity(
                    context.Set<MappedPrecisionAndScaledDataTypesWithIdentity>().Single(e => e.Int == 177), 177);
                AssertMappedPrecisionAndScaledDataTypesWithIdentity(
                    context.Set<MappedPrecisionAndScaledDataTypesWithIdentity>().Single(e => e.Int == 178), 178);
                AssertMappedPrecisionAndScaledDataTypesWithIdentity(
                    context.Set<MappedPrecisionAndScaledDataTypesWithIdentity>().Single(e => e.Int == 179), 179);
            }
        }

        [Fact(Skip = "Unsupported by JET")]
        public virtual void Columns_have_expected_data_types()
        {
            const string query
                = @"SELECT
                        TABLE_NAME,
                        COLUMN_NAME,
                        DATA_TYPE,
                        IS_NULLABLE,
                        CHARACTER_MAXIMUM_LENGTH,
                        NUMERIC_PRECISION,
                        NUMERIC_SCALE,
                        DATETIME_PRECISION
                    FROM INFORMATION_SCHEMA.COLUMNS";

            var columns = new List<ColumnInfo>();

            using (var context = CreateContext())
            {
                var connection = context.Database.GetDbConnection();

                var command = connection.CreateCommand();
                command.CommandText = query;

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var columnInfo = new ColumnInfo
                        {
                            TableName = reader.GetString(0),
                            ColumnName = reader.GetString(1),
                            DataType = reader.GetString(2),
                            IsNullable = reader.IsDBNull(3) ? null : (bool?)(reader.GetString(3) == "YES"),
                            MaxLength = reader.IsDBNull(4) ? null : (int?)reader.GetInt32(4),
                            NumericPrecision = reader.IsDBNull(5) ? null : (int?)reader.GetInt16(5),
                            NumericScale = reader.IsDBNull(6) ? null : (int?)reader.GetInt16(6),
                            DateTimePrecision = null
                        };

                        columns.Add(columnInfo);
                    }
                }
            }

            var builder = new StringBuilder();

            foreach (var column in columns.OrderBy(e => e.TableName).ThenBy(e => e.ColumnName))
            {
                builder.Append(column.TableName);
                builder.Append(".");
                builder.Append(column.ColumnName);
                builder.Append(" ---> [");

                if (column.IsNullable == true)
                {
                    builder.Append("nullable ");
                }

                builder.Append(column.DataType);
                builder.Append("]");

                if (column.MaxLength.HasValue)
                {
                    builder.Append(" [MaxLength = ");
                    builder.Append(column.MaxLength);
                    builder.Append("]");
                }

                if (column.NumericPrecision.HasValue)
                {
                    builder.Append(" [Precision = ");
                    builder.Append(column.NumericPrecision);
                }

                if (column.DateTimePrecision.HasValue)
                {
                    builder.Append(" [Precision = ");
                    builder.Append(column.DateTimePrecision);
                }

                if (column.NumericScale.HasValue)
                {
                    builder.Append(" Scale = ");
                    builder.Append(column.NumericScale);
                }

                if (column.NumericPrecision.HasValue
                    || column.DateTimePrecision.HasValue
                    || column.NumericScale.HasValue)
                {
                    builder.Append("]");
                }

                builder.AppendLine();
            }

            var actual = builder.ToString().Replace(Environment.NewLine, FileLineEnding);

            const string expected = @"BinaryForeignKeyDataType.BinaryKeyDataTypeId ---> [nullable varbinary] [MaxLength = 512]
BinaryForeignKeyDataType.Id ---> [int] [Precision = 10]
BinaryKeyDataType.Id ---> [varbinary] [MaxLength = 512]
BuiltInDataTypes.Enum16 ---> [smallint] [Precision = 5]
BuiltInDataTypes.Enum32 ---> [int] [Precision = 10]
BuiltInDataTypes.Enum64 ---> [bigint] [Precision = 19]
BuiltInDataTypes.Enum8 ---> [tinyint] [Precision = 3]
BuiltInDataTypes.Id ---> [int] [Precision = 10]
BuiltInDataTypes.PartitionId ---> [int] [Precision = 10]
BuiltInDataTypes.TestBoolean ---> [bit] [Precision = 1 Scale = 0]
BuiltInDataTypes.TestByte ---> [tinyint] [Precision = 3]
BuiltInDataTypes.TestDateTime ---> [datetime] [Precision = 23 Scale = 3]
BuiltInDataTypes.TestDecimal ---> [numeric] [Precision = 18 Scale = 2]
BuiltInDataTypes.TestDouble ---> [float] [Precision = 53]
BuiltInDataTypes.TestInt16 ---> [smallint] [Precision = 5]
BuiltInDataTypes.TestInt32 ---> [int] [Precision = 10]
BuiltInDataTypes.TestInt64 ---> [bigint] [Precision = 19]
BuiltInDataTypes.TestSingle ---> [real] [Precision = 24]
BuiltInNullableDataTypes.Enum16 ---> [nullable smallint] [Precision = 5]
BuiltInNullableDataTypes.Enum32 ---> [nullable int] [Precision = 10]
BuiltInNullableDataTypes.Enum64 ---> [nullable bigint] [Precision = 19]
BuiltInNullableDataTypes.Enum8 ---> [nullable tinyint] [Precision = 3]
BuiltInNullableDataTypes.Id ---> [int] [Precision = 10]
BuiltInNullableDataTypes.PartitionId ---> [int] [Precision = 10]
BuiltInNullableDataTypes.TestByteArray ---> [nullable varbinary] [MaxLength = 8000]
BuiltInNullableDataTypes.TestNullableBoolean ---> [nullable bit] [Precision = 1 Scale = 0]
BuiltInNullableDataTypes.TestNullableByte ---> [nullable tinyint] [Precision = 3]
BuiltInNullableDataTypes.TestNullableDateTime ---> [nullable datetime] [Precision = 23 Scale = 3]
BuiltInNullableDataTypes.TestNullableDecimal ---> [nullable numeric] [Precision = 18 Scale = 2]
BuiltInNullableDataTypes.TestNullableDouble ---> [nullable float] [Precision = 53]
BuiltInNullableDataTypes.TestNullableInt16 ---> [nullable smallint] [Precision = 5]
BuiltInNullableDataTypes.TestNullableInt32 ---> [nullable int] [Precision = 10]
BuiltInNullableDataTypes.TestNullableInt64 ---> [nullable bigint] [Precision = 19]
BuiltInNullableDataTypes.TestNullableSingle ---> [nullable real] [Precision = 24]
BuiltInNullableDataTypes.TestString ---> [nullable nvarchar] [MaxLength = 4000]
MappedDataTypes.Bigint ---> [bigint] [Precision = 19]
MappedDataTypes.Bit ---> [bit] [Precision = 1 Scale = 0]
MappedDataTypes.Datetime ---> [datetime] [Precision = 23 Scale = 3]
MappedDataTypes.Dec ---> [numeric] [Precision = 18 Scale = 0]
MappedDataTypes.Decimal ---> [numeric] [Precision = 18 Scale = 0]
MappedDataTypes.Float ---> [float] [Precision = 53]
MappedDataTypes.Image ---> [image] [MaxLength = 1073741823]
MappedDataTypes.Int ---> [int] [Precision = 10]
MappedDataTypes.Money ---> [money] [Precision = 19 Scale = 4]
MappedDataTypes.National_char_varyingMax ---> [ntext] [MaxLength = 536870911]
MappedDataTypes.National_character_varyingMax ---> [ntext] [MaxLength = 536870911]
MappedDataTypes.Ntext ---> [ntext] [MaxLength = 536870911]
MappedDataTypes.Numeric ---> [numeric] [Precision = 18 Scale = 0]
MappedDataTypes.NvarcharMax ---> [ntext] [MaxLength = 536870911]
MappedDataTypes.Real ---> [real] [Precision = 24]
MappedDataTypes.Smallint ---> [smallint] [Precision = 5]
MappedDataTypes.Tinyint ---> [tinyint] [Precision = 3]
MappedDataTypes.VarbinaryMax ---> [image] [MaxLength = 1073741823]
MappedDataTypesWithIdentity.Bigint ---> [bigint] [Precision = 19]
MappedDataTypesWithIdentity.Binary_varyingMax ---> [nullable image] [MaxLength = 1073741823]
MappedDataTypesWithIdentity.Bit ---> [bit] [Precision = 1 Scale = 0]
MappedDataTypesWithIdentity.Datetime ---> [datetime] [Precision = 23 Scale = 3]
MappedDataTypesWithIdentity.Dec ---> [numeric] [Precision = 18 Scale = 0]
MappedDataTypesWithIdentity.Decimal ---> [numeric] [Precision = 18 Scale = 0]
MappedDataTypesWithIdentity.Float ---> [float] [Precision = 53]
MappedDataTypesWithIdentity.Id ---> [int] [Precision = 10]
MappedDataTypesWithIdentity.Image ---> [nullable image] [MaxLength = 1073741823]
MappedDataTypesWithIdentity.Int ---> [int] [Precision = 10]
MappedDataTypesWithIdentity.Money ---> [money] [Precision = 19 Scale = 4]
MappedDataTypesWithIdentity.National_char_varyingMax ---> [nullable ntext] [MaxLength = 536870911]
MappedDataTypesWithIdentity.National_character_varyingMax ---> [nullable ntext] [MaxLength = 536870911]
MappedDataTypesWithIdentity.Ntext ---> [nullable ntext] [MaxLength = 536870911]
MappedDataTypesWithIdentity.Numeric ---> [numeric] [Precision = 18 Scale = 0]
MappedDataTypesWithIdentity.NvarcharMax ---> [nullable ntext] [MaxLength = 536870911]
MappedDataTypesWithIdentity.Real ---> [real] [Precision = 24]
MappedDataTypesWithIdentity.Smallint ---> [smallint] [Precision = 5]
MappedDataTypesWithIdentity.Tinyint ---> [tinyint] [Precision = 3]
MappedDataTypesWithIdentity.VarbinaryMax ---> [nullable image] [MaxLength = 1073741823]
MappedNullableDataTypes.Bigint ---> [nullable bigint] [Precision = 19]
MappedNullableDataTypes.Bit ---> [nullable bit] [Precision = 1 Scale = 0]
MappedNullableDataTypes.Datetime ---> [nullable datetime] [Precision = 23 Scale = 3]
MappedNullableDataTypes.Dec ---> [nullable numeric] [Precision = 18 Scale = 0]
MappedNullableDataTypes.Decimal ---> [nullable numeric] [Precision = 18 Scale = 0]
MappedNullableDataTypes.Float ---> [nullable float] [Precision = 53]
MappedNullableDataTypes.Image ---> [nullable image] [MaxLength = 1073741823]
MappedNullableDataTypes.Int ---> [int] [Precision = 10]
MappedNullableDataTypes.Money ---> [nullable money] [Precision = 19 Scale = 4]
MappedNullableDataTypes.National_char_varyingMax ---> [nullable ntext] [MaxLength = 536870911]
MappedNullableDataTypes.National_character_varyingMax ---> [nullable ntext] [MaxLength = 536870911]
MappedNullableDataTypes.Ntext ---> [nullable ntext] [MaxLength = 536870911]
MappedNullableDataTypes.Numeric ---> [nullable numeric] [Precision = 18 Scale = 0]
MappedNullableDataTypes.NvarcharMax ---> [nullable ntext] [MaxLength = 536870911]
MappedNullableDataTypes.Real ---> [nullable real] [Precision = 24]
MappedNullableDataTypes.Smallint ---> [nullable smallint] [Precision = 5]
MappedNullableDataTypes.Tinyint ---> [nullable tinyint] [Precision = 3]
MappedNullableDataTypes.VarbinaryMax ---> [nullable image] [MaxLength = 1073741823]
MappedNullableDataTypesWithIdentity.Bigint ---> [nullable bigint] [Precision = 19]
MappedNullableDataTypesWithIdentity.Bit ---> [nullable bit] [Precision = 1 Scale = 0]
MappedNullableDataTypesWithIdentity.Datetime ---> [nullable datetime] [Precision = 23 Scale = 3]
MappedNullableDataTypesWithIdentity.Dec ---> [nullable numeric] [Precision = 18 Scale = 0]
MappedNullableDataTypesWithIdentity.Decimal ---> [nullable numeric] [Precision = 18 Scale = 0]
MappedNullableDataTypesWithIdentity.Float ---> [nullable float] [Precision = 53]
MappedNullableDataTypesWithIdentity.Id ---> [int] [Precision = 10]
MappedNullableDataTypesWithIdentity.Image ---> [nullable image] [MaxLength = 1073741823]
MappedNullableDataTypesWithIdentity.Int ---> [nullable int] [Precision = 10]
MappedNullableDataTypesWithIdentity.Money ---> [nullable money] [Precision = 19 Scale = 4]
MappedNullableDataTypesWithIdentity.National_char_varyingMax ---> [nullable ntext] [MaxLength = 536870911]
MappedNullableDataTypesWithIdentity.National_character_varyingMax ---> [nullable ntext] [MaxLength = 536870911]
MappedNullableDataTypesWithIdentity.Ntext ---> [nullable ntext] [MaxLength = 536870911]
MappedNullableDataTypesWithIdentity.Numeric ---> [nullable numeric] [Precision = 18 Scale = 0]
MappedNullableDataTypesWithIdentity.NvarcharMax ---> [nullable ntext] [MaxLength = 536870911]
MappedNullableDataTypesWithIdentity.Real ---> [nullable real] [Precision = 24]
MappedNullableDataTypesWithIdentity.Smallint ---> [nullable smallint] [Precision = 5]
MappedNullableDataTypesWithIdentity.Tinyint ---> [nullable tinyint] [Precision = 3]
MappedNullableDataTypesWithIdentity.VarbinaryMax ---> [nullable image] [MaxLength = 1073741823]
MappedPrecisionAndScaledDataTypes.Dec ---> [numeric] [Precision = 5 Scale = 2]
MappedPrecisionAndScaledDataTypes.Decimal ---> [numeric] [Precision = 5 Scale = 2]
MappedPrecisionAndScaledDataTypes.Id ---> [int] [Precision = 10]
MappedPrecisionAndScaledDataTypes.Numeric ---> [numeric] [Precision = 5 Scale = 2]
MappedPrecisionAndScaledDataTypesWithIdentity.Dec ---> [numeric] [Precision = 5 Scale = 2]
MappedPrecisionAndScaledDataTypesWithIdentity.Decimal ---> [numeric] [Precision = 5 Scale = 2]
MappedPrecisionAndScaledDataTypesWithIdentity.Id ---> [int] [Precision = 10]
MappedPrecisionAndScaledDataTypesWithIdentity.Int ---> [int] [Precision = 10]
MappedPrecisionAndScaledDataTypesWithIdentity.Numeric ---> [numeric] [Precision = 5 Scale = 2]
MappedScaledDataTypes.Dec ---> [numeric] [Precision = 3 Scale = 0]
MappedScaledDataTypes.Decimal ---> [numeric] [Precision = 3 Scale = 0]
MappedScaledDataTypes.Float ---> [real] [Precision = 24]
MappedScaledDataTypes.Id ---> [int] [Precision = 10]
MappedScaledDataTypes.Numeric ---> [numeric] [Precision = 3 Scale = 0]
MappedScaledDataTypesWithIdentity.Dec ---> [numeric] [Precision = 3 Scale = 0]
MappedScaledDataTypesWithIdentity.Decimal ---> [numeric] [Precision = 3 Scale = 0]
MappedScaledDataTypesWithIdentity.Float ---> [real] [Precision = 24]
MappedScaledDataTypesWithIdentity.Id ---> [int] [Precision = 10]
MappedScaledDataTypesWithIdentity.Int ---> [int] [Precision = 10]
MappedScaledDataTypesWithIdentity.Numeric ---> [numeric] [Precision = 3 Scale = 0]
MappedSizedDataTypes.Binary ---> [nullable binary] [MaxLength = 3]
MappedSizedDataTypes.Id ---> [int] [Precision = 10]
MappedSizedDataTypes.National_char_varying ---> [nullable nvarchar] [MaxLength = 3]
MappedSizedDataTypes.National_character ---> [nullable nchar] [MaxLength = 3]
MappedSizedDataTypes.National_character_varying ---> [nullable nvarchar] [MaxLength = 3]
MappedSizedDataTypes.Nchar ---> [nullable nchar] [MaxLength = 3]
MappedSizedDataTypes.Nvarchar ---> [nullable nvarchar] [MaxLength = 3]
MappedSizedDataTypes.Varbinary ---> [nullable varbinary] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.Binary ---> [nullable binary] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.Id ---> [int] [Precision = 10]
MappedSizedDataTypesWithIdentity.Int ---> [int] [Precision = 10]
MappedSizedDataTypesWithIdentity.National_char_varying ---> [nullable nvarchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.National_character ---> [nullable nchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.National_character_varying ---> [nullable nvarchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.Nchar ---> [nullable nchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.Nvarchar ---> [nullable nvarchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.Varbinary ---> [nullable varbinary] [MaxLength = 3]
MaxLengthDataTypes.ByteArray5 ---> [nullable varbinary] [MaxLength = 5]
MaxLengthDataTypes.ByteArray9000 ---> [nullable image] [MaxLength = 1073741823]
MaxLengthDataTypes.Id ---> [int] [Precision = 10]
MaxLengthDataTypes.String3 ---> [nullable nvarchar] [MaxLength = 3]
MaxLengthDataTypes.String9000 ---> [nullable ntext] [MaxLength = 536870911]
StringForeignKeyDataType.Id ---> [int] [Precision = 10]
StringForeignKeyDataType.StringKeyDataTypeId ---> [nullable nvarchar] [MaxLength = 256]
StringKeyDataType.Id ---> [nvarchar] [MaxLength = 256]
UnicodeDataTypes.Id ---> [int] [Precision = 10]
UnicodeDataTypes.StringAnsi ---> [nullable nvarchar] [MaxLength = 4000]
UnicodeDataTypes.StringAnsi3 ---> [nullable nvarchar] [MaxLength = 3]
UnicodeDataTypes.StringAnsi9000 ---> [nullable ntext] [MaxLength = 536870911]
UnicodeDataTypes.StringDefault ---> [nullable nvarchar] [MaxLength = 4000]
UnicodeDataTypes.StringUnicode ---> [nullable nvarchar] [MaxLength = 4000]
";

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void Can_get_column_types_from_built_model()
        {
            using (var context = CreateContext())
            {
                var typeMapper = context.GetService<IRelationalTypeMapper>();

                foreach (var property in context.Model.GetEntityTypes().SelectMany(e => e.GetDeclaredProperties()))
                {
                    var columnType = property.Relational().ColumnType;
                    Assert.NotNull(columnType);

                    if (property[RelationalAnnotationNames.ColumnType] == null)
                    {
                        Assert.Equal(
                            columnType.ToLowerInvariant(),
                            typeMapper.FindMapping(property).StoreType.ToLowerInvariant());
                    }
                }
            }
        }

        private const string FileLineEnding = @"
";

        private string Sql => Fixture.TestSqlLoggerFactory.Sql.Replace(Environment.NewLine, FileLineEnding);

        private class ColumnInfo
        {
            public string TableName { get; set; }
            public string ColumnName { get; set; }
            public string DataType { get; set; }
            public bool? IsNullable { get; set; }
            public int? MaxLength { get; set; }
            public int? NumericPrecision { get; set; }
            public int? NumericScale { get; set; }
            public int? DateTimePrecision { get; set; }
        }
    }
}
