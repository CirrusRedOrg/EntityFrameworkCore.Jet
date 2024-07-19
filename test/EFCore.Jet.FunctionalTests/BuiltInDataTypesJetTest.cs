// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.OleDb;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

// ReSharper disable InconsistentNaming
// ReSharper disable ParameterOnlyUsedForPreconditionCheck.Local
// ReSharper disable CompareOfFloatsByEqualityOperator
// ReSharper disable UnusedParameter.Local
// ReSharper disable PossibleInvalidOperationException

#nullable disable

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class BuiltInDataTypesJetTest : BuiltInDataTypesTestBase<BuiltInDataTypesJetTest.BuiltInDataTypesJetFixture>
    {
        private static readonly string _eol = Environment.NewLine;

        public BuiltInDataTypesJetTest(BuiltInDataTypesJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        [ConditionalFact]
        public void Sql_translation_uses_type_mapper_when_constant()
        {
            using (var context = CreateContext())
            {
                var results
                    = context.Set<MappedNullableDataTypes>()
                        .Where(e => e.TimeSpanAsTime == new TimeSpan(0, 1, 2))
                        .Select(e => e.Int)
                        .ToList();

                Assert.Empty(results);

                AssertSql(
                    $@"SELECT `m`.`Int`
FROM `MappedNullableDataTypes` AS `m`
WHERE `m`.`TimeSpanAsTime` = TIMEVALUE('00:01:02')");
            }
        }

        [ConditionalFact(Skip = "Issue#13487")]
        public void Translate_array_length()
        {
            using (var db = CreateContext())
            {
                db.Set<MappedDataTypesWithIdentity>()
                    .Where(p => p.BytesAsImage.Length == 0)
                    .Select(p => p.BytesAsImage.Length)
                    .FirstOrDefault();

                AssertSql(
                    $@"SELECT TOP 1 CAST(DATALENGTH(`p`.`BytesAsImage`) AS int)
FROM `MappedDataTypesWithIdentity` AS `p`
WHERE CAST(DATALENGTH(`p`.`BytesAsImage`) AS int) = 0");
            }
        }

        [ConditionalFact]
        public void Sql_translation_uses_type_mapper_when_parameter()
        {
            using (var context = CreateContext())
            {
                var timeSpan = new TimeSpan(2, 1, 0);

                var results
                    = context.Set<MappedNullableDataTypes>()
                        .Where(e => e.TimeSpanAsTime == timeSpan)
                        .Select(e => e.Int)
                        .ToList();

                Assert.Empty(results);
                AssertSql(
                    $@"{AssertSqlHelper.Declaration("@__timeSpan_0='02:01:00' (Nullable = true)")}

SELECT `m`.`Int`
FROM `MappedNullableDataTypes` AS `m`
WHERE `m`.`TimeSpanAsTime` = {AssertSqlHelper.Parameter("@__timeSpan_0")}");
            }
        }

        [ConditionalFact]
        public virtual void Can_query_using_DateDiffHour_using_TimeSpan()
        {
            using (var context = CreateContext())
            {
                var timeSpan = new TimeSpan(2, 1, 0);

                var results
                    = context.Set<MappedNullableDataTypes>()
                        .Where(e => EF.Functions.DateDiffHour(e.TimeSpanAsTime, timeSpan) == 0)
                        .Select(e => e.Int)
                        .ToList();

                Assert.Empty(results);
                AssertSql(
                    $@"{AssertSqlHelper.Declaration("@__timeSpan_1='02:01:00' (Nullable = true)")}

SELECT `m`.`Int`
FROM `MappedNullableDataTypes` AS `m`
WHERE DATEDIFF('h', `m`.`TimeSpanAsTime`, {AssertSqlHelper.Parameter("@__timeSpan_1")}) = 0");
            }
        }

        [ConditionalFact]
        public virtual void Can_query_using_DateDiffMinute_using_TimeSpan()
        {
            using (var context = CreateContext())
            {
                var timeSpan = new TimeSpan(2, 1, 0);

                var results
                    = context.Set<MappedNullableDataTypes>()
                        .Where(e => EF.Functions.DateDiffMinute(e.TimeSpanAsTime, timeSpan) == 0)
                        .Select(e => e.Int)
                        .ToList();

                Assert.Empty(results);
                AssertSql(
                    $@"{AssertSqlHelper.Declaration("@__timeSpan_1='02:01:00' (Nullable = true)")}

SELECT `m`.`Int`
FROM `MappedNullableDataTypes` AS `m`
WHERE DATEDIFF('n', `m`.`TimeSpanAsTime`, {AssertSqlHelper.Parameter("@__timeSpan_1")}) = 0");
            }
        }

        [ConditionalFact]
        public virtual void Can_query_using_DateDiffSecond_using_TimeSpan()
        {
            using (var context = CreateContext())
            {
                var timeSpan = new TimeSpan(2, 1, 0);

                var results
                    = context.Set<MappedNullableDataTypes>()
                        .Where(e => EF.Functions.DateDiffSecond(e.TimeSpanAsTime, timeSpan) == 0)
                        .Select(e => e.Int)
                        .ToList();

                Assert.Empty(results);
                AssertSql(
                    $@"{AssertSqlHelper.Declaration("@__timeSpan_1='02:01:00' (Nullable = true)")}

SELECT `m`.`Int`
FROM `MappedNullableDataTypes` AS `m`
WHERE DATEDIFF('s', `m`.`TimeSpanAsTime`, {AssertSqlHelper.Parameter("@__timeSpan_1")}) = 0");
            }
        }

        [ConditionalFact]
        public virtual void Can_query_using_any_mapped_data_type()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedNullableDataTypes>().Add(
                    new MappedNullableDataTypes
                    {
                        Int = 999,
                        LongAsBigint = 78L,
                        ShortAsSmallint = 79,
                        ByteAsTinyint = 80,
                        UintAsInt = uint.MaxValue,
                        UlongAsBigint = ulong.MaxValue,
                        UShortAsSmallint = ushort.MaxValue,
                        SbyteAsTinyint = sbyte.MinValue,
                        BoolAsBit = true,
                        DecimalAsMoney = 81.1m,
                        //DecimalAsSmallmoney = 82.2m,
                        DoubleAsFloat = 83.3,
                        FloatAsReal = 84.4f,
                        DoubleAsDoublePrecision = 85.5,
                        DateOnlyAsDate = new DateOnly(1605, 1, 2),
                        DateTimeAsDate = new DateTime(1605, 1, 2, 10, 11, 12),
                        //DateTimeOffsetAsDatetimeoffset = new DateTimeOffset(new DateTime(), TimeSpan.Zero),
                        //DateTimeAsDatetime2 = new DateTime(),
                        //DateTimeAsSmalldatetime = new DateTime(2018, 1, 2, 13, 11, 12),
                        DateTimeAsDatetime = new DateTime(2019, 1, 2, 14, 11, 12),
                        TimeOnlyAsTime = new TimeOnly(11, 15, 12, 2),
                        TimeSpanAsTime = new TimeSpan(0, 11, 15, 12, 2),
                        StringAsVarcharMax = "C",
                        StringAsCharVaryingMax = "Your",
                        StringAsCharacterVaryingMax = "strong",
                        StringAsNvarcharMax = "don't",
                        StringAsNationalCharVaryingMax = "help",
                        StringAsNationalCharacterVaryingMax = "anyone!",
                        StringAsText = "Gumball Rules!",
                        StringAsNtext = "Gumball Rules OK!",
                        BytesAsVarbinaryMax = new byte[] { 89, 90, 91, 92 },
                        BytesAsBinaryVaryingMax = new byte[] { 93, 94, 95, 96 },
                        BytesAsImage = new byte[] { 97, 98, 99, 100 },
                        Decimal = 101.7m,
                        DecimalAsDec = 102.8m,
                        DecimalAsNumeric = 103.9m,
                        GuidAsUniqueidentifier = new Guid("A8F9F951-145F-4545-AC60-B92FF57ADA47"),
                        UintAsBigint = uint.MaxValue,
                        UlongAsDecimal200 = ulong.MaxValue,
                        UShortAsInt = ushort.MaxValue,
                        SByteAsSmallint = sbyte.MinValue,
                        CharAsVarchar = 'A',
                        CharAsAsCharVarying = 'B',
                        CharAsCharacterVaryingMax = 'C',
                        CharAsNvarchar = 'D',
                        CharAsNationalCharVarying = 'E',
                        CharAsNationalCharacterVaryingMax = 'F',
                        CharAsText = 'G',
                        CharAsNtext = 'H',
                        CharAsInt = 'I',
                        EnumAsNvarchar20 = StringEnumU16.Value4,
                        EnumAsVarcharMax = StringEnum16.Value2
                    });

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                var entity = context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999);

                long? param1 = 78L;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.LongAsBigint == param1));

                short? param2 = 79;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.ShortAsSmallint == param2));

                byte? param3 = 80;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.ByteAsTinyint == param3));

                bool? param4 = true;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.BoolAsBit == param4));

                decimal? param5 = 81.1m;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.DecimalAsMoney == param5));

                //decimal? param6 = 82.2m;
                //Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.DecimalAsSmallmoney == param6));

                double? param7a = 83.3;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.DoubleAsFloat == param7a));

                float? param7b = 84.4f;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.FloatAsReal == param7b));

                double? param7c = 85.5;
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.DoubleAsDoublePrecision == param7c));

                DateOnly? param8a = new DateOnly(1605, 1, 2);
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.DateOnlyAsDate == param8a));

                DateTime? param8b = new DateTime(1605, 1, 2);
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.DateTimeAsDate == param8b));

                /*DateTimeOffset? param9 = new DateTimeOffset(new DateTime(), TimeSpan.Zero);
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.DateTimeOffsetAsDatetimeoffset == param9));

                DateTime? param10 = new DateTime();
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.DateTimeAsDatetime2 == param10));

                DateTime? param11 = new DateTime(2019, 1, 2, 14, 11, 12);
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.DateTimeAsDatetime == param11));

                DateTime? param12 = new DateTime(2018, 1, 2, 13, 11, 0);
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.DateTimeAsSmalldatetime == param12));*/

                TimeOnly? param13a = new TimeOnly(11, 15, 12, 2);
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.TimeOnlyAsTime == param13a));

                TimeSpan? param13b = new TimeSpan(0, 11, 15, 12, 2);
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.TimeSpanAsTime == param13b));

                var param19 = "C";
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.StringAsVarcharMax == param19));

                var param20 = "Your";
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.StringAsCharVaryingMax == param20));

                var param21 = "strong";
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.StringAsCharacterVaryingMax == param21));

                var param27 = "don't";
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.StringAsNvarcharMax == param27));

                var param28 = "help";
                Assert.Same(
                    entity,
                    context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.StringAsNationalCharVaryingMax == param28));

                var param29 = "anyone!";
                Assert.Same(
                    entity,
                    context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.StringAsNationalCharacterVaryingMax == param29));

                var param35 = new byte[] { 89, 90, 91, 92 };
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.BytesAsVarbinaryMax == param35));

                var param36 = new byte[] { 93, 94, 95, 96 };
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.BytesAsBinaryVaryingMax == param36));

                decimal? param38 = 102m;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.Decimal == param38));

                decimal? param39 = 103m;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.DecimalAsDec == param39));

                decimal? param40 = 104m;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.DecimalAsNumeric == param40));

                uint? param41 = uint.MaxValue;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.UintAsInt == param41));

                ulong? param42 = ulong.MaxValue;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.UlongAsBigint == param42));

                ushort? param43 = ushort.MaxValue;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.UShortAsSmallint == param43));

                sbyte? param44 = sbyte.MinValue;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.SbyteAsTinyint == param44));

                uint? param45 = uint.MaxValue;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.UintAsBigint == param45));

                ulong? param46 = ulong.MaxValue;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.UlongAsDecimal200 == param46));

                ushort? param47 = ushort.MaxValue;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.UShortAsInt == param47));

                sbyte? param48 = sbyte.MinValue;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.SByteAsSmallint == param48));

                Guid? param49 = new Guid("A8F9F951-145F-4545-AC60-B92FF57ADA47");
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.GuidAsUniqueidentifier == param49));

                char? param50 = 'A';
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.CharAsVarchar == param50));

                char? param51 = 'B';
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.CharAsAsCharVarying == param51));

                char? param52 = 'C';
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.CharAsCharacterVaryingMax == param52));

                char? param53 = 'D';
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.CharAsNvarchar == param53));

                char? param54 = 'E';
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.CharAsNationalCharVarying == param54));

                char? param55 = 'F';
                Assert.Same(
                    entity,
                    context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.CharAsNationalCharacterVaryingMax == param55));

                char? param58 = 'I';
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.CharAsInt == param58));

                StringEnumU16? param59 = StringEnumU16.Value4;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.EnumAsNvarchar20 == param59));

                StringEnum16? param60 = StringEnum16.Value2;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 999 && e.EnumAsVarcharMax == param60));
            }
        }

        [ConditionalFact]
        public virtual void Can_query_using_any_mapped_data_types_with_nulls()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedNullableDataTypes>().Add(
                    new MappedNullableDataTypes { Int = 911 });

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                var entity = context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911);

                long? param1 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.LongAsBigint == param1));

                short? param2 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.ShortAsSmallint == param2));
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && (long?)e.ShortAsSmallint == param2));

                byte? param3 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.ByteAsTinyint == param3));

                bool? param4 = false;//bit is never nullable
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.BoolAsBit == param4));

                decimal? param5 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.DecimalAsMoney == param5));

                //decimal? param6 = null;
                //Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.DecimalAsSmallmoney == param6));

                double? param7a = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.DoubleAsFloat == param7a));

                float? param7b = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.FloatAsReal == param7b));

                double? param7c = null;
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.DoubleAsDoublePrecision == param7c));

                DateOnly? param8a = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.DateOnlyAsDate == param8a));

                DateTime? param8b = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.DateTimeAsDate == param8b));

                /*DateTimeOffset? param9 = null;
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.DateTimeOffsetAsDatetimeoffset == param9));

                DateTime? param10 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.DateTimeAsDatetime2 == param10));

                DateTime? param11 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.DateTimeAsDatetime == param11));

                DateTime? param12 = null;
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.DateTimeAsSmalldatetime == param12));*/

                TimeOnly? param13a = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.TimeOnlyAsTime == param13a));

                TimeSpan? param13b = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.TimeSpanAsTime == param13b));

                string param19 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.StringAsVarcharMax == param19));

                string param20 = null;
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.StringAsCharVaryingMax == param20));

                string param21 = null;
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.StringAsCharacterVaryingMax == param21));

                string param27 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.StringAsNvarcharMax == param27));

                string param28 = null;
                Assert.Same(
                    entity,
                    context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.StringAsNationalCharVaryingMax == param28));

                string param29 = null;
                Assert.Same(
                    entity,
                    context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.StringAsNationalCharacterVaryingMax == param29));

                string param30 = null;

                Assert.Same(
                    entity,
                    context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.StringAsText == param30));

                string param31 = null;
                Assert.Same(
                    entity,
                    context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.StringAsNtext == param31));

                byte[] param35 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.BytesAsVarbinaryMax == param35));

                byte[] param36 = null;
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.BytesAsBinaryVaryingMax == param36));

                byte[] param37 = null;
                Assert.Same(
                    entity,
                    context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.BytesAsImage == param37));

                decimal? param38 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.Decimal == param38));

                decimal? param39 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.DecimalAsDec == param39));

                decimal? param40 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.DecimalAsNumeric == param40));

                uint? param41 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.UintAsInt == param41));

                ulong? param42 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.UlongAsBigint == param42));

                ushort? param43 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.UShortAsSmallint == param43));

                sbyte? param44 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.SbyteAsTinyint == param44));

                uint? param45 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.UintAsBigint == param45));

                ulong? param46 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.UlongAsDecimal200 == param46));

                ushort? param47 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.UShortAsInt == param47));

                sbyte? param48 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.SByteAsSmallint == param48));

                Guid? param49 = null;
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.GuidAsUniqueidentifier == param49));

                char? param50 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.CharAsVarchar == param50));

                char? param51 = null;
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.CharAsAsCharVarying == param51));

                char? param52 = null;
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.CharAsCharacterVaryingMax == param52));

                char? param53 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.CharAsNvarchar == param53));

                char? param54 = null;
                Assert.Same(
                    entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.CharAsNationalCharVarying == param54));

                char? param55 = null;
                Assert.Same(
                    entity,
                    context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.CharAsNationalCharacterVaryingMax == param55));

                //char? param56 = null;
                //Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.CharAsText == param56));

                //char? param57 = null;
                //Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.CharAsNtext == param57));

                char? param58 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.CharAsInt == param58));

                StringEnumU16? param59 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.EnumAsNvarchar20 == param59));

                StringEnum16? param60 = null;
                Assert.Same(entity, context.Set<MappedNullableDataTypes>().Single(e => e.Int == 911 && e.EnumAsVarcharMax == param60));
            }
        }

        [ConditionalFact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types()
        {
            bool isoledb = false;
            var entity = CreateMappedDataTypes(77);
            using (var context = CreateContext())
            {
                context.Set<MappedDataTypes>().Add(entity);
                isoledb = ((EntityFrameworkCore.Jet.Data.JetConnection)context.Database.GetDbConnection())
                    .DataAccessProviderFactory is OleDbFactory;
                Assert.Equal(1, context.SaveChanges());
            }

            var parameters = DumpParameters();
            Assert.Equal(
               $@"@p0='77'
@p1='True'
@p2='80' (Size = 1)
@p3='0x5D5E5F60' (Nullable = false) (Size = 510)
@p4='0x61626364' (Nullable = false) (Size = 510)
@p5='0x595A5B5C' (Nullable = false) (Size = 510)
@p6='B' (Nullable = false) (Size = 1)
@p7='C' (Nullable = false) (Size = 255)
@p8='73'
@p9='E' (Nullable = false) (Size = 1)
@p10='F' (Nullable = false) (Size = 255)
@p11='H' (Nullable = false) (Size = 1)
@p12='D' (Nullable = false) (Size = 1)
@p13='G' (Nullable = false) (Size = 1)
@p14='A' (Nullable = false) (Size = 1)
@p15='2015-01-02T00:00:00.0000000' (DbType = Date)
@p16='2015-01-02T00:00:00.0000000' (DbType = DateTime)
@p17='2019-01-02T14:11:12.0000000' (DbType = DateTime)
@p18='101' (Precision = 18)
@p19='102' (Precision = 18)
@p20='81.1' (Precision = 3) (Scale = 1){(isoledb ? " (DbType = Currency)" : "")}
@p21='103' (Precision = 18)
@p22='85.5'
@p23='83.3'
@p24='4'
@p25='Value2' (Nullable = false) (Size = 255)
@p26='84.4'
@p27='a8f9f951-145f-4545-ac60-b92ff57ada47'
@p28='78' (DbType = Decimal)
@p29='-128'
@p30='128' (Size = 1)
@p31='79'
@p32='Your' (Nullable = false) (Size = 255)
@p33='And now' (Nullable = false) (Size = 255)
@p34='strong' (Nullable = false) (Size = 255)
@p35='this...' (Nullable = false) (Size = 255)
@p36='help' (Nullable = false) (Size = 255)
@p37='anyone!' (Nullable = false) (Size = 255)
@p38='Gumball Rules OK!' (Nullable = false) (Size = 255)
@p39='DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD' (Nullable = false) (Size = 255)
@p40='Gumball Rules!' (Nullable = false) (Size = 255)
@p41='CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC' (Nullable = false) (Size = 255)
@p42='EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE' (Nullable = false) (Size = 255)
@p43='11:15:12'
@p44='11:15:12'
@p45='65535'
@p46='-1'
@p47='4294967295' (DbType = Decimal)
@p48='-1'
@p49='18446744073709551615' (Precision = 20)
@p50='18446744073709551615' (Precision = 20)",
                parameters,
                ignoreLineEndingDifferences: true);

            using (var context = CreateContext())
            {
                AssertMappedDataTypes(context.Set<MappedDataTypes>().Single(e => e.Int == 77), 77);
            }
        }

        private string DumpParameters()
            => Fixture.TestSqlLoggerFactory.Parameters.Single().Replace(", ", _eol);

        private static void AssertMappedDataTypes(MappedDataTypes entity, int id)
        {
            var expected = CreateMappedDataTypes(id);
            Assert.Equal(id, entity.Int);
            Assert.Equal(78, entity.LongAsBigInt);
            Assert.Equal(79, entity.ShortAsSmallint);
            Assert.Equal(80, entity.ByteAsTinyint);
            Assert.Equal(uint.MaxValue, entity.UintAsInt);
            Assert.Equal(ulong.MaxValue, entity.UlongAsBigint);
            Assert.Equal(ushort.MaxValue, entity.UShortAsSmallint);
            Assert.Equal(sbyte.MinValue, entity.SByteAsTinyint);
            Assert.True(entity.BoolAsBit);
            Assert.Equal(81.1m, entity.DecimalAsMoney);
            //Assert.Equal(82.2m, entity.DecimalAsSmallmoney);
            Assert.Equal(83.3, entity.DoubleAsFloat);
            Assert.Equal(84.4f, entity.FloatAsReal);
            Assert.Equal(85.5, entity.DoubleAsDoublePrecision);
            Assert.Equal(new DateOnly(2015, 1, 2), entity.DateOnlyAsDate);
            Assert.Equal(new DateTime(2015, 1, 2), entity.DateTimeAsDate);
            /*Assert.Equal(
                new DateTimeOffset(new DateTime(2016, 1, 2, 11, 11, 12).AddTicks(1234567), TimeSpan.Zero),
                entity.DateTimeOffsetAsDatetimeoffset);
            Assert.Equal(new DateTime(2017, 1, 2, 12, 11, 12).AddTicks(1234567), entity.DateTimeAsDatetime2);
            Assert.Equal(new DateTime(2018, 1, 2, 13, 11, 00), entity.DateTimeAsSmalldatetime);*/
            Assert.Equal(new DateTime(2019, 1, 2, 14, 11, 12), entity.DateTimeAsDatetime);
            Assert.Equal(new TimeOnly(11, 15, 12), entity.TimeOnlyAsTime);
            Assert.Equal(new TimeSpan(11, 15, 12), entity.TimeSpanAsTime);
            Assert.Equal(expected.StringAsVarcharMax, entity.StringAsVarcharMax);
            Assert.Equal("Your", entity.StringAsCharVaryingMax);
            Assert.Equal("strong", entity.StringAsCharacterVaryingMax);
            Assert.Equal(expected.StringAsNvarcharMax, entity.StringAsNvarcharMax);
            Assert.Equal("help", entity.StringAsNationalCharVaryingMax);
            Assert.Equal("anyone!", entity.StringAsNationalCharacterVaryingMax);
            Assert.Equal(expected.StringAsVarcharMaxUtf8, entity.StringAsVarcharMaxUtf8);
            Assert.Equal("And now", entity.StringAsCharVaryingMaxUtf8);
            Assert.Equal("this...", entity.StringAsCharacterVaryingMaxUtf8);
            Assert.Equal("Gumball Rules!", entity.StringAsText);
            Assert.Equal("Gumball Rules OK!", entity.StringAsNtext);
            Assert.Equal(new byte[] { 89, 90, 91, 92 }, entity.BytesAsVarbinaryMax);
            Assert.Equal(new byte[] { 93, 94, 95, 96 }, entity.BytesAsBinaryVaryingMax);
            Assert.Equal(new byte[] { 97, 98, 99, 100 }, entity.BytesAsImage);
            Assert.Equal(101m, entity.Decimal);
            Assert.Equal(102m, entity.DecimalAsDec);
            Assert.Equal(103m, entity.DecimalAsNumeric);
            Assert.Equal(new Guid("A8F9F951-145F-4545-AC60-B92FF57ADA47"), entity.GuidAsUniqueidentifier);
            Assert.Equal(uint.MaxValue, entity.UintAsBigint);
            Assert.Equal(ulong.MaxValue, entity.UlongAsDecimal200);
            Assert.Equal(ushort.MaxValue, entity.UShortAsInt);
            Assert.Equal(sbyte.MinValue, entity.SByteAsSmallint);
            Assert.Equal('A', entity.CharAsVarchar);
            Assert.Equal('B', entity.CharAsAsCharVarying);
            Assert.Equal('C', entity.CharAsCharacterVaryingMax);
            Assert.Equal('D', entity.CharAsNvarchar);
            Assert.Equal('E', entity.CharAsNationalCharVarying);
            Assert.Equal('F', entity.CharAsNationalCharacterVaryingMax);
            Assert.Equal('G', entity.CharAsText);
            Assert.Equal('H', entity.CharAsNtext);
            Assert.Equal('I', entity.CharAsInt);
            Assert.Equal(StringEnum16.Value2, entity.EnumAsVarcharMax);
            Assert.Equal(StringEnumU16.Value4, entity.EnumAsNvarchar20);
        }

        private static MappedDataTypes CreateMappedDataTypes(int id)
            => new MappedDataTypes
            {
                Int = id,
                LongAsBigInt = 78L,
                ShortAsSmallint = 79,
                ByteAsTinyint = 80,
                UintAsInt = uint.MaxValue,
                UlongAsBigint = ulong.MaxValue,
                UShortAsSmallint = ushort.MaxValue,
                SByteAsTinyint = sbyte.MinValue,
                BoolAsBit = true,
                DecimalAsMoney = 81.1m,
                //DecimalAsSmallmoney = 82.2m,
                DoubleAsFloat = 83.3,
                FloatAsReal = 84.4f,
                DoubleAsDoublePrecision = 85.5,
                DateOnlyAsDate = new DateOnly(2015, 1, 2),
                DateTimeAsDate = new DateTime(2015, 1, 2, 10, 11, 12),
                //DateTimeOffsetAsDatetimeoffset = new DateTimeOffset(new DateTime(2016, 1, 2, 11, 11, 12).AddTicks(1234567), TimeSpan.Zero),
                //DateTimeAsDatetime2 = new DateTime(2017, 1, 2, 12, 11, 12).AddTicks(1234567),
                //DateTimeAsSmalldatetime = new DateTime(2018, 1, 2, 13, 11, 12),
                DateTimeAsDatetime = new DateTime(2019, 1, 2, 14, 11, 12),
                TimeOnlyAsTime = new TimeOnly(11, 15, 12),
                TimeSpanAsTime = new TimeSpan(11, 15, 12),
                StringAsVarcharMax = string.Concat(Enumerable.Repeat("C", 255)),
                StringAsCharVaryingMax = "Your",
                StringAsCharacterVaryingMax = "strong",
                StringAsNvarcharMax = string.Concat(Enumerable.Repeat("D", 255)),
                StringAsNationalCharVaryingMax = "help",
                StringAsNationalCharacterVaryingMax = "anyone!",
                StringAsVarcharMaxUtf8 = string.Concat(Enumerable.Repeat("E", 255)),
                StringAsCharVaryingMaxUtf8 = "And now",
                StringAsCharacterVaryingMaxUtf8 = "this...",
                StringAsText = "Gumball Rules!",
                StringAsNtext = "Gumball Rules OK!",
                BytesAsVarbinaryMax = new byte[] { 89, 90, 91, 92 },
                BytesAsBinaryVaryingMax = new byte[] { 93, 94, 95, 96 },
                BytesAsImage = new byte[] { 97, 98, 99, 100 },
                Decimal = 101m,
                DecimalAsDec = 102m,
                DecimalAsNumeric = 103m,
                GuidAsUniqueidentifier = new Guid("A8F9F951-145F-4545-AC60-B92FF57ADA47"),
                UintAsBigint = uint.MaxValue,
                UlongAsDecimal200 = ulong.MaxValue,
                UShortAsInt = ushort.MaxValue,
                SByteAsSmallint = sbyte.MinValue,
                CharAsVarchar = 'A',
                CharAsAsCharVarying = 'B',
                CharAsCharacterVaryingMax = 'C',
                CharAsNvarchar = 'D',
                CharAsNationalCharVarying = 'E',
                CharAsNationalCharacterVaryingMax = 'F',
                CharAsText = 'G',
                CharAsNtext = 'H',
                CharAsInt = 'I',
                EnumAsNvarchar20 = StringEnumU16.Value4,
                EnumAsVarcharMax = StringEnum16.Value2
            };

        [ConditionalFact]
        public virtual void Can_insert_and_read_back_all_mapped_nullable_data_types()
        {
            bool isoledb = false;
            using (var context = CreateContext())
            {
                context.Set<MappedNullableDataTypes>().Add(CreateMappedNullableDataTypes(77));
                isoledb = ((EntityFrameworkCore.Jet.Data.JetConnection)context.Database.GetDbConnection())
                    .DataAccessProviderFactory is OleDbFactory;
                Assert.Equal(1, context.SaveChanges());
            }

            var parameters = DumpParameters();
            Assert.Equal(
               $@"@p0='77'
@p1='True' (Nullable = true)
@p2='80' (Nullable = true) (Size = 1)
@p3='0x5D5E5F60' (Size = 510)
@p4='0x61626364' (Size = 510)
@p5='0x595A5B5C' (Size = 510)
@p6='B' (Size = 1)
@p7='C' (Size = 255)
@p8='73' (Nullable = true)
@p9='E' (Size = 1)
@p10='F' (Size = 255)
@p11='H' (Size = 1)
@p12='D' (Size = 1)
@p13='G' (Size = 1)
@p14='A' (Size = 1)
@p15='2015-01-02T00:00:00.0000000' (Nullable = true) (DbType = Date)
@p16='2015-01-02T00:00:00.0000000' (Nullable = true) (DbType = DateTime)
@p17='2019-01-02T14:11:12.0000000' (Nullable = true) (DbType = DateTime)
@p18='101' (Nullable = true) (Precision = 18)
@p19='102' (Nullable = true) (Precision = 18)
@p20='81.1' (Nullable = true) (Precision = 3) (Scale = 1){(isoledb ? " (DbType = Currency)" : "")}
@p21='103' (Nullable = true) (Precision = 18)
@p22='85.5' (Nullable = true)
@p23='83.3' (Nullable = true)
@p24='4' (Nullable = true)
@p25='Value2' (Size = 255)
@p26='84.4' (Nullable = true)
@p27='a8f9f951-145f-4545-ac60-b92ff57ada47' (Nullable = true)
@p28='78' (Nullable = true) (DbType = Decimal)
@p29='-128' (Nullable = true)
@p30='128' (Nullable = true) (Size = 1)
@p31='79' (Nullable = true)
@p32='Your' (Size = 255)
@p33='And now' (Size = 255)
@p34='strong' (Size = 255)
@p35='this...' (Size = 255)
@p36='help' (Size = 255)
@p37='anyone!' (Size = 255)
@p38='Gumball Rules OK!' (Size = 255)
@p39='don't' (Size = 255)
@p40='Gumball Rules!' (Size = 255)
@p41='C' (Size = 255)
@p42='short' (Size = 255)
@p43='11:15:12' (Nullable = true)
@p44='11:15:12' (Nullable = true)
@p45='65535' (Nullable = true)
@p46='-1' (Nullable = true)
@p47='4294967295' (Nullable = true) (DbType = Decimal)
@p48='-1' (Nullable = true)
@p49='18446744073709551615' (Nullable = true) (Precision = 20)
@p50='18446744073709551615' (Nullable = true) (Precision = 20)",
                parameters,
                ignoreLineEndingDifferences: true);

            using (var context = CreateContext())
            {
                AssertMappedNullableDataTypes(context.Set<MappedNullableDataTypes>().Single(e => e.Int == 77), 77);
            }
        }

        private static void AssertMappedNullableDataTypes(MappedNullableDataTypes entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Equal(78, entity.LongAsBigint);
            Assert.Equal(79, entity.ShortAsSmallint.Value);
            Assert.Equal(80, entity.ByteAsTinyint.Value);
            Assert.Equal(uint.MaxValue, entity.UintAsInt);
            Assert.Equal(ulong.MaxValue, entity.UlongAsBigint);
            Assert.Equal(ushort.MaxValue, entity.UShortAsSmallint);
            Assert.Equal(sbyte.MinValue, entity.SbyteAsTinyint);
            Assert.True(entity.BoolAsBit);
            Assert.Equal(81.1m, entity.DecimalAsMoney);
            //Assert.Equal(82.2m, entity.DecimalAsSmallmoney);
            Assert.Equal(83.3, entity.DoubleAsFloat);
            Assert.Equal(84.4f, entity.FloatAsReal);
            Assert.Equal(85.5, entity.DoubleAsDoublePrecision);
            Assert.Equal(new DateOnly(2015, 1, 2), entity.DateOnlyAsDate);
            Assert.Equal(new DateTime(2015, 1, 2), entity.DateTimeAsDate);
            /*Assert.Equal(
                new DateTimeOffset(new DateTime(2016, 1, 2, 11, 11, 12).AddTicks(9876543), TimeSpan.Zero),
                entity.DateTimeOffsetAsDatetimeoffset);
            Assert.Equal(new DateTime(2017, 1, 2, 12, 11, 12).AddTicks(9876543), entity.DateTimeAsDatetime2);
            Assert.Equal(new DateTime(2018, 1, 2, 13, 11, 00), entity.DateTimeAsSmalldatetime);*/
            Assert.Equal(new DateTime(2019, 1, 2, 14, 11, 12), entity.DateTimeAsDatetime);
            Assert.Equal(new TimeOnly(11, 15, 12), entity.TimeOnlyAsTime);
            Assert.Equal(new TimeSpan(11, 15, 12), entity.TimeSpanAsTime);
            Assert.Equal("C", entity.StringAsVarcharMax);
            Assert.Equal("Your", entity.StringAsCharVaryingMax);
            Assert.Equal("strong", entity.StringAsCharacterVaryingMax);
            Assert.Equal("don't", entity.StringAsNvarcharMax);
            Assert.Equal("help", entity.StringAsNationalCharVaryingMax);
            Assert.Equal("anyone!", entity.StringAsNationalCharacterVaryingMax);
            Assert.Equal("Gumball Rules!", entity.StringAsText);
            Assert.Equal("Gumball Rules OK!", entity.StringAsNtext);
            Assert.Equal(new byte[] { 89, 90, 91, 92 }, entity.BytesAsVarbinaryMax);
            Assert.Equal(new byte[] { 93, 94, 95, 96 }, entity.BytesAsBinaryVaryingMax);
            Assert.Equal(new byte[] { 97, 98, 99, 100 }, entity.BytesAsImage);
            Assert.Equal(101m, entity.Decimal);
            Assert.Equal(102m, entity.DecimalAsDec);
            Assert.Equal(103m, entity.DecimalAsNumeric);
            Assert.Equal(new Guid("A8F9F951-145F-4545-AC60-B92FF57ADA47"), entity.GuidAsUniqueidentifier);
            Assert.Equal(uint.MaxValue, entity.UintAsBigint);
            Assert.Equal(ulong.MaxValue, entity.UlongAsDecimal200);
            Assert.Equal(ushort.MaxValue, entity.UShortAsInt);
            Assert.Equal(sbyte.MinValue, entity.SByteAsSmallint);
            Assert.Equal('A', entity.CharAsVarchar);
            Assert.Equal('B', entity.CharAsAsCharVarying);
            Assert.Equal('C', entity.CharAsCharacterVaryingMax);
            Assert.Equal('D', entity.CharAsNvarchar);
            Assert.Equal('E', entity.CharAsNationalCharVarying);
            Assert.Equal('F', entity.CharAsNationalCharacterVaryingMax);
            Assert.Equal('G', entity.CharAsText);
            Assert.Equal('H', entity.CharAsNtext);
            Assert.Equal('I', entity.CharAsInt);
            Assert.Equal(StringEnum16.Value2, entity.EnumAsVarcharMax);
            Assert.Equal(StringEnumU16.Value4, entity.EnumAsNvarchar20);
        }

        private static MappedNullableDataTypes CreateMappedNullableDataTypes(int id)
            => new MappedNullableDataTypes
            {
                Int = id,
                LongAsBigint = 78L,
                ShortAsSmallint = 79,
                ByteAsTinyint = 80,
                UintAsInt = uint.MaxValue,
                UlongAsBigint = ulong.MaxValue,
                UShortAsSmallint = ushort.MaxValue,
                SbyteAsTinyint = sbyte.MinValue,
                BoolAsBit = true,
                DecimalAsMoney = 81.1m,
                //DecimalAsSmallmoney = 82.2m,
                DoubleAsFloat = 83.3,
                FloatAsReal = 84.4f,
                DoubleAsDoublePrecision = 85.5,
                DateOnlyAsDate = new DateOnly(2015, 1, 2),
                DateTimeAsDate = new DateTime(2015, 1, 2, 10, 11, 12),
                //DateTimeOffsetAsDatetimeoffset = new DateTimeOffset(new DateTime(2016, 1, 2, 11, 11, 12).AddTicks(9876543), TimeSpan.Zero),
                //DateTimeAsDatetime2 = new DateTime(2017, 1, 2, 12, 11, 12).AddTicks(9876543),
                //DateTimeAsSmalldatetime = new DateTime(2018, 1, 2, 13, 11, 12),
                DateTimeAsDatetime = new DateTime(2019, 1, 2, 14, 11, 12),
                TimeOnlyAsTime = new TimeOnly(11, 15, 12),
                TimeSpanAsTime = new TimeSpan(11, 15, 12),
                StringAsVarcharMax = "C",
                StringAsCharVaryingMax = "Your",
                StringAsCharacterVaryingMax = "strong",
                StringAsNvarcharMax = "don't",
                StringAsNationalCharVaryingMax = "help",
                StringAsNationalCharacterVaryingMax = "anyone!",
                StringAsVarcharMaxUtf8 = "short",
                StringAsCharVaryingMaxUtf8 = "And now",
                StringAsCharacterVaryingMaxUtf8 = "this...",
                StringAsText = "Gumball Rules!",
                StringAsNtext = "Gumball Rules OK!",
                BytesAsVarbinaryMax = new byte[] { 89, 90, 91, 92 },
                BytesAsBinaryVaryingMax = new byte[] { 93, 94, 95, 96 },
                BytesAsImage = new byte[] { 97, 98, 99, 100 },
                Decimal = 101m,
                DecimalAsDec = 102m,
                DecimalAsNumeric = 103m,
                GuidAsUniqueidentifier = new Guid("A8F9F951-145F-4545-AC60-B92FF57ADA47"),
                UintAsBigint = uint.MaxValue,
                UlongAsDecimal200 = ulong.MaxValue,
                UShortAsInt = ushort.MaxValue,
                SByteAsSmallint = sbyte.MinValue,
                CharAsVarchar = 'A',
                CharAsAsCharVarying = 'B',
                CharAsCharacterVaryingMax = 'C',
                CharAsNvarchar = 'D',
                CharAsNationalCharVarying = 'E',
                CharAsNationalCharacterVaryingMax = 'F',
                CharAsText = 'G',
                CharAsNtext = 'H',
                CharAsInt = 'I',
                EnumAsNvarchar20 = StringEnumU16.Value4,
                EnumAsVarcharMax = StringEnum16.Value2
            };

        [ConditionalFact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_set_to_null()
        {
            bool isoledb = false;
            using (var context = CreateContext())
            {
                context.Set<MappedNullableDataTypes>().Add(new MappedNullableDataTypes { Int = 78 });
                isoledb = ((EntityFrameworkCore.Jet.Data.JetConnection)context.Database.GetDbConnection())
                    .DataAccessProviderFactory is OleDbFactory;
                Assert.Equal(1, context.SaveChanges());
            }

            var parameters = DumpParameters();
            Assert.Equal(
               $@"@p0='78'
@p1=NULL (DbType = Boolean)
@p2=NULL (DbType = Byte)
@p3=NULL (Size = 510) (DbType = Binary)
@p4=NULL (Size = 510) (DbType = Binary)
@p5=NULL (Size = 510) (DbType = Binary)
@p6=NULL (Size = 1)
@p7=NULL (Size = 255)
@p8=NULL (DbType = Int32)
@p9=NULL (Size = 1)
@p10=NULL (Size = 255)
@p11=NULL (Size = 1)
@p12=NULL (Size = 1)
@p13=NULL (Size = 1)
@p14=NULL (Size = 1)
@p15=NULL (DbType = Date)
@p16=NULL (DbType = DateTime)
@p17=NULL (DbType = DateTime)
@p18=NULL (Precision = 18) (DbType = Decimal)
@p19=NULL (Precision = 18) (DbType = Decimal)
@p20=NULL {(isoledb ? "(DbType = Currency)" : "(DbType = Decimal)")}
@p21=NULL (Precision = 18) (DbType = Decimal)
@p22=NULL (DbType = Double)
@p23=NULL (DbType = Double)
@p24=NULL (DbType = Int32)
@p25=NULL (Size = 255)
@p26=NULL (DbType = Single)
@p27=NULL (DbType = Guid)
@p28=NULL (DbType = Decimal)
@p29=NULL (DbType = Int16)
@p30=NULL (DbType = Byte)
@p31=NULL (DbType = Int16)
@p32=NULL (Size = 255)
@p33=NULL (Size = 255)
@p34=NULL (Size = 255)
@p35=NULL (Size = 255)
@p36=NULL (Size = 255)
@p37=NULL (Size = 255)
@p38=NULL (Size = 255)
@p39=NULL (Size = 255)
@p40=NULL (Size = 255)
@p41=NULL (Size = 255)
@p42=NULL (Size = 255)
@p43=NULL (DbType = Time)
@p44=NULL (DbType = Time)
@p45=NULL (DbType = Int32)
@p46=NULL (DbType = Int16)
@p47=NULL (DbType = Decimal)
@p48=NULL (DbType = Int32)
@p49=NULL (Precision = 20) (DbType = Decimal)
@p50=NULL (Precision = 20) (DbType = Decimal)",
                parameters,
                ignoreLineEndingDifferences: true);

            using (var context = CreateContext())
            {
                AssertNullMappedNullableDataTypes(context.Set<MappedNullableDataTypes>().Single(e => e.Int == 78), 78);
            }
        }

        private static void AssertNullMappedNullableDataTypes(MappedNullableDataTypes entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Null(entity.LongAsBigint);
            Assert.Null(entity.ShortAsSmallint);
            Assert.Null(entity.ByteAsTinyint);
            Assert.Null(entity.UintAsInt);
            Assert.Null(entity.UlongAsBigint);
            Assert.Null(entity.UShortAsSmallint);
            Assert.Null(entity.SbyteAsTinyint);
            Assert.Equal(entity.BoolAsBit, false);//bits not null in jet
            Assert.Null(entity.DecimalAsMoney);
            //Assert.Null(entity.DecimalAsSmallmoney);
            Assert.Null(entity.DoubleAsFloat);
            Assert.Null(entity.FloatAsReal);
            Assert.Null(entity.DoubleAsDoublePrecision);
            Assert.Null(entity.DateOnlyAsDate);
            Assert.Null(entity.DateTimeAsDate);
            //Assert.Null(entity.DateTimeOffsetAsDatetimeoffset);
            //Assert.Null(entity.DateTimeAsDatetime2);
            //Assert.Null(entity.DateTimeAsSmalldatetime);
            //Assert.Null(entity.DateTimeAsSmalldatetime);
            Assert.Null(entity.DateTimeAsDatetime);
            Assert.Null(entity.TimeOnlyAsTime);
            Assert.Null(entity.TimeSpanAsTime);
            Assert.Null(entity.StringAsVarcharMax);
            Assert.Null(entity.StringAsCharVaryingMax);
            Assert.Null(entity.StringAsCharacterVaryingMax);
            Assert.Null(entity.StringAsNvarcharMax);
            Assert.Null(entity.StringAsNationalCharVaryingMax);
            Assert.Null(entity.StringAsNationalCharacterVaryingMax);
            Assert.Null(entity.StringAsText);
            Assert.Null(entity.StringAsNtext);
            Assert.Null(entity.BytesAsVarbinaryMax);
            Assert.Null(entity.BytesAsBinaryVaryingMax);
            Assert.Null(entity.BytesAsImage);
            Assert.Null(entity.Decimal);
            Assert.Null(entity.DecimalAsDec);
            Assert.Null(entity.DecimalAsNumeric);
            Assert.Null(entity.GuidAsUniqueidentifier);
            Assert.Null(entity.UintAsBigint);
            Assert.Null(entity.UlongAsDecimal200);
            Assert.Null(entity.UShortAsInt);
            Assert.Null(entity.SByteAsSmallint);
            Assert.Null(entity.CharAsVarchar);
            Assert.Null(entity.CharAsAsCharVarying);
            Assert.Null(entity.CharAsCharacterVaryingMax);
            Assert.Null(entity.CharAsNvarchar);
            Assert.Null(entity.CharAsNationalCharVarying);
            Assert.Null(entity.CharAsNationalCharacterVaryingMax);
            Assert.Null(entity.CharAsText);
            Assert.Null(entity.CharAsNtext);
            Assert.Null(entity.CharAsInt);
            Assert.Null(entity.EnumAsNvarchar20);
            Assert.Null(entity.EnumAsVarcharMax);
        }

        [ConditionalFact]
        public virtual void Can_insert_and_read_back_all_mapped_sized_data_types()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedSizedDataTypes>().Add(CreateMappedSizedDataTypes(77));

                Assert.Equal(1, context.SaveChanges());
            }

            var parameters = DumpParameters();
            Assert.Equal(
               $@"@p0='77'
@p1='0x0A0B0C' (Size = 3)
@p2='0x0C0D0E' (Size = 3)
@p3='0x0B0C0D' (Size = 3)
@p4='B' (Size = 3)
@p5='C' (Size = 3)
@p6='E' (Size = 3)
@p7='F' (Size = 3)
@p8='D' (Size = 3)
@p9='A' (Size = 3)
@p10='Wor' (Size = 3)
@p11=NULL (Size = 3)
@p12='Thr' (Size = 3)
@p13=NULL (Size = 3)
@p14='Lon' (Size = 3)
@p15=NULL (Size = 3)
@p16='Let' (Size = 3)
@p17=NULL (Size = 3)
@p18='The' (Size = 3)
@p19='Squ' (Size = 3)
@p20='Col' (Size = 3)
@p21='Won' (Size = 3)
@p22='Int' (Size = 3)
@p23='Tha' (Size = 3)
@p24=NULL (Size = 3)",
                parameters,
                ignoreLineEndingDifferences: true);

            using (var context = CreateContext())
            {
                AssertMappedSizedDataTypes(context.Set<MappedSizedDataTypes>().Single(e => e.Id == 77), 77);
            }
        }

        private static void AssertMappedSizedDataTypes(MappedSizedDataTypes entity, int id)
        {
            Assert.Equal(id, entity.Id);
            Assert.Equal("Wor", entity.StringAsChar3);
            Assert.Equal("Lon", entity.StringAsCharacter3);
            Assert.Equal("Tha", entity.StringAsVarchar3);
            Assert.Equal("Thr", entity.StringAsCharVarying3);
            Assert.Equal("Let", entity.StringAsCharacterVarying3);
            Assert.Equal("Won", entity.StringAsNchar3);
            Assert.Equal("Squ", entity.StringAsNationalCharacter3);
            Assert.Equal("Int", entity.StringAsNvarchar3);
            Assert.Equal("The", entity.StringAsNationalCharVarying3);
            Assert.Equal("Col", entity.StringAsNationalCharacterVarying3);
            Assert.Equal(new byte[] { 10, 11, 12 }, entity.BytesAsBinary3);
            Assert.Equal(new byte[] { 11, 12, 13 }, entity.BytesAsVarbinary3);
            Assert.Equal(new byte[] { 12, 13, 14 }, entity.BytesAsBinaryVarying3);
            Assert.Equal('A', entity.CharAsVarchar3);
            Assert.Equal('B', entity.CharAsAsCharVarying3);
            Assert.Equal('C', entity.CharAsCharacterVarying3);
            Assert.Equal('D', entity.CharAsNvarchar3);
            Assert.Equal('E', entity.CharAsNationalCharVarying3);
            Assert.Equal('F', entity.CharAsNationalCharacterVarying3);
        }

        private static MappedSizedDataTypes CreateMappedSizedDataTypes(int id)
            => new MappedSizedDataTypes
            {
                Id = id,
                StringAsChar3 = "Wor",
                StringAsCharacter3 = "Lon",
                StringAsVarchar3 = "Tha",
                StringAsCharVarying3 = "Thr",
                StringAsCharacterVarying3 = "Let",
                StringAsNchar3 = "Won",
                StringAsNationalCharacter3 = "Squ",
                StringAsNvarchar3 = "Int",
                StringAsNationalCharVarying3 = "The",
                StringAsNationalCharacterVarying3 = "Col",
                BytesAsBinary3 = new byte[] { 10, 11, 12 },
                BytesAsVarbinary3 = new byte[] { 11, 12, 13 },
                BytesAsBinaryVarying3 = new byte[] { 12, 13, 14 },
                CharAsVarchar3 = 'A',
                CharAsAsCharVarying3 = 'B',
                CharAsCharacterVarying3 = 'C',
                CharAsNvarchar3 = 'D',
                CharAsNationalCharVarying3 = 'E',
                CharAsNationalCharacterVarying3 = 'F'
            };

        [ConditionalFact]
        public virtual void Can_insert_and_read_back_nulls_for_all_mapped_sized_data_types()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedSizedDataTypes>().Add(new MappedSizedDataTypes { Id = 78 });

                Assert.Equal(1, context.SaveChanges());
            }

            var parameters = DumpParameters();
            Assert.Equal(
               $@"@p0='78'
@p1=NULL (Size = 3) (DbType = Binary)
@p2=NULL (Size = 3) (DbType = Binary)
@p3=NULL (Size = 3) (DbType = Binary)
@p4=NULL (Size = 3)
@p5=NULL (Size = 3)
@p6=NULL (Size = 3)
@p7=NULL (Size = 3)
@p8=NULL (Size = 3)
@p9=NULL (Size = 3)
@p10=NULL (Size = 3)
@p11=NULL (Size = 3)
@p12=NULL (Size = 3)
@p13=NULL (Size = 3)
@p14=NULL (Size = 3)
@p15=NULL (Size = 3)
@p16=NULL (Size = 3)
@p17=NULL (Size = 3)
@p18=NULL (Size = 3)
@p19=NULL (Size = 3)
@p20=NULL (Size = 3)
@p21=NULL (Size = 3)
@p22=NULL (Size = 3)
@p23=NULL (Size = 3)
@p24=NULL (Size = 3)",
                parameters,
                ignoreLineEndingDifferences: true);

            using (var context = CreateContext())
            {
                AssertNullMappedSizedDataTypes(context.Set<MappedSizedDataTypes>().Single(e => e.Id == 78), 78);
            }
        }

        private static void AssertNullMappedSizedDataTypes(MappedSizedDataTypes entity, int id)
        {
            Assert.Equal(id, entity.Id);
            Assert.Null(entity.StringAsChar3);
            Assert.Null(entity.StringAsCharacter3);
            Assert.Null(entity.StringAsVarchar3);
            Assert.Null(entity.StringAsCharVarying3);
            Assert.Null(entity.StringAsCharacterVarying3);
            Assert.Null(entity.StringAsNchar3);
            Assert.Null(entity.StringAsNationalCharacter3);
            Assert.Null(entity.StringAsNvarchar3);
            Assert.Null(entity.StringAsNationalCharVarying3);
            Assert.Null(entity.StringAsNationalCharacterVarying3);
            Assert.Null(entity.BytesAsBinary3);
            Assert.Null(entity.BytesAsVarbinary3);
            Assert.Null(entity.BytesAsBinaryVarying3);
            Assert.Null(entity.CharAsVarchar3);
            Assert.Null(entity.CharAsAsCharVarying3);
            Assert.Null(entity.CharAsCharacterVarying3);
            Assert.Null(entity.CharAsNvarchar3);
            Assert.Null(entity.CharAsNationalCharVarying3);
            Assert.Null(entity.CharAsNationalCharacterVarying3);
        }

        [ConditionalFact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_with_scale()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedScaledDataTypes>().Add(CreateMappedScaledDataTypes(77));

                Assert.Equal(1, context.SaveChanges());
            }

            var parameters = DumpParameters();
            Assert.Equal(
               $@"@p0='77'
@p1='102' (Precision = 3)
@p2='101' (Precision = 3)
@p3='103' (Precision = 3)
@p4='85.55'
@p5='85.5'
@p6='83.33000183105469'
@p7='83.30000305175781'
@p8='12:34:56'
@p9='12:34:56'", parameters,
                ignoreLineEndingDifferences: true);

            using (var context = CreateContext())
            {
                AssertMappedScaledDataTypes(context.Set<MappedScaledDataTypes>().Single(e => e.Id == 77), 77);
            }
        }

        private static void AssertMappedScaledDataTypes(MappedScaledDataTypes entity, int id)
        {
            Assert.Equal(id, entity.Id);
            Assert.Equal(83.3f, entity.FloatAsFloat3);
            Assert.Equal(85.5f, entity.FloatAsDoublePrecision3);
            Assert.Equal(83.33f, entity.FloatAsFloat25);
            Assert.Equal(85.55f, entity.FloatAsDoublePrecision25);
            /*Assert.Equal(
                new DateTimeOffset(new DateTime(2016, 1, 2, 11, 11, 12, 765), TimeSpan.Zero), entity.DateTimeOffsetAsDatetimeoffset3);
            Assert.Equal(new DateTime(2017, 1, 2, 12, 11, 12, 321), entity.DateTimeAsDatetime23);*/
            Assert.Equal(TimeOnly.Parse("12:34:56", CultureInfo.InvariantCulture), entity.TimeOnlyAsTime3);
            Assert.Equal(TimeSpan.Parse("12:34:56", CultureInfo.InvariantCulture), entity.TimeSpanAsTime3);
            Assert.Equal(101m, entity.DecimalAsDecimal3);
            Assert.Equal(102m, entity.DecimalAsDec3);
            Assert.Equal(103m, entity.DecimalAsNumeric3);
        }

        private static MappedScaledDataTypes CreateMappedScaledDataTypes(int id)
            => new MappedScaledDataTypes
            {
                Id = id,
                FloatAsFloat3 = 83.3f,
                FloatAsDoublePrecision3 = 85.5f,
                FloatAsFloat25 = 83.33f,
                FloatAsDoublePrecision25 = 85.55f,
                //DateTimeOffsetAsDatetimeoffset3 = new DateTimeOffset(new DateTime(2016, 1, 2, 11, 11, 12, 765), TimeSpan.Zero),
                //DateTimeAsDatetime23 = new DateTime(2017, 1, 2, 12, 11, 12, 321),
                DecimalAsDecimal3 = 101m,
                DecimalAsDec3 = 102m,
                DecimalAsNumeric3 = 103m,
                TimeOnlyAsTime3 = TimeOnly.Parse("12:34:56", CultureInfo.InvariantCulture),
                TimeSpanAsTime3 = TimeSpan.Parse("12:34:56", CultureInfo.InvariantCulture)
            };

        [ConditionalFact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_with_precision_and_scale()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedPrecisionAndScaledDataTypes>().Add(CreateMappedPrecisionAndScaledDataTypes(77));

                Assert.Equal(1, context.SaveChanges());
            }

            var parameters = DumpParameters();
            Assert.Equal(
               $@"@p0='77'
@p1='102.2' (Precision = 5) (Scale = 2)
@p2='101.1' (Precision = 5) (Scale = 2)
@p3='103.3' (Precision = 5) (Scale = 2)",
parameters,
                ignoreLineEndingDifferences: true);

            using (var context = CreateContext())
            {
                AssertMappedPrecisionAndScaledDataTypes(context.Set<MappedPrecisionAndScaledDataTypes>().Single(e => e.Id == 77), 77);
            }
        }

        private static void AssertMappedPrecisionAndScaledDataTypes(MappedPrecisionAndScaledDataTypes entity, int id)
        {
            Assert.Equal(id, entity.Id);
            Assert.Equal(101.1m, entity.DecimalAsDecimal52);
            Assert.Equal(102.2m, entity.DecimalAsDec52);
            Assert.Equal(103.3m, entity.DecimalAsNumeric52);
        }

        private static MappedPrecisionAndScaledDataTypes CreateMappedPrecisionAndScaledDataTypes(int id)
            => new MappedPrecisionAndScaledDataTypes
            {
                Id = id,
                DecimalAsDecimal52 = 101.1m,
                DecimalAsDec52 = 102.2m,
                DecimalAsNumeric52 = 103.3m
            };

        [ConditionalFact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_with_identity()
        {
            bool isoledb = false;
            using (var context = CreateContext())
            {
                context.Set<MappedDataTypesWithIdentity>().Add(CreateMappedDataTypesWithIdentity(77));
                isoledb = ((EntityFrameworkCore.Jet.Data.JetConnection)context.Database.GetDbConnection())
                    .DataAccessProviderFactory is OleDbFactory;
                Assert.Equal(1, context.SaveChanges());
            }

            var parameters = DumpParameters();
            Assert.Equal(
               $@"@p0='True'
@p1='80' (Size = 1)
@p2='0x5D5E5F60' (Nullable = false) (Size = 255)
@p3='0x61626364' (Nullable = false) (Size = 510)
@p4='0x595A5B5C' (Nullable = false) (Size = 255)
@p5='B' (Nullable = false) (Size = 1)
@p6='C' (Nullable = false) (Size = 255)
@p7='73'
@p8='E' (Nullable = false) (Size = 1)
@p9='F' (Nullable = false) (Size = 255)
@p10='H' (Nullable = false) (Size = 1)
@p11='D' (Nullable = false) (Size = 1)
@p12='G' (Nullable = false) (Size = 1)
@p13='A' (Nullable = false) (Size = 1)
@p14='2015-01-02T00:00:00.0000000' (DbType = Date)
@p15='2015-01-02T00:00:00.0000000' (DbType = DateTime)
@p16='2019-01-02T14:11:12.0000000' (DbType = DateTime)
@p17='101' (Precision = 18)
@p18='102' (Precision = 18)
@p19='81.1' (Precision = 3) (Scale = 1){(isoledb ? " (DbType = Currency)" : "")}
@p20='103' (Precision = 18)
@p21='85.5'
@p22='83.3'
@p23='4'
@p24='Value2' (Nullable = false) (Size = 255)
@p25='84.4'
@p26='a8f9f951-145f-4545-ac60-b92ff57ada47'
@p27='77'
@p28='78' (DbType = Decimal)
@p29='-128'
@p30='128' (Size = 1)
@p31='79'
@p32='Your' (Nullable = false) (Size = 255)
@p33='And now' (Nullable = false) (Size = 255)
@p34='strong' (Nullable = false) (Size = 255)
@p35='this...' (Nullable = false) (Size = 255)
@p36='help' (Nullable = false) (Size = 255)
@p37='anyone!' (Nullable = false) (Size = 255)
@p38='Gumball Rules OK!' (Nullable = false) (Size = 255)
@p39='don't' (Nullable = false) (Size = 255)
@p40='Gumball Rules!' (Nullable = false) (Size = 255)
@p41='C' (Nullable = false) (Size = 255)
@p42='short' (Nullable = false) (Size = 255)
@p43='11:15:12'
@p44='11:15:12'
@p45='65535'
@p46='-1'
@p47='4294967295' (DbType = Decimal)
@p48='-1'
@p49='18446744073709551615' (Precision = 20)
@p50='18446744073709551615' (Precision = 20)",
parameters,
                ignoreLineEndingDifferences: true);

            using (var context = CreateContext())
            {
                AssertMappedDataTypesWithIdentity(context.Set<MappedDataTypesWithIdentity>().Single(e => e.Int == 77), 77);
            }
        }

        private static void AssertMappedDataTypesWithIdentity(MappedDataTypesWithIdentity entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Equal(78, entity.LongAsBigint);
            Assert.Equal(79, entity.ShortAsSmallint);
            Assert.Equal(80, entity.ByteAsTinyint);
            Assert.Equal(uint.MaxValue, entity.UintAsInt);
            Assert.Equal(ulong.MaxValue, entity.UlongAsBigint);
            Assert.Equal(ushort.MaxValue, entity.UShortAsSmallint);
            Assert.Equal(sbyte.MinValue, entity.SbyteAsTinyint);
            Assert.True(entity.BoolAsBit);
            Assert.Equal(81.1m, entity.DecimalAsMoney);
            //Assert.Equal(82.2m, entity.DecimalAsSmallmoney);
            Assert.Equal(83.3, entity.DoubleAsFloat);
            Assert.Equal(84.4f, entity.FloatAsReal);
            Assert.Equal(85.5, entity.DoubleAsDoublePrecision);
            Assert.Equal(new DateOnly(2015, 1, 2), entity.DateOnlyAsDate);
            Assert.Equal(new DateTime(2015, 1, 2), entity.DateTimeAsDate);
            /*Assert.Equal(
                new DateTimeOffset(new DateTime(2016, 1, 2, 11, 11, 12).AddTicks(7654321), TimeSpan.Zero),
                entity.DateTimeOffsetAsDatetimeoffset);
            Assert.Equal(new DateTime(2017, 1, 2, 12, 11, 12).AddTicks(7654321), entity.DateTimeAsDatetime2);
            Assert.Equal(new DateTime(2018, 1, 2, 13, 11, 00), entity.DateTimeAsSmalldatetime);*/
            Assert.Equal(new DateTime(2019, 1, 2, 14, 11, 12), entity.DateTimeAsDatetime);
            Assert.Equal(new TimeOnly(11, 15, 12), entity.TimeOnlyAsTime);
            Assert.Equal(new TimeSpan(11, 15, 12), entity.TimeSpanAsTime);
            Assert.Equal("C", entity.StringAsVarcharMax);
            Assert.Equal("Your", entity.StringAsCharVaryingMax);
            Assert.Equal("strong", entity.StringAsCharacterVaryingMax);
            Assert.Equal("don't", entity.StringAsNvarcharMax);
            Assert.Equal("help", entity.StringAsNationalCharVaryingMax);
            Assert.Equal("anyone!", entity.StringAsNationalCharacterVaryingMax);
            Assert.Equal("short", entity.StringAsVarcharMaxUtf8);
            Assert.Equal("And now", entity.StringAsCharVaryingMaxUtf8);
            Assert.Equal("this...", entity.StringAsCharacterVaryingMaxUtf8);
            Assert.Equal("Gumball Rules!", entity.StringAsText);
            Assert.Equal("Gumball Rules OK!", entity.StringAsNtext);
            Assert.Equal(new byte[] { 89, 90, 91, 92 }, entity.BytesAsVarbinaryMax);
            Assert.Equal(new byte[] { 93, 94, 95, 96 }, entity.BytesAsBinaryVaryingMax);
            Assert.Equal(new byte[] { 97, 98, 99, 100 }, entity.BytesAsImage);
            Assert.Equal(101m, entity.Decimal);
            Assert.Equal(102m, entity.DecimalAsDec);
            Assert.Equal(103m, entity.DecimalAsNumeric);
            Assert.Equal(new Guid("A8F9F951-145F-4545-AC60-B92FF57ADA47"), entity.GuidAsUniqueidentifier);
            Assert.Equal(uint.MaxValue, entity.UintAsBigint);
            Assert.Equal(ulong.MaxValue, entity.UlongAsDecimal200);
            Assert.Equal(ushort.MaxValue, entity.UShortAsInt);
            Assert.Equal(sbyte.MinValue, entity.SByteAsSmallint);
            Assert.Equal('A', entity.CharAsVarchar);
            Assert.Equal('B', entity.CharAsAsCharVarying);
            Assert.Equal('C', entity.CharAsCharacterVaryingMax);
            Assert.Equal('D', entity.CharAsNvarchar);
            Assert.Equal('E', entity.CharAsNationalCharVarying);
            Assert.Equal('F', entity.CharAsNationalCharacterVaryingMax);
            Assert.Equal('G', entity.CharAsText);
            Assert.Equal('H', entity.CharAsNtext);
            Assert.Equal('I', entity.CharAsInt);
            Assert.Equal(StringEnum16.Value2, entity.EnumAsVarcharMax);
            Assert.Equal(StringEnumU16.Value4, entity.EnumAsNvarchar20);
        }

        private static MappedDataTypesWithIdentity CreateMappedDataTypesWithIdentity(int id)
            => new MappedDataTypesWithIdentity
            {
                Int = id,
                LongAsBigint = 78L,
                ShortAsSmallint = 79,
                ByteAsTinyint = 80,
                UintAsInt = uint.MaxValue,
                UlongAsBigint = ulong.MaxValue,
                UShortAsSmallint = ushort.MaxValue,
                SbyteAsTinyint = sbyte.MinValue,
                BoolAsBit = true,
                DecimalAsMoney = 81.1m,
                //DecimalAsSmallmoney = 82.2m,
                DoubleAsFloat = 83.3,
                FloatAsReal = 84.4f,
                DoubleAsDoublePrecision = 85.5,
                DateOnlyAsDate = new DateOnly(2015, 1, 2),
                DateTimeAsDate = new DateTime(2015, 1, 2, 10, 11, 12),
                //DateTimeOffsetAsDatetimeoffset = new DateTimeOffset(new DateTime(2016, 1, 2, 11, 11, 12).AddTicks(7654321), TimeSpan.Zero),
                //DateTimeAsDatetime2 = new DateTime(2017, 1, 2, 12, 11, 12).AddTicks(7654321),
                //DateTimeAsSmalldatetime = new DateTime(2018, 1, 2, 13, 11, 12),
                DateTimeAsDatetime = new DateTime(2019, 1, 2, 14, 11, 12),
                TimeOnlyAsTime = new TimeOnly(11, 15, 12),
                TimeSpanAsTime = new TimeSpan(11, 15, 12),
                StringAsVarcharMax = "C",
                StringAsCharVaryingMax = "Your",
                StringAsCharacterVaryingMax = "strong",
                StringAsNvarcharMax = "don't",
                StringAsNationalCharVaryingMax = "help",
                StringAsNationalCharacterVaryingMax = "anyone!",
                StringAsVarcharMaxUtf8 = "short",
                StringAsCharVaryingMaxUtf8 = "And now",
                StringAsCharacterVaryingMaxUtf8 = "this...",
                StringAsText = "Gumball Rules!",
                StringAsNtext = "Gumball Rules OK!",
                BytesAsVarbinaryMax = new byte[] { 89, 90, 91, 92 },
                BytesAsBinaryVaryingMax = new byte[] { 93, 94, 95, 96 },
                BytesAsImage = new byte[] { 97, 98, 99, 100 },
                Decimal = 101m,
                DecimalAsDec = 102m,
                DecimalAsNumeric = 103m,
                GuidAsUniqueidentifier = new Guid("A8F9F951-145F-4545-AC60-B92FF57ADA47"),
                UintAsBigint = uint.MaxValue,
                UlongAsDecimal200 = ulong.MaxValue,
                UShortAsInt = ushort.MaxValue,
                SByteAsSmallint = sbyte.MinValue,
                CharAsVarchar = 'A',
                CharAsAsCharVarying = 'B',
                CharAsCharacterVaryingMax = 'C',
                CharAsNvarchar = 'D',
                CharAsNationalCharVarying = 'E',
                CharAsNationalCharacterVaryingMax = 'F',
                CharAsText = 'G',
                CharAsNtext = 'H',
                CharAsInt = 'I',
                EnumAsNvarchar20 = StringEnumU16.Value4,
                EnumAsVarcharMax = StringEnum16.Value2
            };

        [ConditionalFact]
        public virtual void Can_insert_and_read_back_all_mapped_nullable_data_types_with_identity()
        {
            bool isoledb = false;
            using (var context = CreateContext())
            {
                context.Set<MappedNullableDataTypesWithIdentity>().Add(CreateMappedNullableDataTypesWithIdentity(77));
                isoledb = ((EntityFrameworkCore.Jet.Data.JetConnection)context.Database.GetDbConnection())
                    .DataAccessProviderFactory is OleDbFactory;
                Assert.Equal(1, context.SaveChanges());
            }

            var parameters = DumpParameters();
            Assert.Equal(
               $@"@p0='True' (Nullable = true)
@p1='80' (Nullable = true) (Size = 1)
@p2='0x61626364' (Size = 510)
@p3='0x595A5B5C' (Size = 510)
@p4='0x5D5E5F60' (Size = 510)
@p5='B' (Size = 1)
@p6='C' (Size = 255)
@p7='73' (Nullable = true)
@p8='E' (Size = 1)
@p9='F' (Size = 255)
@p10='H' (Size = 1)
@p11='D' (Size = 1)
@p12='G' (Size = 1)
@p13='A' (Size = 1)
@p14='2015-01-02T00:00:00.0000000' (Nullable = true) (DbType = Date)
@p15='2015-01-02T00:00:00.0000000' (Nullable = true) (DbType = DateTime)
@p16='2019-01-02T14:11:12.0000000' (Nullable = true) (DbType = DateTime)
@p17='101' (Nullable = true) (Precision = 18)
@p18='102' (Nullable = true) (Precision = 18)
@p19='81.1' (Nullable = true) (Precision = 3) (Scale = 1){(isoledb ? " (DbType = Currency)" : "")}
@p20='103' (Nullable = true) (Precision = 18)
@p21='85.5' (Nullable = true)
@p22='83.3' (Nullable = true)
@p23='4' (Nullable = true)
@p24='Value2' (Size = 255)
@p25='84.4' (Nullable = true)
@p26='a8f9f951-145f-4545-ac60-b92ff57ada47' (Nullable = true)
@p27='77' (Nullable = true)
@p28='78' (Nullable = true) (DbType = Decimal)
@p29='-128' (Nullable = true)
@p30='128' (Nullable = true) (Size = 1)
@p31='79' (Nullable = true)
@p32='Your' (Size = 255)
@p33='And now' (Size = 255)
@p34='strong' (Size = 255)
@p35='this...' (Size = 255)
@p36='help' (Size = 255)
@p37='anyone!' (Size = 255)
@p38='Gumball Rules OK!' (Size = 255)
@p39='don't' (Size = 255)
@p40='Gumball Rules!' (Size = 255)
@p41='C' (Size = 255)
@p42='short' (Size = 255)
@p43='11:15:12' (Nullable = true)
@p44='11:15:12' (Nullable = true)
@p45='65535' (Nullable = true)
@p46='4294967295' (Nullable = true) (DbType = Decimal)
@p47='-1' (Nullable = true)
@p48='18446744073709551615' (Nullable = true) (Precision = 20)
@p49='18446744073709551615' (Nullable = true) (Precision = 20)
@p50='-1' (Nullable = true)",
                parameters,
                ignoreLineEndingDifferences: true);

            using (var context = CreateContext())
            {
                AssertMappedNullableDataTypesWithIdentity(context.Set<MappedNullableDataTypesWithIdentity>().Single(e => e.Int == 77), 77);
            }
        }

        private static void AssertMappedNullableDataTypesWithIdentity(MappedNullableDataTypesWithIdentity entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Equal(78, entity.LongAsBigint);
            Assert.Equal(79, entity.ShortAsSmallint.Value);
            Assert.Equal(80, entity.ByteAsTinyint.Value);
            Assert.Equal(uint.MaxValue, entity.UintAsInt);
            Assert.Equal(ulong.MaxValue, entity.UlongAsBigint);
            Assert.Equal(ushort.MaxValue, entity.UshortAsSmallint);
            Assert.Equal(sbyte.MinValue, entity.SbyteAsTinyint);
            Assert.True(entity.BoolAsBit);
            Assert.Equal(81.1m, entity.DecimalAsMoney);
            //Assert.Equal(82.2m, entity.DecimalAsSmallmoney);
            Assert.Equal(83.3, entity.DoubleAsFloat);
            Assert.Equal(84.4f, entity.FloatAsReal);
            Assert.Equal(85.5, entity.DoubleAsDoublePrecision);
            Assert.Equal(new DateTime(2015, 1, 2), entity.DateTimeAsDate);
            Assert.Equal(new DateOnly(2015, 1, 2), entity.DateOnlyAsDate);
            /*Assert.Equal(
                new DateTimeOffset(new DateTime(2016, 1, 2, 11, 11, 12).AddTicks(2345678), TimeSpan.Zero),
                entity.DateTimeOffsetAsDatetimeoffset);
            Assert.Equal(new DateTime(2017, 1, 2, 12, 11, 12).AddTicks(2345678), entity.DateTimeAsDatetime2);
            Assert.Equal(new DateTime(2018, 1, 2, 13, 11, 00), entity.DateTimeAsSmalldatetime);*/
            Assert.Equal(new DateTime(2019, 1, 2, 14, 11, 12), entity.DateTimeAsDatetime);
            Assert.Equal(new TimeOnly(11, 15, 12), entity.TimeOnlyAsTime);
            Assert.Equal(new TimeSpan(11, 15, 12), entity.TimeSpanAsTime);
            Assert.Equal("C", entity.StringAsVarcharMax);
            Assert.Equal("Your", entity.StringAsCharVaryingMax);
            Assert.Equal("strong", entity.StringAsCharacterVaryingMax);
            Assert.Equal("don't", entity.StringAsNvarcharMax);
            Assert.Equal("help", entity.StringAsNationalCharVaryingMax);
            Assert.Equal("anyone!", entity.StringAsNationalCharacterVaryingMax);
            Assert.Equal("Gumball Rules!", entity.StringAsText);
            Assert.Equal("Gumball Rules OK!", entity.StringAsNtext);
            Assert.Equal(new byte[] { 89, 90, 91, 92 }, entity.BytesAsVarbinaryMax);
            Assert.Equal(new byte[] { 93, 94, 95, 96 }, entity.BytesAsVaryingMax);
            Assert.Equal(new byte[] { 97, 98, 99, 100 }, entity.BytesAsImage);
            Assert.Equal(101m, entity.Decimal);
            Assert.Equal(102m, entity.DecimalAsDec);
            Assert.Equal(103m, entity.DecimalAsNumeric);
            Assert.Equal(new Guid("A8F9F951-145F-4545-AC60-B92FF57ADA47"), entity.GuidAsUniqueidentifier);
            Assert.Equal(uint.MaxValue, entity.UintAsBigint);
            Assert.Equal(ulong.MaxValue, entity.UlongAsDecimal200);
            Assert.Equal(ushort.MaxValue, entity.UShortAsInt);
            Assert.Equal(sbyte.MinValue, entity.SByteAsSmallint);
            Assert.Equal('A', entity.CharAsVarchar);
            Assert.Equal('B', entity.CharAsAsCharVarying);
            Assert.Equal('C', entity.CharAsCharacterVaryingMax);
            Assert.Equal('D', entity.CharAsNvarchar);
            Assert.Equal('E', entity.CharAsNationalCharVarying);
            Assert.Equal('F', entity.CharAsNationalCharacterVaryingMax);
            Assert.Equal('G', entity.CharAsText);
            Assert.Equal('H', entity.CharAsNtext);
            Assert.Equal('I', entity.CharAsInt);
            Assert.Equal(StringEnum16.Value2, entity.EnumAsVarcharMax);
            Assert.Equal(StringEnumU16.Value4, entity.EnumAsNvarchar20);
        }

        private static MappedNullableDataTypesWithIdentity CreateMappedNullableDataTypesWithIdentity(int id)
            => new MappedNullableDataTypesWithIdentity
            {
                Int = id,
                LongAsBigint = 78L,
                ShortAsSmallint = 79,
                ByteAsTinyint = 80,
                UintAsInt = uint.MaxValue,
                UlongAsBigint = ulong.MaxValue,
                UshortAsSmallint = ushort.MaxValue,
                SbyteAsTinyint = sbyte.MinValue,
                BoolAsBit = true,
                DecimalAsMoney = 81.1m,
                //DecimalAsSmallmoney = 82.2m,
                DoubleAsFloat = 83.3,
                FloatAsReal = 84.4f,
                DoubleAsDoublePrecision = 85.5,
                DateOnlyAsDate = new DateOnly(2015, 1, 2),
                DateTimeAsDate = new DateTime(2015, 1, 2, 10, 11, 12),
                //DateTimeOffsetAsDatetimeoffset = new DateTimeOffset(new DateTime(2016, 1, 2, 11, 11, 12).AddTicks(2345678), TimeSpan.Zero),
                //DateTimeAsDatetime2 = new DateTime(2017, 1, 2, 12, 11, 12).AddTicks(2345678),
                //DateTimeAsSmalldatetime = new DateTime(2018, 1, 2, 13, 11, 12),
                DateTimeAsDatetime = new DateTime(2019, 1, 2, 14, 11, 12),
                TimeOnlyAsTime = new TimeOnly(11, 15, 12),
                TimeSpanAsTime = new TimeSpan(11, 15, 12),
                StringAsVarcharMax = "C",
                StringAsCharVaryingMax = "Your",
                StringAsCharacterVaryingMax = "strong",
                StringAsNvarcharMax = "don't",
                StringAsNationalCharVaryingMax = "help",
                StringAsNationalCharacterVaryingMax = "anyone!",
                StringAsVarcharMaxUtf8 = "short",
                StringAsCharVaryingMaxUtf8 = "And now",
                StringAsCharacterVaryingMaxUtf8 = "this...",
                StringAsText = "Gumball Rules!",
                StringAsNtext = "Gumball Rules OK!",
                BytesAsVarbinaryMax = new byte[] { 89, 90, 91, 92 },
                BytesAsVaryingMax = new byte[] { 93, 94, 95, 96 },
                BytesAsImage = new byte[] { 97, 98, 99, 100 },
                Decimal = 101m,
                DecimalAsDec = 102m,
                DecimalAsNumeric = 103m,
                GuidAsUniqueidentifier = new Guid("A8F9F951-145F-4545-AC60-B92FF57ADA47"),
                UintAsBigint = uint.MaxValue,
                UlongAsDecimal200 = ulong.MaxValue,
                UShortAsInt = ushort.MaxValue,
                SByteAsSmallint = sbyte.MinValue,
                CharAsVarchar = 'A',
                CharAsAsCharVarying = 'B',
                CharAsCharacterVaryingMax = 'C',
                CharAsNvarchar = 'D',
                CharAsNationalCharVarying = 'E',
                CharAsNationalCharacterVaryingMax = 'F',
                CharAsText = 'G',
                CharAsNtext = 'H',
                CharAsInt = 'I',
                EnumAsNvarchar20 = StringEnumU16.Value4,
                EnumAsVarcharMax = StringEnum16.Value2
            };

        [ConditionalFact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_set_to_null_with_identity()
        {
            bool isoledb = false;
            using (var context = CreateContext())
            {
                context.Set<MappedNullableDataTypesWithIdentity>().Add(new MappedNullableDataTypesWithIdentity { Int = 78 });
                isoledb = ((EntityFrameworkCore.Jet.Data.JetConnection)context.Database.GetDbConnection())
                    .DataAccessProviderFactory is OleDbFactory;
                Assert.Equal(1, context.SaveChanges());
            }

            var parameters = DumpParameters();
            Assert.Equal(
               $@"@p0=NULL (DbType = Boolean)
@p1=NULL (DbType = Byte)
@p2=NULL (Size = 510) (DbType = Binary)
@p3=NULL (Size = 510) (DbType = Binary)
@p4=NULL (Size = 510) (DbType = Binary)
@p5=NULL (Size = 1)
@p6=NULL (Size = 255)
@p7=NULL (DbType = Int32)
@p8=NULL (Size = 1)
@p9=NULL (Size = 255)
@p10=NULL (Size = 1)
@p11=NULL (Size = 1)
@p12=NULL (Size = 1)
@p13=NULL (Size = 1)
@p14=NULL (DbType = Date)
@p15=NULL (DbType = DateTime)
@p16=NULL (DbType = DateTime)
@p17=NULL (Precision = 18) (DbType = Decimal)
@p18=NULL (Precision = 18) (DbType = Decimal)
@p19=NULL {(isoledb ? "(DbType = Currency)" : "(DbType = Decimal)")}
@p20=NULL (Precision = 18) (DbType = Decimal)
@p21=NULL (DbType = Double)
@p22=NULL (DbType = Double)
@p23=NULL (DbType = Int32)
@p24=NULL (Size = 255)
@p25=NULL (DbType = Single)
@p26=NULL (DbType = Guid)
@p27='78' (Nullable = true)
@p28=NULL (DbType = Decimal)
@p29=NULL (DbType = Int16)
@p30=NULL (DbType = Byte)
@p31=NULL (DbType = Int16)
@p32=NULL (Size = 255)
@p33=NULL (Size = 255)
@p34=NULL (Size = 255)
@p35=NULL (Size = 255)
@p36=NULL (Size = 255)
@p37=NULL (Size = 255)
@p38=NULL (Size = 255)
@p39=NULL (Size = 255)
@p40=NULL (Size = 255)
@p41=NULL (Size = 255)
@p42=NULL (Size = 255)
@p43=NULL (DbType = Time)
@p44=NULL (DbType = Time)
@p45=NULL (DbType = Int32)
@p46=NULL (DbType = Decimal)
@p47=NULL (DbType = Int32)
@p48=NULL (Precision = 20) (DbType = Decimal)
@p49=NULL (Precision = 20) (DbType = Decimal)
@p50=NULL (DbType = Int16)",
                parameters,
                ignoreLineEndingDifferences: true);

            using (var context = CreateContext())
            {
                AssertNullMappedNullableDataTypesWithIdentity(
                    context.Set<MappedNullableDataTypesWithIdentity>().Single(e => e.Int == 78), 78);
            }
        }

        private static void AssertNullMappedNullableDataTypesWithIdentity(
            MappedNullableDataTypesWithIdentity entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Null(entity.LongAsBigint);
            Assert.Null(entity.ShortAsSmallint);
            Assert.Null(entity.ByteAsTinyint);
            Assert.Null(entity.UintAsInt);
            Assert.Null(entity.UlongAsBigint);
            Assert.Null(entity.UshortAsSmallint);
            Assert.Null(entity.SbyteAsTinyint);
            Assert.Equal(entity.BoolAsBit, false);//Bit in Jet is non nullable so can not be null
            Assert.Null(entity.DecimalAsMoney);
            //Assert.Null(entity.DecimalAsSmallmoney);
            Assert.Null(entity.DoubleAsFloat);
            Assert.Null(entity.FloatAsReal);
            Assert.Null(entity.DoubleAsDoublePrecision);
            Assert.Null(entity.DateOnlyAsDate);
            Assert.Null(entity.DateTimeAsDate);
            //Assert.Null(entity.DateTimeOffsetAsDatetimeoffset);
            //Assert.Null(entity.DateTimeAsDatetime2);
            //Assert.Null(entity.DateTimeAsSmalldatetime);
            Assert.Null(entity.DateTimeAsDatetime);
            Assert.Null(entity.TimeOnlyAsTime);
            Assert.Null(entity.TimeSpanAsTime);
            Assert.Null(entity.StringAsVarcharMax);
            Assert.Null(entity.StringAsCharVaryingMax);
            Assert.Null(entity.StringAsCharacterVaryingMax);
            Assert.Null(entity.StringAsNvarcharMax);
            Assert.Null(entity.StringAsNationalCharVaryingMax);
            Assert.Null(entity.StringAsNationalCharacterVaryingMax);
            Assert.Null(entity.StringAsText);
            Assert.Null(entity.StringAsNtext);
            Assert.Null(entity.BytesAsVarbinaryMax);
            Assert.Null(entity.BytesAsVaryingMax);
            Assert.Null(entity.BytesAsImage);
            Assert.Null(entity.Decimal);
            Assert.Null(entity.DecimalAsDec);
            Assert.Null(entity.DecimalAsNumeric);
            Assert.Null(entity.GuidAsUniqueidentifier);
            Assert.Null(entity.UintAsBigint);
            Assert.Null(entity.UlongAsDecimal200);
            Assert.Null(entity.UShortAsInt);
            Assert.Null(entity.SByteAsSmallint);
            Assert.Null(entity.CharAsVarchar);
            Assert.Null(entity.CharAsAsCharVarying);
            Assert.Null(entity.CharAsCharacterVaryingMax);
            Assert.Null(entity.CharAsNvarchar);
            Assert.Null(entity.CharAsNationalCharVarying);
            Assert.Null(entity.CharAsNationalCharacterVaryingMax);
            Assert.Null(entity.CharAsText);
            Assert.Null(entity.CharAsNtext);
            Assert.Null(entity.CharAsInt);
            Assert.Null(entity.EnumAsNvarchar20);
            Assert.Null(entity.EnumAsVarcharMax);
        }

        [ConditionalFact]
        public virtual void Can_insert_and_read_back_all_mapped_sized_data_types_with_identity()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedSizedDataTypesWithIdentity>().Add(CreateMappedSizedDataTypesWithIdentity(77));

                Assert.Equal(1, context.SaveChanges());
            }

            var parameters = DumpParameters();
            Assert.Equal(
               $@"@p0='0x0A0B0C' (Size = 3)
@p1='0x0C0D0E' (Size = 3)
@p2='0x0B0C0D' (Size = 3)
@p3='B' (Size = 3)
@p4='C' (Size = 3)
@p5='E' (Size = 3)
@p6='F' (Size = 3)
@p7='D' (Size = 3)
@p8='A' (Size = 3)
@p9='77'
@p10='Wor' (Size = 3)
@p11='Wha' (Size = 3)
@p12='Thr' (Size = 3)
@p13='tex' (Size = 3)
@p14='Lon' (Size = 3)
@p15='doe' (Size = 3)
@p16='Let' (Size = 3)
@p17='men' (Size = 3)
@p18='The' (Size = 3)
@p19='Squ' (Size = 3)
@p20='Col' (Size = 3)
@p21='Won' (Size = 3)
@p22='Int' (Size = 3)
@p23='Tha' (Size = 3)
@p24='the' (Size = 3)",
                parameters,
                ignoreLineEndingDifferences: true);

            using (var context = CreateContext())
            {
                AssertMappedSizedDataTypesWithIdentity(context.Set<MappedSizedDataTypesWithIdentity>().Single(e => e.Int == 77), 77);
            }
        }

        private static void AssertMappedSizedDataTypesWithIdentity(MappedSizedDataTypesWithIdentity entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Equal("Wor", entity.StringAsChar3);
            Assert.Equal("Lon", entity.StringAsCharacter3);
            Assert.Equal("Tha", entity.StringAsVarchar3);
            Assert.Equal("Thr", entity.StringAsCharVarying3);
            Assert.Equal("Let", entity.StringAsCharacterVarying3);
            Assert.Equal("Won", entity.StringAsNchar3);
            Assert.Equal("Squ", entity.StringAsNationalCharacter3);
            Assert.Equal("Int", entity.StringAsNvarchar3);
            Assert.Equal("The", entity.StringAsNationalCharVarying3);
            Assert.Equal("Col", entity.StringAsNationalCharacterVarying3);
            Assert.Equal("Wha", entity.StringAsChar3Utf8);
            Assert.Equal("doe", entity.StringAsCharacter3Utf8);
            Assert.Equal("the", entity.StringAsVarchar3Utf8);
            Assert.Equal("tex", entity.StringAsCharVarying3Utf8);
            Assert.Equal("men", entity.StringAsCharacterVarying3Utf8);
            Assert.Equal(new byte[] { 10, 11, 12 }, entity.BytesAsBinary3);
            Assert.Equal(new byte[] { 11, 12, 13 }, entity.BytesAsVarbinary3);
            Assert.Equal(new byte[] { 12, 13, 14 }, entity.BytesAsBinaryVarying3);
            Assert.Equal('A', entity.CharAsVarchar3);
            Assert.Equal('B', entity.CharAsAsCharVarying3);
            Assert.Equal('C', entity.CharAsCharacterVarying3);
            Assert.Equal('D', entity.CharAsNvarchar3);
            Assert.Equal('E', entity.CharAsNationalCharVarying3);
            Assert.Equal('F', entity.CharAsNationalCharacterVarying3);
        }

        private static MappedSizedDataTypesWithIdentity CreateMappedSizedDataTypesWithIdentity(int id)
            => new MappedSizedDataTypesWithIdentity
            {
                Int = id,
                StringAsChar3 = "Wor",
                StringAsCharacter3 = "Lon",
                StringAsVarchar3 = "Tha",
                StringAsCharVarying3 = "Thr",
                StringAsCharacterVarying3 = "Let",
                StringAsNchar3 = "Won",
                StringAsNationalCharacter3 = "Squ",
                StringAsNvarchar3 = "Int",
                StringAsNationalCharVarying3 = "The",
                StringAsNationalCharacterVarying3 = "Col",
                StringAsChar3Utf8 = "Wha",
                StringAsCharacter3Utf8 = "doe",
                StringAsVarchar3Utf8 = "the",
                StringAsCharVarying3Utf8 = "tex",
                StringAsCharacterVarying3Utf8 = "men",
                BytesAsBinary3 = new byte[] { 10, 11, 12 },
                BytesAsVarbinary3 = new byte[] { 11, 12, 13 },
                BytesAsBinaryVarying3 = new byte[] { 12, 13, 14 },
                CharAsVarchar3 = 'A',
                CharAsAsCharVarying3 = 'B',
                CharAsCharacterVarying3 = 'C',
                CharAsNvarchar3 = 'D',
                CharAsNationalCharVarying3 = 'E',
                CharAsNationalCharacterVarying3 = 'F'
            };

        [ConditionalFact]
        public virtual void Can_insert_and_read_back_nulls_for_all_mapped_sized_data_types_with_identity()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedSizedDataTypesWithIdentity>().Add(new MappedSizedDataTypesWithIdentity { Int = 78 });

                Assert.Equal(1, context.SaveChanges());
            }

            var parameters = DumpParameters();
            Assert.Equal(
               $@"@p0=NULL (Size = 3) (DbType = Binary)
@p1=NULL (Size = 3) (DbType = Binary)
@p2=NULL (Size = 3) (DbType = Binary)
@p3=NULL (Size = 3)
@p4=NULL (Size = 3)
@p5=NULL (Size = 3)
@p6=NULL (Size = 3)
@p7=NULL (Size = 3)
@p8=NULL (Size = 3)
@p9='78'
@p10=NULL (Size = 3)
@p11=NULL (Size = 3)
@p12=NULL (Size = 3)
@p13=NULL (Size = 3)
@p14=NULL (Size = 3)
@p15=NULL (Size = 3)
@p16=NULL (Size = 3)
@p17=NULL (Size = 3)
@p18=NULL (Size = 3)
@p19=NULL (Size = 3)
@p20=NULL (Size = 3)
@p21=NULL (Size = 3)
@p22=NULL (Size = 3)
@p23=NULL (Size = 3)
@p24=NULL (Size = 3)",
                parameters,
                ignoreLineEndingDifferences: true);

            using (var context = CreateContext())
            {
                AssertNullMappedSizedDataTypesWithIdentity(context.Set<MappedSizedDataTypesWithIdentity>().Single(e => e.Int == 78), 78);
            }
        }

        private static void AssertNullMappedSizedDataTypesWithIdentity(MappedSizedDataTypesWithIdentity entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Null(entity.StringAsChar3);
            Assert.Null(entity.StringAsCharacter3);
            Assert.Null(entity.StringAsVarchar3);
            Assert.Null(entity.StringAsCharVarying3);
            Assert.Null(entity.StringAsCharacterVarying3);
            Assert.Null(entity.StringAsNchar3);
            Assert.Null(entity.StringAsNationalCharacter3);
            Assert.Null(entity.StringAsNvarchar3);
            Assert.Null(entity.StringAsNationalCharVarying3);
            Assert.Null(entity.StringAsNationalCharacterVarying3);
            Assert.Null(entity.StringAsChar3Utf8);
            Assert.Null(entity.StringAsCharacter3Utf8);
            Assert.Null(entity.StringAsVarchar3Utf8);
            Assert.Null(entity.StringAsCharVarying3Utf8);
            Assert.Null(entity.StringAsCharacterVarying3Utf8);
            Assert.Null(entity.BytesAsBinary3);
            Assert.Null(entity.BytesAsVarbinary3);
            Assert.Null(entity.BytesAsBinaryVarying3);
            Assert.Null(entity.CharAsVarchar3);
            Assert.Null(entity.CharAsAsCharVarying3);
            Assert.Null(entity.CharAsCharacterVarying3);
            Assert.Null(entity.CharAsNvarchar3);
            Assert.Null(entity.CharAsNationalCharVarying3);
            Assert.Null(entity.CharAsNationalCharacterVarying3);
        }

        [ConditionalFact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_with_scale_with_identity()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedScaledDataTypesWithIdentity>().Add(CreateMappedScaledDataTypesWithIdentity(77));

                Assert.Equal(1, context.SaveChanges());
            }

            var parameters = DumpParameters();
            Assert.Equal(
               $@"@p0='102' (Precision = 3)
@p1='101' (Precision = 3)
@p2='103' (Precision = 3)
@p3='85.55'
@p4='85.5'
@p5='83.33000183105469'
@p6='83.30000305175781'
@p7='77'
@p8='12:34:56'
@p9='12:34:56'",
                parameters,
                ignoreLineEndingDifferences: true);

            using (var context = CreateContext())
            {
                AssertMappedScaledDataTypesWithIdentity(context.Set<MappedScaledDataTypesWithIdentity>().Single(e => e.Int == 77), 77);
            }
        }

        private static void AssertMappedScaledDataTypesWithIdentity(MappedScaledDataTypesWithIdentity entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Equal(83.3f, entity.FloatAsFloat3);
            Assert.Equal(85.5f, entity.FloatAsDoublePrecision3);
            Assert.Equal(83.33f, entity.FloatAsFloat25);
            Assert.Equal(85.55f, entity.FloatAsDoublePrecision25);
            /*Assert.Equal(
                new DateTimeOffset(new DateTime(2016, 1, 2, 11, 11, 12, 567), TimeSpan.Zero), entity.DateTimeOffsetAsDatetimeoffset3);
            Assert.Equal(new DateTime(2017, 1, 2, 12, 11, 12, 123), entity.DateTimeAsDatetime23);*/
            Assert.Equal(101m, entity.DecimalAsDecimal3);
            Assert.Equal(102m, entity.DecimalAsDec3);
            Assert.Equal(103m, entity.DecimalAsNumeric3);
            Assert.Equal(TimeOnly.Parse("12:34:56", CultureInfo.InvariantCulture), entity.TimeOnlyAsTime3);
            Assert.Equal(TimeSpan.Parse("12:34:56", CultureInfo.InvariantCulture), entity.TimeSpanAsTime3);
        }

        private static MappedScaledDataTypesWithIdentity CreateMappedScaledDataTypesWithIdentity(int id)
            => new MappedScaledDataTypesWithIdentity
            {
                Int = id,
                FloatAsFloat3 = 83.3f,
                FloatAsDoublePrecision3 = 85.5f,
                FloatAsFloat25 = 83.33f,
                FloatAsDoublePrecision25 = 85.55f,
                //DateTimeOffsetAsDatetimeoffset3 = new DateTimeOffset(new DateTime(2016, 1, 2, 11, 11, 12, 567), TimeSpan.Zero),
                //DateTimeAsDatetime23 = new DateTime(2017, 1, 2, 12, 11, 12, 123),
                DecimalAsDecimal3 = 101m,
                DecimalAsDec3 = 102m,
                DecimalAsNumeric3 = 103m,
                TimeOnlyAsTime3 = TimeOnly.Parse("12:34:56", CultureInfo.InvariantCulture),
                TimeSpanAsTime3 = TimeSpan.Parse("12:34:56", CultureInfo.InvariantCulture)
            };

        [ConditionalFact]
        public virtual void Can_insert_and_read_back_all_mapped_data_types_with_precision_and_scale_with_identity()
        {
            using (var context = CreateContext())
            {
                context.Set<MappedPrecisionAndScaledDataTypesWithIdentity>().Add(
                    CreateMappedPrecisionAndScaledDataTypesWithIdentity(77));

                Assert.Equal(1, context.SaveChanges());
            }

            var parameters = DumpParameters();
            Assert.Equal(
               $@"@p0='102.2' (Precision = 5) (Scale = 2)
@p1='101.1' (Precision = 5) (Scale = 2)
@p2='103.3' (Precision = 5) (Scale = 2)
@p3='77'",
                parameters,
                ignoreLineEndingDifferences: true);

            using (var context = CreateContext())
            {
                AssertMappedPrecisionAndScaledDataTypesWithIdentity(
                    context.Set<MappedPrecisionAndScaledDataTypesWithIdentity>().Single(e => e.Int == 77), 77);
            }
        }

        private static void AssertMappedPrecisionAndScaledDataTypesWithIdentity(
            MappedPrecisionAndScaledDataTypesWithIdentity entity, int id)
        {
            Assert.Equal(id, entity.Int);
            Assert.Equal(101.1m, entity.DecimalAsDecimal52);
            Assert.Equal(102.2m, entity.DecimalAsDec52);
            Assert.Equal(103.3m, entity.DecimalAsNumeric52);
        }

        private static MappedPrecisionAndScaledDataTypesWithIdentity CreateMappedPrecisionAndScaledDataTypesWithIdentity(int id)
            => new MappedPrecisionAndScaledDataTypesWithIdentity
            {
                Int = id,
                DecimalAsDecimal52 = 101.1m,
                DecimalAsDec52 = 102.2m,
                DecimalAsNumeric52 = 103.3m
            };

        [ConditionalFact]
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

        [ConditionalFact]
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

        [ConditionalFact]
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

        [ConditionalFact]
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

        [ConditionalFact]
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

        [ConditionalFact]
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

        [ConditionalFact]
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

        [ConditionalFact]
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

        [ConditionalFact]
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
                AssertMappedNullableDataTypesWithIdentity(
                    context.Set<MappedNullableDataTypesWithIdentity>().Single(e => e.Int == 177), 177);
                AssertMappedNullableDataTypesWithIdentity(
                    context.Set<MappedNullableDataTypesWithIdentity>().Single(e => e.Int == 178), 178);
                AssertMappedNullableDataTypesWithIdentity(
                    context.Set<MappedNullableDataTypesWithIdentity>().Single(e => e.Int == 179), 179);
            }
        }

        [ConditionalFact]
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
                AssertNullMappedNullableDataTypesWithIdentity(
                    context.Set<MappedNullableDataTypesWithIdentity>().Single(e => e.Int == 278), 278);
                AssertNullMappedNullableDataTypesWithIdentity(
                    context.Set<MappedNullableDataTypesWithIdentity>().Single(e => e.Int == 279), 279);
                AssertNullMappedNullableDataTypesWithIdentity(
                    context.Set<MappedNullableDataTypesWithIdentity>().Single(e => e.Int == 280), 280);
            }
        }

        [ConditionalFact]
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

        [ConditionalFact]
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

        [ConditionalFact]
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

        [ConditionalFact]
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

        [ConditionalFact]
        public virtual void Columns_have_expected_data_types()
        {
            var actual = QueryForColumnTypes(
                CreateContext(),
                nameof(ObjectBackedDataTypes), nameof(NullableBackedDataTypes), nameof(NonNullableBackedDataTypes));

            const string expected = @"#Dual.ID ---> [integer]
Animal.Id ---> [counter]
AnimalDetails.AnimalId ---> [nullable integer]
AnimalDetails.BoolField ---> [integer]
AnimalDetails.Id ---> [counter]
AnimalIdentification.AnimalId ---> [integer]
AnimalIdentification.Id ---> [counter]
AnimalIdentification.Method ---> [integer]
BinaryForeignKeyDataType.BinaryKeyDataTypeId ---> [nullable varbinary] [MaxLength = 510]
BinaryForeignKeyDataType.Id ---> [integer]
BinaryKeyDataType.Ex ---> [nullable varchar] [MaxLength = 255]
BinaryKeyDataType.Id ---> [varbinary] [MaxLength = 510]
BuiltInDataTypes.Enum16 ---> [smallint]
BuiltInDataTypes.Enum32 ---> [integer]
BuiltInDataTypes.Enum64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.Enum8 ---> [byte]
BuiltInDataTypes.EnumS8 ---> [smallint]
BuiltInDataTypes.EnumU16 ---> [integer]
BuiltInDataTypes.EnumU32 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.EnumU64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.Id ---> [integer]
BuiltInDataTypes.PartitionId ---> [integer]
BuiltInDataTypes.TestBoolean ---> [smallint]
BuiltInDataTypes.TestByte ---> [byte]
BuiltInDataTypes.TestCharacter ---> [varchar] [MaxLength = 1]
BuiltInDataTypes.TestDateOnly ---> [datetime]
BuiltInDataTypes.TestDateTime ---> [datetime]
BuiltInDataTypes.TestDateTimeOffset ---> [datetime]
BuiltInDataTypes.TestDecimal ---> [decimal] [Precision = 18 Scale = 2]
BuiltInDataTypes.TestDouble ---> [double]
BuiltInDataTypes.TestInt16 ---> [smallint]
BuiltInDataTypes.TestInt32 ---> [integer]
BuiltInDataTypes.TestInt64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestSignedByte ---> [smallint]
BuiltInDataTypes.TestSingle ---> [single]
BuiltInDataTypes.TestTimeOnly ---> [datetime]
BuiltInDataTypes.TestTimeSpan ---> [datetime]
BuiltInDataTypes.TestUnsignedInt16 ---> [integer]
BuiltInDataTypes.TestUnsignedInt32 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestUnsignedInt64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.Enum16 ---> [smallint]
BuiltInDataTypesShadow.Enum32 ---> [integer]
BuiltInDataTypesShadow.Enum64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.Enum8 ---> [byte]
BuiltInDataTypesShadow.EnumS8 ---> [smallint]
BuiltInDataTypesShadow.EnumU16 ---> [integer]
BuiltInDataTypesShadow.EnumU32 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.EnumU64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.Id ---> [integer]
BuiltInDataTypesShadow.PartitionId ---> [integer]
BuiltInDataTypesShadow.TestBoolean ---> [smallint]
BuiltInDataTypesShadow.TestByte ---> [byte]
BuiltInDataTypesShadow.TestCharacter ---> [varchar] [MaxLength = 1]
BuiltInDataTypesShadow.TestDateOnly ---> [datetime]
BuiltInDataTypesShadow.TestDateTime ---> [datetime]
BuiltInDataTypesShadow.TestDateTimeOffset ---> [datetime]
BuiltInDataTypesShadow.TestDecimal ---> [decimal] [Precision = 18 Scale = 2]
BuiltInDataTypesShadow.TestDouble ---> [double]
BuiltInDataTypesShadow.TestInt16 ---> [smallint]
BuiltInDataTypesShadow.TestInt32 ---> [integer]
BuiltInDataTypesShadow.TestInt64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestSignedByte ---> [smallint]
BuiltInDataTypesShadow.TestSingle ---> [single]
BuiltInDataTypesShadow.TestTimeOnly ---> [datetime]
BuiltInDataTypesShadow.TestTimeSpan ---> [datetime]
BuiltInDataTypesShadow.TestUnsignedInt16 ---> [integer]
BuiltInDataTypesShadow.TestUnsignedInt32 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestUnsignedInt64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.Enum16 ---> [nullable smallint]
BuiltInNullableDataTypes.Enum32 ---> [nullable integer]
BuiltInNullableDataTypes.Enum64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.Enum8 ---> [nullable byte]
BuiltInNullableDataTypes.EnumS8 ---> [nullable smallint]
BuiltInNullableDataTypes.EnumU16 ---> [nullable integer]
BuiltInNullableDataTypes.EnumU32 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.EnumU64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.Id ---> [integer]
BuiltInNullableDataTypes.PartitionId ---> [integer]
BuiltInNullableDataTypes.TestByteArray ---> [nullable longbinary]
BuiltInNullableDataTypes.TestNullableBoolean ---> [nullable smallint]
BuiltInNullableDataTypes.TestNullableByte ---> [nullable byte]
BuiltInNullableDataTypes.TestNullableCharacter ---> [nullable varchar] [MaxLength = 1]
BuiltInNullableDataTypes.TestNullableDateOnly ---> [nullable datetime]
BuiltInNullableDataTypes.TestNullableDateTime ---> [nullable datetime]
BuiltInNullableDataTypes.TestNullableDateTimeOffset ---> [nullable datetime]
BuiltInNullableDataTypes.TestNullableDecimal ---> [nullable decimal] [Precision = 18 Scale = 2]
BuiltInNullableDataTypes.TestNullableDouble ---> [nullable double]
BuiltInNullableDataTypes.TestNullableInt16 ---> [nullable smallint]
BuiltInNullableDataTypes.TestNullableInt32 ---> [nullable integer]
BuiltInNullableDataTypes.TestNullableInt64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableSignedByte ---> [nullable smallint]
BuiltInNullableDataTypes.TestNullableSingle ---> [nullable single]
BuiltInNullableDataTypes.TestNullableTimeOnly ---> [nullable datetime]
BuiltInNullableDataTypes.TestNullableTimeSpan ---> [nullable datetime]
BuiltInNullableDataTypes.TestNullableUnsignedInt16 ---> [nullable integer]
BuiltInNullableDataTypes.TestNullableUnsignedInt32 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableUnsignedInt64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestString ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypesShadow.Enum16 ---> [nullable smallint]
BuiltInNullableDataTypesShadow.Enum32 ---> [nullable integer]
BuiltInNullableDataTypesShadow.Enum64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.Enum8 ---> [nullable byte]
BuiltInNullableDataTypesShadow.EnumS8 ---> [nullable smallint]
BuiltInNullableDataTypesShadow.EnumU16 ---> [nullable integer]
BuiltInNullableDataTypesShadow.EnumU32 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.EnumU64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.Id ---> [integer]
BuiltInNullableDataTypesShadow.PartitionId ---> [integer]
BuiltInNullableDataTypesShadow.TestByteArray ---> [nullable longbinary]
BuiltInNullableDataTypesShadow.TestNullableBoolean ---> [nullable smallint]
BuiltInNullableDataTypesShadow.TestNullableByte ---> [nullable byte]
BuiltInNullableDataTypesShadow.TestNullableCharacter ---> [nullable varchar] [MaxLength = 1]
BuiltInNullableDataTypesShadow.TestNullableDateOnly ---> [nullable datetime]
BuiltInNullableDataTypesShadow.TestNullableDateTime ---> [nullable datetime]
BuiltInNullableDataTypesShadow.TestNullableDateTimeOffset ---> [nullable datetime]
BuiltInNullableDataTypesShadow.TestNullableDecimal ---> [nullable decimal] [Precision = 18 Scale = 2]
BuiltInNullableDataTypesShadow.TestNullableDouble ---> [nullable double]
BuiltInNullableDataTypesShadow.TestNullableInt16 ---> [nullable smallint]
BuiltInNullableDataTypesShadow.TestNullableInt32 ---> [nullable integer]
BuiltInNullableDataTypesShadow.TestNullableInt64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestNullableSignedByte ---> [nullable smallint]
BuiltInNullableDataTypesShadow.TestNullableSingle ---> [nullable single]
BuiltInNullableDataTypesShadow.TestNullableTimeOnly ---> [nullable datetime]
BuiltInNullableDataTypesShadow.TestNullableTimeSpan ---> [nullable datetime]
BuiltInNullableDataTypesShadow.TestNullableUnsignedInt16 ---> [nullable integer]
BuiltInNullableDataTypesShadow.TestNullableUnsignedInt32 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestNullableUnsignedInt64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestString ---> [nullable varchar] [MaxLength = 255]
DateTimeEnclosure.DateTimeOffset ---> [nullable datetime]
DateTimeEnclosure.Id ---> [counter]
EmailTemplate.Id ---> [guid]
EmailTemplate.TemplateType ---> [integer]
MappedDataTypes.BoolAsBit ---> [bit]
MappedDataTypes.ByteAsTinyint ---> [byte]
MappedDataTypes.BytesAsBinaryVaryingMax ---> [varbinary] [MaxLength = 510]
MappedDataTypes.BytesAsImage ---> [longbinary]
MappedDataTypes.BytesAsVarbinaryMax ---> [varbinary] [MaxLength = 510]
MappedDataTypes.CharAsAsCharVarying ---> [varchar] [MaxLength = 1]
MappedDataTypes.CharAsCharacterVaryingMax ---> [varchar] [MaxLength = 255]
MappedDataTypes.CharAsInt ---> [integer]
MappedDataTypes.CharAsNationalCharacterVaryingMax ---> [varchar] [MaxLength = 255]
MappedDataTypes.CharAsNationalCharVarying ---> [varchar] [MaxLength = 1]
MappedDataTypes.CharAsNtext ---> [longchar]
MappedDataTypes.CharAsNvarchar ---> [varchar] [MaxLength = 1]
MappedDataTypes.CharAsText ---> [varchar] [MaxLength = 1]
MappedDataTypes.CharAsVarchar ---> [varchar] [MaxLength = 1]
MappedDataTypes.DateOnlyAsDate ---> [datetime]
MappedDataTypes.DateTimeAsDate ---> [datetime]
MappedDataTypes.DateTimeAsDatetime ---> [datetime]
MappedDataTypes.Decimal ---> [decimal] [Precision = 18 Scale = 0]
MappedDataTypes.DecimalAsDec ---> [decimal] [Precision = 18 Scale = 0]
MappedDataTypes.DecimalAsMoney ---> [currency]
MappedDataTypes.DecimalAsNumeric ---> [decimal] [Precision = 18 Scale = 0]
MappedDataTypes.DoubleAsDoublePrecision ---> [double]
MappedDataTypes.DoubleAsFloat ---> [double]
MappedDataTypes.EnumAsNvarchar20 ---> [integer]
MappedDataTypes.EnumAsVarcharMax ---> [varchar] [MaxLength = 255]
MappedDataTypes.FloatAsReal ---> [single]
MappedDataTypes.GuidAsUniqueidentifier ---> [guid]
MappedDataTypes.Int ---> [integer]
MappedDataTypes.LongAsBigInt ---> [decimal] [Precision = 20 Scale = 0]
MappedDataTypes.SByteAsSmallint ---> [smallint]
MappedDataTypes.SByteAsTinyint ---> [byte]
MappedDataTypes.ShortAsSmallint ---> [smallint]
MappedDataTypes.StringAsCharacterVaryingMax ---> [varchar] [MaxLength = 255]
MappedDataTypes.StringAsCharacterVaryingMaxUtf8 ---> [varchar] [MaxLength = 255]
MappedDataTypes.StringAsCharVaryingMax ---> [varchar] [MaxLength = 255]
MappedDataTypes.StringAsCharVaryingMaxUtf8 ---> [varchar] [MaxLength = 255]
MappedDataTypes.StringAsNationalCharacterVaryingMax ---> [varchar] [MaxLength = 255]
MappedDataTypes.StringAsNationalCharVaryingMax ---> [varchar] [MaxLength = 255]
MappedDataTypes.StringAsNtext ---> [longchar]
MappedDataTypes.StringAsNvarcharMax ---> [varchar] [MaxLength = 255]
MappedDataTypes.StringAsText ---> [longchar]
MappedDataTypes.StringAsVarcharMax ---> [varchar] [MaxLength = 255]
MappedDataTypes.StringAsVarcharMaxUtf8 ---> [varchar] [MaxLength = 255]
MappedDataTypes.TimeOnlyAsTime ---> [datetime]
MappedDataTypes.TimeSpanAsTime ---> [datetime]
MappedDataTypes.UintAsBigint ---> [decimal] [Precision = 20 Scale = 0]
MappedDataTypes.UintAsInt ---> [integer]
MappedDataTypes.UlongAsBigint ---> [decimal] [Precision = 20 Scale = 0]
MappedDataTypes.UlongAsDecimal200 ---> [decimal] [Precision = 20 Scale = 0]
MappedDataTypes.UShortAsInt ---> [integer]
MappedDataTypes.UShortAsSmallint ---> [smallint]
MappedDataTypesWithIdentity.BoolAsBit ---> [bit]
MappedDataTypesWithIdentity.ByteAsTinyint ---> [byte]
MappedDataTypesWithIdentity.BytesAsBinaryVaryingMax ---> [varbinary] [MaxLength = 255]
MappedDataTypesWithIdentity.BytesAsImage ---> [longbinary]
MappedDataTypesWithIdentity.BytesAsVarbinaryMax ---> [varbinary] [MaxLength = 255]
MappedDataTypesWithIdentity.CharAsAsCharVarying ---> [varchar] [MaxLength = 1]
MappedDataTypesWithIdentity.CharAsCharacterVaryingMax ---> [varchar] [MaxLength = 255]
MappedDataTypesWithIdentity.CharAsInt ---> [integer]
MappedDataTypesWithIdentity.CharAsNationalCharacterVaryingMax ---> [varchar] [MaxLength = 255]
MappedDataTypesWithIdentity.CharAsNationalCharVarying ---> [varchar] [MaxLength = 1]
MappedDataTypesWithIdentity.CharAsNtext ---> [longchar]
MappedDataTypesWithIdentity.CharAsNvarchar ---> [varchar] [MaxLength = 1]
MappedDataTypesWithIdentity.CharAsText ---> [varchar] [MaxLength = 1]
MappedDataTypesWithIdentity.CharAsVarchar ---> [varchar] [MaxLength = 1]
MappedDataTypesWithIdentity.DateOnlyAsDate ---> [datetime]
MappedDataTypesWithIdentity.DateTimeAsDate ---> [datetime]
MappedDataTypesWithIdentity.DateTimeAsDatetime ---> [datetime]
MappedDataTypesWithIdentity.Decimal ---> [decimal] [Precision = 18 Scale = 0]
MappedDataTypesWithIdentity.DecimalAsDec ---> [decimal] [Precision = 18 Scale = 0]
MappedDataTypesWithIdentity.DecimalAsMoney ---> [currency]
MappedDataTypesWithIdentity.DecimalAsNumeric ---> [decimal] [Precision = 18 Scale = 0]
MappedDataTypesWithIdentity.DoubleAsDoublePrecision ---> [double]
MappedDataTypesWithIdentity.DoubleAsFloat ---> [double]
MappedDataTypesWithIdentity.EnumAsNvarchar20 ---> [integer]
MappedDataTypesWithIdentity.EnumAsVarcharMax ---> [varchar] [MaxLength = 255]
MappedDataTypesWithIdentity.FloatAsReal ---> [single]
MappedDataTypesWithIdentity.GuidAsUniqueidentifier ---> [guid]
MappedDataTypesWithIdentity.Id ---> [counter]
MappedDataTypesWithIdentity.Int ---> [integer]
MappedDataTypesWithIdentity.LongAsBigint ---> [decimal] [Precision = 20 Scale = 0]
MappedDataTypesWithIdentity.SByteAsSmallint ---> [smallint]
MappedDataTypesWithIdentity.SbyteAsTinyint ---> [byte]
MappedDataTypesWithIdentity.ShortAsSmallint ---> [smallint]
MappedDataTypesWithIdentity.StringAsCharacterVaryingMax ---> [varchar] [MaxLength = 255]
MappedDataTypesWithIdentity.StringAsCharacterVaryingMaxUtf8 ---> [varchar] [MaxLength = 255]
MappedDataTypesWithIdentity.StringAsCharVaryingMax ---> [varchar] [MaxLength = 255]
MappedDataTypesWithIdentity.StringAsCharVaryingMaxUtf8 ---> [varchar] [MaxLength = 255]
MappedDataTypesWithIdentity.StringAsNationalCharacterVaryingMax ---> [varchar] [MaxLength = 255]
MappedDataTypesWithIdentity.StringAsNationalCharVaryingMax ---> [varchar] [MaxLength = 255]
MappedDataTypesWithIdentity.StringAsNtext ---> [longchar]
MappedDataTypesWithIdentity.StringAsNvarcharMax ---> [varchar] [MaxLength = 255]
MappedDataTypesWithIdentity.StringAsText ---> [longchar]
MappedDataTypesWithIdentity.StringAsVarcharMax ---> [varchar] [MaxLength = 255]
MappedDataTypesWithIdentity.StringAsVarcharMaxUtf8 ---> [varchar] [MaxLength = 255]
MappedDataTypesWithIdentity.TimeOnlyAsTime ---> [datetime]
MappedDataTypesWithIdentity.TimeSpanAsTime ---> [datetime]
MappedDataTypesWithIdentity.UintAsBigint ---> [decimal] [Precision = 20 Scale = 0]
MappedDataTypesWithIdentity.UintAsInt ---> [integer]
MappedDataTypesWithIdentity.UlongAsBigint ---> [decimal] [Precision = 20 Scale = 0]
MappedDataTypesWithIdentity.UlongAsDecimal200 ---> [decimal] [Precision = 20 Scale = 0]
MappedDataTypesWithIdentity.UShortAsInt ---> [integer]
MappedDataTypesWithIdentity.UShortAsSmallint ---> [smallint]
MappedNullableDataTypes.BoolAsBit ---> [nullable bit]
MappedNullableDataTypes.ByteAsTinyint ---> [nullable byte]
MappedNullableDataTypes.BytesAsBinaryVaryingMax ---> [nullable varbinary] [MaxLength = 510]
MappedNullableDataTypes.BytesAsImage ---> [nullable longbinary]
MappedNullableDataTypes.BytesAsVarbinaryMax ---> [nullable varbinary] [MaxLength = 510]
MappedNullableDataTypes.CharAsAsCharVarying ---> [nullable varchar] [MaxLength = 1]
MappedNullableDataTypes.CharAsCharacterVaryingMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypes.CharAsInt ---> [nullable integer]
MappedNullableDataTypes.CharAsNationalCharacterVaryingMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypes.CharAsNationalCharVarying ---> [nullable varchar] [MaxLength = 1]
MappedNullableDataTypes.CharAsNtext ---> [nullable longchar]
MappedNullableDataTypes.CharAsNvarchar ---> [nullable varchar] [MaxLength = 1]
MappedNullableDataTypes.CharAsText ---> [nullable varchar] [MaxLength = 1]
MappedNullableDataTypes.CharAsVarchar ---> [nullable varchar] [MaxLength = 1]
MappedNullableDataTypes.DateOnlyAsDate ---> [nullable datetime]
MappedNullableDataTypes.DateTimeAsDate ---> [nullable datetime]
MappedNullableDataTypes.DateTimeAsDatetime ---> [nullable datetime]
MappedNullableDataTypes.Decimal ---> [nullable decimal] [Precision = 18 Scale = 0]
MappedNullableDataTypes.DecimalAsDec ---> [nullable decimal] [Precision = 18 Scale = 0]
MappedNullableDataTypes.DecimalAsMoney ---> [nullable currency]
MappedNullableDataTypes.DecimalAsNumeric ---> [nullable decimal] [Precision = 18 Scale = 0]
MappedNullableDataTypes.DoubleAsDoublePrecision ---> [nullable double]
MappedNullableDataTypes.DoubleAsFloat ---> [nullable double]
MappedNullableDataTypes.EnumAsNvarchar20 ---> [nullable integer]
MappedNullableDataTypes.EnumAsVarcharMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypes.FloatAsReal ---> [nullable single]
MappedNullableDataTypes.GuidAsUniqueidentifier ---> [nullable guid]
MappedNullableDataTypes.Int ---> [integer]
MappedNullableDataTypes.LongAsBigint ---> [nullable decimal] [Precision = 20 Scale = 0]
MappedNullableDataTypes.SByteAsSmallint ---> [nullable smallint]
MappedNullableDataTypes.SbyteAsTinyint ---> [nullable byte]
MappedNullableDataTypes.ShortAsSmallint ---> [nullable smallint]
MappedNullableDataTypes.StringAsCharacterVaryingMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypes.StringAsCharacterVaryingMaxUtf8 ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypes.StringAsCharVaryingMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypes.StringAsCharVaryingMaxUtf8 ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypes.StringAsNationalCharacterVaryingMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypes.StringAsNationalCharVaryingMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypes.StringAsNtext ---> [nullable longchar]
MappedNullableDataTypes.StringAsNvarcharMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypes.StringAsText ---> [nullable longchar]
MappedNullableDataTypes.StringAsVarcharMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypes.StringAsVarcharMaxUtf8 ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypes.TimeOnlyAsTime ---> [nullable datetime]
MappedNullableDataTypes.TimeSpanAsTime ---> [nullable datetime]
MappedNullableDataTypes.UintAsBigint ---> [nullable decimal] [Precision = 20 Scale = 0]
MappedNullableDataTypes.UintAsInt ---> [nullable integer]
MappedNullableDataTypes.UlongAsBigint ---> [nullable decimal] [Precision = 20 Scale = 0]
MappedNullableDataTypes.UlongAsDecimal200 ---> [nullable decimal] [Precision = 20 Scale = 0]
MappedNullableDataTypes.UShortAsInt ---> [nullable integer]
MappedNullableDataTypes.UShortAsSmallint ---> [nullable smallint]
MappedNullableDataTypesWithIdentity.BoolAsBit ---> [nullable bit]
MappedNullableDataTypesWithIdentity.ByteAsTinyint ---> [nullable byte]
MappedNullableDataTypesWithIdentity.BytesAsImage ---> [nullable longbinary]
MappedNullableDataTypesWithIdentity.BytesAsVarbinaryMax ---> [nullable varbinary] [MaxLength = 510]
MappedNullableDataTypesWithIdentity.BytesAsVaryingMax ---> [nullable varbinary] [MaxLength = 510]
MappedNullableDataTypesWithIdentity.CharAsAsCharVarying ---> [nullable varchar] [MaxLength = 1]
MappedNullableDataTypesWithIdentity.CharAsCharacterVaryingMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypesWithIdentity.CharAsInt ---> [nullable integer]
MappedNullableDataTypesWithIdentity.CharAsNationalCharacterVaryingMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypesWithIdentity.CharAsNationalCharVarying ---> [nullable varchar] [MaxLength = 1]
MappedNullableDataTypesWithIdentity.CharAsNtext ---> [nullable longchar]
MappedNullableDataTypesWithIdentity.CharAsNvarchar ---> [nullable varchar] [MaxLength = 1]
MappedNullableDataTypesWithIdentity.CharAsText ---> [nullable varchar] [MaxLength = 1]
MappedNullableDataTypesWithIdentity.CharAsVarchar ---> [nullable varchar] [MaxLength = 1]
MappedNullableDataTypesWithIdentity.DateOnlyAsDate ---> [nullable datetime]
MappedNullableDataTypesWithIdentity.DateTimeAsDate ---> [nullable datetime]
MappedNullableDataTypesWithIdentity.DateTimeAsDatetime ---> [nullable datetime]
MappedNullableDataTypesWithIdentity.Decimal ---> [nullable decimal] [Precision = 18 Scale = 0]
MappedNullableDataTypesWithIdentity.DecimalAsDec ---> [nullable decimal] [Precision = 18 Scale = 0]
MappedNullableDataTypesWithIdentity.DecimalAsMoney ---> [nullable currency]
MappedNullableDataTypesWithIdentity.DecimalAsNumeric ---> [nullable decimal] [Precision = 18 Scale = 0]
MappedNullableDataTypesWithIdentity.DoubleAsDoublePrecision ---> [nullable double]
MappedNullableDataTypesWithIdentity.DoubleAsFloat ---> [nullable double]
MappedNullableDataTypesWithIdentity.EnumAsNvarchar20 ---> [nullable integer]
MappedNullableDataTypesWithIdentity.EnumAsVarcharMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypesWithIdentity.FloatAsReal ---> [nullable single]
MappedNullableDataTypesWithIdentity.GuidAsUniqueidentifier ---> [nullable guid]
MappedNullableDataTypesWithIdentity.Id ---> [counter]
MappedNullableDataTypesWithIdentity.Int ---> [nullable integer]
MappedNullableDataTypesWithIdentity.LongAsBigint ---> [nullable decimal] [Precision = 20 Scale = 0]
MappedNullableDataTypesWithIdentity.SByteAsSmallint ---> [nullable smallint]
MappedNullableDataTypesWithIdentity.SbyteAsTinyint ---> [nullable byte]
MappedNullableDataTypesWithIdentity.ShortAsSmallint ---> [nullable smallint]
MappedNullableDataTypesWithIdentity.StringAsCharacterVaryingMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypesWithIdentity.StringAsCharacterVaryingMaxUtf8 ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypesWithIdentity.StringAsCharVaryingMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypesWithIdentity.StringAsCharVaryingMaxUtf8 ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypesWithIdentity.StringAsNationalCharacterVaryingMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypesWithIdentity.StringAsNationalCharVaryingMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypesWithIdentity.StringAsNtext ---> [nullable longchar]
MappedNullableDataTypesWithIdentity.StringAsNvarcharMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypesWithIdentity.StringAsText ---> [nullable longchar]
MappedNullableDataTypesWithIdentity.StringAsVarcharMax ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypesWithIdentity.StringAsVarcharMaxUtf8 ---> [nullable varchar] [MaxLength = 255]
MappedNullableDataTypesWithIdentity.TimeOnlyAsTime ---> [nullable datetime]
MappedNullableDataTypesWithIdentity.TimeSpanAsTime ---> [nullable datetime]
MappedNullableDataTypesWithIdentity.UintAsBigint ---> [nullable decimal] [Precision = 20 Scale = 0]
MappedNullableDataTypesWithIdentity.UintAsInt ---> [nullable integer]
MappedNullableDataTypesWithIdentity.UlongAsBigint ---> [nullable decimal] [Precision = 20 Scale = 0]
MappedNullableDataTypesWithIdentity.UlongAsDecimal200 ---> [nullable decimal] [Precision = 20 Scale = 0]
MappedNullableDataTypesWithIdentity.UShortAsInt ---> [nullable integer]
MappedNullableDataTypesWithIdentity.UshortAsSmallint ---> [nullable smallint]
MappedPrecisionAndScaledDataTypes.DecimalAsDec52 ---> [decimal] [Precision = 5 Scale = 2]
MappedPrecisionAndScaledDataTypes.DecimalAsDecimal52 ---> [decimal] [Precision = 5 Scale = 2]
MappedPrecisionAndScaledDataTypes.DecimalAsNumeric52 ---> [decimal] [Precision = 5 Scale = 2]
MappedPrecisionAndScaledDataTypes.Id ---> [integer]
MappedPrecisionAndScaledDataTypesWithIdentity.DecimalAsDec52 ---> [decimal] [Precision = 5 Scale = 2]
MappedPrecisionAndScaledDataTypesWithIdentity.DecimalAsDecimal52 ---> [decimal] [Precision = 5 Scale = 2]
MappedPrecisionAndScaledDataTypesWithIdentity.DecimalAsNumeric52 ---> [decimal] [Precision = 5 Scale = 2]
MappedPrecisionAndScaledDataTypesWithIdentity.Id ---> [counter]
MappedPrecisionAndScaledDataTypesWithIdentity.Int ---> [integer]
MappedScaledDataTypes.DecimalAsDec3 ---> [decimal] [Precision = 3 Scale = 0]
MappedScaledDataTypes.DecimalAsDecimal3 ---> [decimal] [Precision = 3 Scale = 0]
MappedScaledDataTypes.DecimalAsNumeric3 ---> [decimal] [Precision = 3 Scale = 0]
MappedScaledDataTypes.FloatAsDoublePrecision25 ---> [double]
MappedScaledDataTypes.FloatAsDoublePrecision3 ---> [double]
MappedScaledDataTypes.FloatAsFloat25 ---> [double]
MappedScaledDataTypes.FloatAsFloat3 ---> [double]
MappedScaledDataTypes.Id ---> [integer]
MappedScaledDataTypes.TimeOnlyAsTime3 ---> [datetime]
MappedScaledDataTypes.TimeSpanAsTime3 ---> [datetime]
MappedScaledDataTypesWithIdentity.DecimalAsDec3 ---> [decimal] [Precision = 3 Scale = 0]
MappedScaledDataTypesWithIdentity.DecimalAsDecimal3 ---> [decimal] [Precision = 3 Scale = 0]
MappedScaledDataTypesWithIdentity.DecimalAsNumeric3 ---> [decimal] [Precision = 3 Scale = 0]
MappedScaledDataTypesWithIdentity.FloatAsDoublePrecision25 ---> [double]
MappedScaledDataTypesWithIdentity.FloatAsDoublePrecision3 ---> [double]
MappedScaledDataTypesWithIdentity.FloatAsFloat25 ---> [double]
MappedScaledDataTypesWithIdentity.FloatAsFloat3 ---> [double]
MappedScaledDataTypesWithIdentity.Id ---> [counter]
MappedScaledDataTypesWithIdentity.Int ---> [integer]
MappedScaledDataTypesWithIdentity.TimeOnlyAsTime3 ---> [datetime]
MappedScaledDataTypesWithIdentity.TimeSpanAsTime3 ---> [datetime]
MappedSizedDataTypes.BytesAsBinary3 ---> [nullable varbinary] [MaxLength = 3]
MappedSizedDataTypes.BytesAsBinaryVarying3 ---> [nullable varbinary] [MaxLength = 3]
MappedSizedDataTypes.BytesAsVarbinary3 ---> [nullable varbinary] [MaxLength = 3]
MappedSizedDataTypes.CharAsAsCharVarying3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypes.CharAsCharacterVarying3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypes.CharAsNationalCharacterVarying3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypes.CharAsNationalCharVarying3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypes.CharAsNvarchar3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypes.CharAsVarchar3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypes.Id ---> [integer]
MappedSizedDataTypes.StringAsChar3 ---> [nullable char] [MaxLength = 3]
MappedSizedDataTypes.StringAsChar3Utf8 ---> [nullable char] [MaxLength = 3]
MappedSizedDataTypes.StringAsCharacter3 ---> [nullable char] [MaxLength = 3]
MappedSizedDataTypes.StringAsCharacter3Utf8 ---> [nullable char] [MaxLength = 3]
MappedSizedDataTypes.StringAsCharacterVarying3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypes.StringAsCharacterVarying3Utf8 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypes.StringAsCharVarying3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypes.StringAsCharVarying3Utf8 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypes.StringAsNationalCharacter3 ---> [nullable char] [MaxLength = 3]
MappedSizedDataTypes.StringAsNationalCharacterVarying3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypes.StringAsNationalCharVarying3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypes.StringAsNchar3 ---> [nullable char] [MaxLength = 3]
MappedSizedDataTypes.StringAsNvarchar3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypes.StringAsVarchar3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypes.StringAsVarchar3Utf8 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.BytesAsBinary3 ---> [nullable varbinary] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.BytesAsBinaryVarying3 ---> [nullable varbinary] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.BytesAsVarbinary3 ---> [nullable varbinary] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.CharAsAsCharVarying3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.CharAsCharacterVarying3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.CharAsNationalCharacterVarying3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.CharAsNationalCharVarying3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.CharAsNvarchar3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.CharAsVarchar3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.Id ---> [counter]
MappedSizedDataTypesWithIdentity.Int ---> [integer]
MappedSizedDataTypesWithIdentity.StringAsChar3 ---> [nullable char] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.StringAsChar3Utf8 ---> [nullable char] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.StringAsCharacter3 ---> [nullable char] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.StringAsCharacter3Utf8 ---> [nullable char] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.StringAsCharacterVarying3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.StringAsCharacterVarying3Utf8 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.StringAsCharVarying3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.StringAsCharVarying3Utf8 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.StringAsNationalCharacter3 ---> [nullable char] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.StringAsNationalCharacterVarying3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.StringAsNationalCharVarying3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.StringAsNchar3 ---> [nullable char] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.StringAsNvarchar3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.StringAsVarchar3 ---> [nullable varchar] [MaxLength = 3]
MappedSizedDataTypesWithIdentity.StringAsVarchar3Utf8 ---> [nullable varchar] [MaxLength = 3]
MaxLengthDataTypes.ByteArray5 ---> [nullable varbinary] [MaxLength = 5]
MaxLengthDataTypes.ByteArray9000 ---> [nullable varbinary] [MaxLength = 255]
MaxLengthDataTypes.Id ---> [integer]
MaxLengthDataTypes.String3 ---> [nullable varchar] [MaxLength = 3]
MaxLengthDataTypes.String9000 ---> [nullable varchar] [MaxLength = 255]
MaxLengthDataTypes.StringUnbounded ---> [nullable varchar] [MaxLength = 255]
StringEnclosure.Id ---> [counter]
StringEnclosure.Value ---> [nullable varchar] [MaxLength = 255]
StringForeignKeyDataType.Id ---> [integer]
StringForeignKeyDataType.StringKeyDataTypeId ---> [nullable varchar] [MaxLength = 255]
StringKeyDataType.Id ---> [varchar] [MaxLength = 255]
UnicodeDataTypes.Id ---> [integer]
UnicodeDataTypes.StringAnsi ---> [nullable varchar] [MaxLength = 255]
UnicodeDataTypes.StringAnsi3 ---> [nullable varchar] [MaxLength = 3]
UnicodeDataTypes.StringAnsi9000 ---> [nullable varchar] [MaxLength = 255]
UnicodeDataTypes.StringDefault ---> [nullable varchar] [MaxLength = 255]
UnicodeDataTypes.StringUnicode ---> [nullable varchar] [MaxLength = 255]
";

            Assert.Equal(expected, actual, ignoreLineEndingDifferences: true);
        }

        [ConditionalFact]
        public void Can_get_column_types_from_built_model()
        {
            using (var context = CreateContext())
            {
                var typeMapper = context.GetService<IRelationalTypeMappingSource>();

                foreach (var property in context.Model.GetEntityTypes().SelectMany(e => e.GetDeclaredProperties()))
                {
                    var columnType = property.GetColumnType();
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

        public static string QueryForColumnTypes(DbContext context, params string[] tablesToIgnore)
        {
            const string query
                = @"SELECT * FROM `INFORMATION_SCHEMA.COLUMNS`";

            var columns = new List<ColumnInfo>();

            using (context)
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
                            DataType = reader.GetString(3),
                            IsNullable = reader.IsDBNull(4) ? null : (bool?)(reader.GetBoolean(4)),
                            MaxLength = reader.IsDBNull(5) ? null : (int?)reader.GetInt32(5),
                            NumericPrecision = reader.IsDBNull(6) ? null : (int?)reader.GetInt32(6),
                            NumericScale = reader.IsDBNull(7) ? null : (int?)reader.GetInt32(7),
                            //DateTimePrecision = reader.IsDBNull(7) ? null : (int?)reader.GetInt16(7)
                        };

                        if (!tablesToIgnore.Contains(columnInfo.TableName))
                        {
                            columns.Add(columnInfo);
                        }
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

            var actual = builder.ToString();
            return actual;
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        public class BuiltInDataTypesJetFixture : BuiltInDataTypesFixtureBase
        {
            public override bool StrictEquality => true;

            public override bool SupportsAnsi => false;

            public override bool SupportsUnicodeToAnsiConversion => true;

            public override bool SupportsLargeStringComparisons => true;

            public override bool SupportsDecimalComparisons => true;

            protected override ITestStoreFactory TestStoreFactory
                => JetTestStoreFactory.Instance;

            protected override bool ShouldLogCategory(string logCategory)
                => logCategory == DbLoggerCategory.Query.Name;

            public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

            protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
            {
                base.OnModelCreating(modelBuilder, context);

                modelBuilder.Entity<MappedDataTypes>(
                    b =>
                    {
                        b.HasKey(e => e.Int);
                        b.Property(e => e.Int).ValueGeneratedNever();
                    });

                modelBuilder.Entity<MappedNullableDataTypes>(
                    b =>
                    {
                        b.HasKey(e => e.Int);
                        b.Property(e => e.Int).ValueGeneratedNever();
                    });

                modelBuilder.Entity<MappedDataTypesWithIdentity>();
                modelBuilder.Entity<MappedNullableDataTypesWithIdentity>();

                modelBuilder.Entity<MappedSizedDataTypes>()
                    .Property(e => e.Id)
                    .ValueGeneratedNever();

                modelBuilder.Entity<MappedScaledDataTypes>()
                    .Property(e => e.Id)
                    .ValueGeneratedNever();

                modelBuilder.Entity<MappedPrecisionAndScaledDataTypes>()
                    .Property(e => e.Id)
                    .ValueGeneratedNever();

                MakeRequired<MappedDataTypes>(modelBuilder);
                MakeRequired<MappedDataTypesWithIdentity>(modelBuilder);

                modelBuilder.Entity<MappedSizedDataTypes>();
                modelBuilder.Entity<MappedScaledDataTypes>();
                modelBuilder.Entity<MappedPrecisionAndScaledDataTypes>();
                modelBuilder.Entity<MappedSizedDataTypesWithIdentity>();
                modelBuilder.Entity<MappedScaledDataTypesWithIdentity>();
                modelBuilder.Entity<MappedPrecisionAndScaledDataTypesWithIdentity>();
                modelBuilder.Entity<MappedSizedDataTypesWithIdentity>();
                modelBuilder.Entity<MappedScaledDataTypesWithIdentity>();
                modelBuilder.Entity<MappedPrecisionAndScaledDataTypesWithIdentity>();
            }

            public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            {
                var options = base.AddOptions(builder).ConfigureWarnings(
                    c => c
                        .Log(JetEventId.DecimalTypeDefaultWarning));

                new JetDbContextOptionsBuilder(options).MinBatchSize(1);

                return options;
            }

            public override bool SupportsBinaryKeys => true;

            public override DateTime DefaultDateTime => new DateTime();
            public override bool PreservesDateTimeKind { get; }

            public override int LongStringLength => 255;

            public override string ReallyLargeString
                => string.Join("", Enumerable.Repeat("testphrase", 25));
        }

        [Flags]
        protected enum StringEnum16 : short
        {
            Value1 = 1,
            Value2 = 2,
            Value4 = 4
        }

        [Flags]
        protected enum StringEnumU16 : ushort
        {
            Value1 = 1,
            Value2 = 2,
            Value4 = 4
        }

        protected class MappedDataTypes
        {
            [Column(TypeName = "int")]
            public int Int { get; set; }

            [Column(TypeName = "bigint")]
            public long LongAsBigInt { get; set; }

            [Column(TypeName = "smallint")]
            public short ShortAsSmallint { get; set; }

            [Column(TypeName = "tinyint")]
            public byte ByteAsTinyint { get; set; }

            [Column(TypeName = "int")]
            public uint UintAsInt { get; set; }

            [Column(TypeName = "bigint")]
            public ulong UlongAsBigint { get; set; }

            [Column(TypeName = "smallint")]
            public ushort UShortAsSmallint { get; set; }

            [Column(TypeName = "tinyint")]
            public sbyte SByteAsTinyint { get; set; }

            [Column(TypeName = "bit")]
            public bool BoolAsBit { get; set; }

            [Column(TypeName = "money")]
            public decimal DecimalAsMoney { get; set; }

            /*[Column(TypeName = "smallmoney")]
            public decimal DecimalAsSmallmoney { get; set; }*/

            [Column(TypeName = "float")]
            public double DoubleAsFloat { get; set; }

            [Column(TypeName = "real")]
            public float FloatAsReal { get; set; }

            [Column(TypeName = "double precision")]
            public double DoubleAsDoublePrecision { get; set; }

            [Column(TypeName = "date")]
            public DateOnly DateOnlyAsDate { get; set; }

            [Column(TypeName = "date")]
            public DateTime DateTimeAsDate { get; set; }

            /*[Column(TypeName = "datetimeoffset")]
            public DateTimeOffset DateTimeOffsetAsDatetimeoffset { get; set; }

            [Column(TypeName = "datetime2")]
            public DateTime DateTimeAsDatetime2 { get; set; }

            [Column(TypeName = "smalldatetime")]
            public DateTime DateTimeAsSmalldatetime { get; set; }*/

            [Column(TypeName = "datetime")]
            public DateTime DateTimeAsDatetime { get; set; }

            [Column(TypeName = "time")]
            public TimeOnly TimeOnlyAsTime { get; set; }

            [Column(TypeName = "time")]
            public TimeSpan TimeSpanAsTime { get; set; }

            [Column(TypeName = "varchar(255)")]
            public string StringAsVarcharMax { get; set; }

            [Column(TypeName = "char varying(255)")]
            public string StringAsCharVaryingMax { get; set; }

            [Column(TypeName = "character varying(255)")]
            public string StringAsCharacterVaryingMax { get; set; }

            [Column(TypeName = "nvarchar(255)")]
            public string StringAsNvarcharMax { get; set; }

            [Column(TypeName = "national char varying(255)")]
            public string StringAsNationalCharVaryingMax { get; set; }

            [Column(TypeName = "national character varying(255)")]
            public string StringAsNationalCharacterVaryingMax { get; set; }

            [Column(TypeName = "varchar(255)")]
            [Unicode]
            public string StringAsVarcharMaxUtf8 { get; set; }

            [Column(TypeName = "char varying(255)")]
            [Unicode]
            public string StringAsCharVaryingMaxUtf8 { get; set; }

            [Column(TypeName = "character varying(255)")]
            [Unicode]
            public string StringAsCharacterVaryingMaxUtf8 { get; set; }

            [Column(TypeName = "text")]
            public string StringAsText { get; set; }

            [Column(TypeName = "ntext")]
            public string StringAsNtext { get; set; }

            [Column(TypeName = "varbinary(510)")]
            public byte[] BytesAsVarbinaryMax { get; set; }

            [Column(TypeName = "binary varying(510)")]
            public byte[] BytesAsBinaryVaryingMax { get; set; }

            [Column(TypeName = "image")]
            public byte[] BytesAsImage { get; set; }

            [Column(TypeName = "decimal")]
            public decimal Decimal { get; set; }

            [Column(TypeName = "dec")]
            public decimal DecimalAsDec { get; set; }

            [Column(TypeName = "numeric")]
            public decimal DecimalAsNumeric { get; set; }

            [Column(TypeName = "uniqueidentifier")]
            public Guid GuidAsUniqueidentifier { get; set; }

            [Column(TypeName = "bigint")]
            public uint UintAsBigint { get; set; }

            [Column(TypeName = "decimal(20,0)")]
            public ulong UlongAsDecimal200 { get; set; }

            [Column(TypeName = "int")]
            public ushort UShortAsInt { get; set; }

            [Column(TypeName = "smallint")]
            public sbyte SByteAsSmallint { get; set; }

            [Column(TypeName = "varchar")]
            public char CharAsVarchar { get; set; }

            [Column(TypeName = "char varying(1)")]
            public char CharAsAsCharVarying { get; set; }

            [Column(TypeName = "character varying(255)")]
            public char CharAsCharacterVaryingMax { get; set; }

            [Column(TypeName = "nvarchar")]
            public char CharAsNvarchar { get; set; }

            [Column(TypeName = "national char varying(1)")]
            public char CharAsNationalCharVarying { get; set; }

            [Column(TypeName = "national character varying(255)")]
            public char CharAsNationalCharacterVaryingMax { get; set; }

            [Column(TypeName = "text")]
            public char CharAsText { get; set; }

            [Column(TypeName = "ntext")]
            public char CharAsNtext { get; set; }

            [Column(TypeName = "int")]
            public char CharAsInt { get; set; }

            [Column(TypeName = "varchar(255)")]
            public StringEnum16 EnumAsVarcharMax { get; set; }

            [Column(TypeName = "nvarchar(20)")]
            public StringEnumU16 EnumAsNvarchar20 { get; set; }

            /*[Column(TypeName = "sql_variant")]
            public object SqlVariantString { get; set; }

            [Column(TypeName = "sql_variant")]
            public object SqlVariantInt { get; set; }*/
        }

        protected class MappedSizedDataTypes
        {
            public int Id { get; set; }

            [Column(TypeName = "char(3)")]
            public string StringAsChar3 { get; set; }

            [Column(TypeName = "character(3)")]
            public string StringAsCharacter3 { get; set; }

            [Column(TypeName = "varchar(3)")]
            public string StringAsVarchar3 { get; set; }

            [Column(TypeName = "char varying(3)")]
            public string StringAsCharVarying3 { get; set; }

            [Column(TypeName = "character varying(3)")]
            public string StringAsCharacterVarying3 { get; set; }

            [Column(TypeName = "nchar(3)")]
            public string StringAsNchar3 { get; set; }

            [Column(TypeName = "national character(3)")]
            public string StringAsNationalCharacter3 { get; set; }

            [Column(TypeName = "nvarchar(3)")]
            public string StringAsNvarchar3 { get; set; }

            [Column(TypeName = "national char varying(3)")]
            public string StringAsNationalCharVarying3 { get; set; }

            [Column(TypeName = "national character varying(3)")]
            public string StringAsNationalCharacterVarying3 { get; set; }

            [Column(TypeName = "char(3)")]
            [Unicode]
            public string StringAsChar3Utf8 { get; set; }

            [Column(TypeName = "character(3)")]
            [Unicode]
            public string StringAsCharacter3Utf8 { get; set; }

            [Column(TypeName = "varchar(3)")]
            [Unicode]
            public string StringAsVarchar3Utf8 { get; set; }

            [Column(TypeName = "char varying(3)")]
            [Unicode]
            public string StringAsCharVarying3Utf8 { get; set; }

            [Column(TypeName = "character varying(3)")]
            [Unicode]
            public string StringAsCharacterVarying3Utf8 { get; set; }

            [Column(TypeName = "binary(3)")]
            public byte[] BytesAsBinary3 { get; set; }

            [Column(TypeName = "varbinary(3)")]
            public byte[] BytesAsVarbinary3 { get; set; }

            [Column(TypeName = "binary varying(3)")]
            public byte[] BytesAsBinaryVarying3 { get; set; }

            [Column(TypeName = "varchar(3)")]
            public char? CharAsVarchar3 { get; set; }

            [Column(TypeName = "char varying(3)")]
            public char? CharAsAsCharVarying3 { get; set; }

            [Column(TypeName = "character varying(3)")]
            public char? CharAsCharacterVarying3 { get; set; }

            [Column(TypeName = "nvarchar(3)")]
            public char? CharAsNvarchar3 { get; set; }

            [Column(TypeName = "national char varying(3)")]
            public char? CharAsNationalCharVarying3 { get; set; }

            [Column(TypeName = "national character varying(3)")]
            public char? CharAsNationalCharacterVarying3 { get; set; }
        }

        protected class MappedSizedSeparatelyDataTypes
        {
            public int Id { get; set; }

            [Column(TypeName = "char")]
            public string StringAsChar3 { get; set; }

            [Column(TypeName = "character")]
            public string StringAsCharacter3 { get; set; }

            [Column(TypeName = "varchar")]
            public string StringAsVarchar3 { get; set; }

            [Column(TypeName = "char varying")]
            public string StringAsCharVarying3 { get; set; }

            [Column(TypeName = "character varying")]
            public string StringAsCharacterVarying3 { get; set; }

            [Column(TypeName = "nchar")]
            public string StringAsNchar3 { get; set; }

            [Column(TypeName = "national character")]
            public string StringAsNationalCharacter3 { get; set; }

            [Column(TypeName = "nvarchar")]
            public string StringAsNvarchar3 { get; set; }

            [Column(TypeName = "national char varying")]
            public string StringAsNationalCharVarying3 { get; set; }

            [Column(TypeName = "national character varying")]
            public string StringAsNationalCharacterVarying3 { get; set; }

            [Column(TypeName = "char")]
            public string StringAsChar3Utf8 { get; set; }

            [Column(TypeName = "character")]
            public string StringAsCharacter3Utf8 { get; set; }

            [Column(TypeName = "varchar")]
            public string StringAsVarchar3Utf8 { get; set; }

            [Column(TypeName = "char varying")]
            public string StringAsCharVarying3Utf8 { get; set; }

            [Column(TypeName = "character varying")]
            public string StringAsCharacterVarying3Utf8 { get; set; }

            [Column(TypeName = "binary")]
            public byte[] BytesAsBinary3 { get; set; }

            [Column(TypeName = "varbinary")]
            public byte[] BytesAsVarbinary3 { get; set; }

            [Column(TypeName = "binary varying")]
            public byte[] BytesAsBinaryVarying3 { get; set; }

            [Column(TypeName = "varchar")]
            public char? CharAsVarchar3 { get; set; }

            [Column(TypeName = "char varying")]
            public char? CharAsAsCharVarying3 { get; set; }

            [Column(TypeName = "character varying")]
            public char? CharAsCharacterVarying3 { get; set; }

            [Column(TypeName = "nvarchar")]
            public char? CharAsNvarchar3 { get; set; }

            [Column(TypeName = "national char varying")]
            public char? CharAsNationalCharVarying3 { get; set; }

            [Column(TypeName = "national character varying")]
            public char? CharAsNationalCharacterVarying3 { get; set; }
        }

        protected class MappedScaledDataTypes
        {
            public int Id { get; set; }

            [Column(TypeName = "float(3)")]
            [Precision(5)]
            public float FloatAsFloat3 { get; set; }

            [Column(TypeName = "double precision(3)")]
            public float FloatAsDoublePrecision3 { get; set; }

            [Column(TypeName = "float(25)")]
            [Precision(5)]
            public float FloatAsFloat25 { get; set; }

            [Column(TypeName = "double precision(25)")]
            public float FloatAsDoublePrecision25 { get; set; }

            /*[Column(TypeName = "datetimeoffset(3)")]
            [Precision(5)]
            public DateTimeOffset DateTimeOffsetAsDatetimeoffset3 { get; set; }

            [Column(TypeName = "datetime2(3)")]
            [Precision(5)]
            public DateTime DateTimeAsDatetime23 { get; set; }*/

            [Column(TypeName = "decimal(3)")]
            [Precision(5)]
            public decimal DecimalAsDecimal3 { get; set; }

            [Column(TypeName = "dec(3)")]
            public decimal DecimalAsDec3 { get; set; }

            [Column(TypeName = "numeric(3)")]
            [Precision(5)]
            public decimal DecimalAsNumeric3 { get; set; }

            [Column(TypeName = "time(3)")]
            public TimeOnly TimeOnlyAsTime3 { get; set; }

            [Column(TypeName = "time(3)")]
            public TimeSpan TimeSpanAsTime3 { get; set; }
        }

        protected class MappedScaledSeparatelyDataTypes
        {
            public int Id { get; set; }

            [Column(TypeName = "float")]
            public float FloatAsFloat3 { get; set; }

            [Column(TypeName = "double precision")]
            public float FloatAsDoublePrecision3 { get; set; }

            [Column(TypeName = "float")]
            public float FloatAsFloat25 { get; set; }

            [Column(TypeName = "double precision")]
            public float FloatAsDoublePrecision25 { get; set; }

            /*[Column(TypeName = "datetimeoffset")]
            public DateTimeOffset DateTimeOffsetAsDatetimeoffset3 { get; set; }

            [Column(TypeName = "datetime2")]
            public DateTime DateTimeAsDatetime23 { get; set; }*/

            [Column(TypeName = "decimal")]
            public decimal DecimalAsDecimal3 { get; set; }

            [Column(TypeName = "dec")]
            public decimal DecimalAsDec3 { get; set; }

            [Column(TypeName = "numeric")]
            public decimal DecimalAsNumeric3 { get; set; }

            [Column(TypeName = "time(3)")]
            public TimeOnly TimeOnlyAsTime3 { get; set; }

            [Column(TypeName = "time(3)")]
            public TimeSpan TimeSpanAsTime3 { get; set; }
        }

        protected class DoubleDataTypes
        {
            public int Id { get; set; }

            public double Double3 { get; set; }
            public double Double25 { get; set; }
        }

        protected class MappedPrecisionAndScaledDataTypes
        {
            public int Id { get; set; }

            [Column(TypeName = "decimal(5,2)")]
            [Precision(7, 3)]
            public decimal DecimalAsDecimal52 { get; set; }

            [Column(TypeName = "dec(5,2)")]
            public decimal DecimalAsDec52 { get; set; }

            [Column(TypeName = "numeric(5,2)")]
            public decimal DecimalAsNumeric52 { get; set; }
        }

        protected class MappedPrecisionAndScaledSeparatelyDataTypes
        {
            public int Id { get; set; }

            [Column(TypeName = "decimal")]
            public decimal DecimalAsDecimal52 { get; set; }

            [Column(TypeName = "dec")]
            public decimal DecimalAsDec52 { get; set; }

            [Column(TypeName = "numeric")]
            public decimal DecimalAsNumeric52 { get; set; }
        }

        protected class MappedNullableDataTypes
        {
            [Column(TypeName = "int")]
            public int? Int { get; set; }

            [Column(TypeName = "bigint")]
            public long? LongAsBigint { get; set; }

            [Column(TypeName = "smallint")]
            public short? ShortAsSmallint { get; set; }

            [Column(TypeName = "tinyint")]
            public byte? ByteAsTinyint { get; set; }

            [Column(TypeName = "int")]
            public uint? UintAsInt { get; set; }

            [Column(TypeName = "bigint")]
            public ulong? UlongAsBigint { get; set; }

            [Column(TypeName = "smallint")]
            public ushort? UShortAsSmallint { get; set; }

            [Column(TypeName = "tinyint")]
            public sbyte? SbyteAsTinyint { get; set; }

            [Column(TypeName = "bit")]
            public bool? BoolAsBit { get; set; }

            [Column(TypeName = "money")]
            public decimal? DecimalAsMoney { get; set; }

            /*[Column(TypeName = "smallmoney")]
            public decimal? DecimalAsSmallmoney { get; set; }*/

            [Column(TypeName = "float")]
            public double? DoubleAsFloat { get; set; }

            [Column(TypeName = "real")]
            public float? FloatAsReal { get; set; }

            [Column(TypeName = "double precision")]
            public double? DoubleAsDoublePrecision { get; set; }

            [Column(TypeName = "date")]
            public DateOnly? DateOnlyAsDate { get; set; }

            [Column(TypeName = "date")]
            public DateTime? DateTimeAsDate { get; set; }

            /*[Column(TypeName = "datetimeoffset")]
            public DateTimeOffset? DateTimeOffsetAsDatetimeoffset { get; set; }

            [Column(TypeName = "datetime2")]
            public DateTime? DateTimeAsDatetime2 { get; set; }

            [Column(TypeName = "smalldatetime")]
            public DateTime? DateTimeAsSmalldatetime { get; set; }*/

            [Column(TypeName = "datetime")]
            public DateTime? DateTimeAsDatetime { get; set; }

            [Column(TypeName = "time")]
            public TimeOnly? TimeOnlyAsTime { get; set; }

            [Column(TypeName = "time")]
            public TimeSpan? TimeSpanAsTime { get; set; }

            [Column(TypeName = "varchar(255)")]
            public string StringAsVarcharMax { get; set; }

            [Column(TypeName = "char varying(255)")]
            public string StringAsCharVaryingMax { get; set; }

            [Column(TypeName = "character varying(255)")]
            public string StringAsCharacterVaryingMax { get; set; }

            [Column(TypeName = "nvarchar(255)")]
            public string StringAsNvarcharMax { get; set; }

            [Column(TypeName = "national char varying(255)")]
            [MaxLength(100)]
            public string StringAsNationalCharVaryingMax { get; set; }

            [Column(TypeName = "national character varying(255)")]
            [StringLength(100)]
            public string StringAsNationalCharacterVaryingMax { get; set; }

            [Column(TypeName = "varchar(255)")]
            [Unicode]
            public string StringAsVarcharMaxUtf8 { get; set; }

            [Column(TypeName = "char varying(255)")]
            [Unicode]
            public string StringAsCharVaryingMaxUtf8 { get; set; }

            [Column(TypeName = "character varying(255)")]
            [Unicode]
            public string StringAsCharacterVaryingMaxUtf8 { get; set; }

            [Column(TypeName = "text")]
            public string StringAsText { get; set; }

            [Column(TypeName = "ntext")]
            public string StringAsNtext { get; set; }

            [Column(TypeName = "varbinary(510)")]
            public byte[] BytesAsVarbinaryMax { get; set; }

            [Column(TypeName = "binary varying(510)")]
            public byte[] BytesAsBinaryVaryingMax { get; set; }

            [Column(TypeName = "image")]
            public byte[] BytesAsImage { get; set; }

            [Column(TypeName = "decimal")]
            public decimal? Decimal { get; set; }

            [Column(TypeName = "dec")]
            public decimal? DecimalAsDec { get; set; }

            [Column(TypeName = "numeric")]
            public decimal? DecimalAsNumeric { get; set; }

            [Column(TypeName = "uniqueidentifier")]
            public Guid? GuidAsUniqueidentifier { get; set; }

            [Column(TypeName = "bigint")]
            public uint? UintAsBigint { get; set; }

            [Column(TypeName = "decimal(20,0)")]
            public ulong? UlongAsDecimal200 { get; set; }

            [Column(TypeName = "int")]
            public ushort? UShortAsInt { get; set; }

            [Column(TypeName = "smallint")]
            public sbyte? SByteAsSmallint { get; set; }

            [Column(TypeName = "varchar(1)")]
            public char? CharAsVarchar { get; set; }

            [Column(TypeName = "char varying")]
            public char? CharAsAsCharVarying { get; set; }

            [Column(TypeName = "character varying(255)")]
            public char? CharAsCharacterVaryingMax { get; set; }

            [Column(TypeName = "nvarchar")]
            public char? CharAsNvarchar { get; set; }

            [Column(TypeName = "national char varying(1)")]
            public char? CharAsNationalCharVarying { get; set; }

            [Column(TypeName = "national character varying(255)")]
            public char? CharAsNationalCharacterVaryingMax { get; set; }

            [Column(TypeName = "text")]
            public char? CharAsText { get; set; }

            [Column(TypeName = "ntext")]
            public char? CharAsNtext { get; set; }

            [Column(TypeName = "int")]
            public char? CharAsInt { get; set; }

            [Column(TypeName = "varchar(255)")]
            public StringEnum16? EnumAsVarcharMax { get; set; }

            [Column(TypeName = "nvarchar(20)")]
            public StringEnumU16? EnumAsNvarchar20 { get; set; }

            /*[Column(TypeName = "sql_variant")]
            public object SqlVariantString { get; set; }

            [Column(TypeName = "sql_variant")]
            public object SqlVariantInt { get; set; }*/
        }

        protected class MappedDataTypesWithIdentity
        {
            public int Id { get; set; }

            [Column(TypeName = "int")]
            public int Int { get; set; }

            [Column(TypeName = "bigint")]
            public long LongAsBigint { get; set; }

            [Column(TypeName = "smallint")]
            public short ShortAsSmallint { get; set; }

            [Column(TypeName = "tinyint")]
            public byte ByteAsTinyint { get; set; }

            [Column(TypeName = "int")]
            public uint UintAsInt { get; set; }

            [Column(TypeName = "bigint")]
            public ulong UlongAsBigint { get; set; }

            [Column(TypeName = "smallint")]
            public ushort UShortAsSmallint { get; set; }

            [Column(TypeName = "tinyint")]
            public sbyte SbyteAsTinyint { get; set; }

            [Column(TypeName = "bit")]
            public bool BoolAsBit { get; set; }

            [Column(TypeName = "money")]
            public decimal DecimalAsMoney { get; set; }

            /*[Column(TypeName = "smallmoney")]
            public decimal DecimalAsSmallmoney { get; set; }*/

            [Column(TypeName = "float")]
            public double DoubleAsFloat { get; set; }

            [Column(TypeName = "real")]
            public float FloatAsReal { get; set; }

            [Column(TypeName = "double precision")]
            public double DoubleAsDoublePrecision { get; set; }

            [Column(TypeName = "date")]
            public DateOnly DateOnlyAsDate { get; set; }

            [Column(TypeName = "date")]
            public DateTime DateTimeAsDate { get; set; }

            /*[Column(TypeName = "datetimeoffset")]
            public DateTimeOffset DateTimeOffsetAsDatetimeoffset { get; set; }

            [Column(TypeName = "datetime2")]
            public DateTime DateTimeAsDatetime2 { get; set; }

            [Column(TypeName = "smalldatetime")]
            public DateTime DateTimeAsSmalldatetime { get; set; }*/

            [Column(TypeName = "datetime")]
            public DateTime DateTimeAsDatetime { get; set; }

            [Column(TypeName = "time")]
            public TimeOnly TimeOnlyAsTime { get; set; }

            [Column(TypeName = "time")]
            public TimeSpan TimeSpanAsTime { get; set; }

            [Column(TypeName = "varchar(255)")]
            public string StringAsVarcharMax { get; set; }

            [Column(TypeName = "char varying(255)")]
            public string StringAsCharVaryingMax { get; set; }

            [Column(TypeName = "character varying(255)")]
            public string StringAsCharacterVaryingMax { get; set; }

            [Column(TypeName = "nvarchar(255)")]
            public string StringAsNvarcharMax { get; set; }

            [Column(TypeName = "national char varying(255)")]
            public string StringAsNationalCharVaryingMax { get; set; }

            [Column(TypeName = "national character varying(255)")]
            public string StringAsNationalCharacterVaryingMax { get; set; }

            [Column(TypeName = "varchar(255)")]
            [Unicode]
            public string StringAsVarcharMaxUtf8 { get; set; }

            [Column(TypeName = "char varying(255)")]
            [Unicode]
            public string StringAsCharVaryingMaxUtf8 { get; set; }

            [Column(TypeName = "character varying(255)")]
            [Unicode]
            public string StringAsCharacterVaryingMaxUtf8 { get; set; }

            [Column(TypeName = "text")]
            public string StringAsText { get; set; }

            [Column(TypeName = "ntext")]
            public string StringAsNtext { get; set; }

            [Column(TypeName = "varbinary(255)")]
            public byte[] BytesAsVarbinaryMax { get; set; }

            [Column(TypeName = "binary varying(255)")]
            public byte[] BytesAsBinaryVaryingMax { get; set; }

            [Column(TypeName = "image")]
            public byte[] BytesAsImage { get; set; }

            [Column(TypeName = "decimal")]
            public decimal Decimal { get; set; }

            [Column(TypeName = "dec")]
            public decimal DecimalAsDec { get; set; }

            [Column(TypeName = "numeric")]
            public decimal DecimalAsNumeric { get; set; }

            [Column(TypeName = "uniqueidentifier")]
            public Guid GuidAsUniqueidentifier { get; set; }

            [Column(TypeName = "bigint")]
            public uint UintAsBigint { get; set; }

            [Column(TypeName = "decimal(20,0)")]
            public ulong UlongAsDecimal200 { get; set; }

            [Column(TypeName = "int")]
            public ushort UShortAsInt { get; set; }

            [Column(TypeName = "smallint")]
            public sbyte SByteAsSmallint { get; set; }

            [Column(TypeName = "varchar(1)")]
            public char CharAsVarchar { get; set; }

            [Column(TypeName = "char varying")]
            public char CharAsAsCharVarying { get; set; }

            [Column(TypeName = "character varying(255)")]
            public char CharAsCharacterVaryingMax { get; set; }

            [Column(TypeName = "nvarchar")]
            public char CharAsNvarchar { get; set; }

            [Column(TypeName = "national char varying(1)")]
            public char CharAsNationalCharVarying { get; set; }

            [Column(TypeName = "national character varying(255)")]
            public char CharAsNationalCharacterVaryingMax { get; set; }

            [Column(TypeName = "text")]
            public char CharAsText { get; set; }

            [Column(TypeName = "ntext")]
            public char CharAsNtext { get; set; }

            [Column(TypeName = "int")]
            public char CharAsInt { get; set; }

            [Column(TypeName = "varchar(255)")]
            public StringEnum16 EnumAsVarcharMax { get; set; }

            [Column(TypeName = "nvarchar(20)")]
            public StringEnumU16 EnumAsNvarchar20 { get; set; }

            /*[Column(TypeName = "sql_variant")]
            public object SqlVariantString { get; set; }

            [Column(TypeName = "sql_variant")]
            public object SqlVariantInt { get; set; }*/
        }

        protected class MappedSizedDataTypesWithIdentity
        {
            public int Id { get; set; }
            public int Int { get; set; }

            [Column(TypeName = "char(3)")]
            public string StringAsChar3 { get; set; }

            [Column(TypeName = "character(3)")]
            public string StringAsCharacter3 { get; set; }

            [Column(TypeName = "varchar(3)")]
            public string StringAsVarchar3 { get; set; }

            [Column(TypeName = "char varying(3)")]
            public string StringAsCharVarying3 { get; set; }

            [Column(TypeName = "character varying(3)")]
            public string StringAsCharacterVarying3 { get; set; }

            [Column(TypeName = "nchar(3)")]
            public string StringAsNchar3 { get; set; }

            [Column(TypeName = "national character(3)")]
            public string StringAsNationalCharacter3 { get; set; }

            [Column(TypeName = "nvarchar(3)")]
            public string StringAsNvarchar3 { get; set; }

            [Column(TypeName = "national char varying(3)")]
            public string StringAsNationalCharVarying3 { get; set; }

            [Column(TypeName = "national character varying(3)")]
            public string StringAsNationalCharacterVarying3 { get; set; }

            [Column(TypeName = "char(3)")]
            [Unicode]
            public string StringAsChar3Utf8 { get; set; }

            [Column(TypeName = "character(3)")]
            [Unicode]
            public string StringAsCharacter3Utf8 { get; set; }

            [Column(TypeName = "varchar(3)")]
            [Unicode]
            public string StringAsVarchar3Utf8 { get; set; }

            [Column(TypeName = "char varying(3)")]
            [Unicode]
            public string StringAsCharVarying3Utf8 { get; set; }

            [Column(TypeName = "character varying(3)")]
            [Unicode]
            public string StringAsCharacterVarying3Utf8 { get; set; }

            [Column(TypeName = "binary(3)")]
            public byte[] BytesAsBinary3 { get; set; }

            [Column(TypeName = "varbinary(3)")]
            public byte[] BytesAsVarbinary3 { get; set; }

            [Column(TypeName = "binary varying(3)")]
            public byte[] BytesAsBinaryVarying3 { get; set; }

            [Column(TypeName = "varchar(3)")]
            public char? CharAsVarchar3 { get; set; }

            [Column(TypeName = "char varying(3)")]
            public char? CharAsAsCharVarying3 { get; set; }

            [Column(TypeName = "character varying(3)")]
            public char? CharAsCharacterVarying3 { get; set; }

            [Column(TypeName = "nvarchar(3)")]
            public char? CharAsNvarchar3 { get; set; }

            [Column(TypeName = "national char varying(3)")]
            public char? CharAsNationalCharVarying3 { get; set; }

            [Column(TypeName = "national character varying(3)")]
            public char? CharAsNationalCharacterVarying3 { get; set; }
        }

        protected class MappedScaledDataTypesWithIdentity
        {
            public int Id { get; set; }
            public int Int { get; set; }

            [Column(TypeName = "float(3)")]
            public float FloatAsFloat3 { get; set; }

            [Column(TypeName = "double precision(3)")]
            public float FloatAsDoublePrecision3 { get; set; }

            [Column(TypeName = "float(25)")]
            public float FloatAsFloat25 { get; set; }

            [Column(TypeName = "double precision(25)")]
            public float FloatAsDoublePrecision25 { get; set; }

            /*[Column(TypeName = "datetimeoffset(3)")]
            public DateTimeOffset DateTimeOffsetAsDatetimeoffset3 { get; set; }

            [Column(TypeName = "datetime2(3)")]
            public DateTime DateTimeAsDatetime23 { get; set; }*/

            [Column(TypeName = "decimal(3)")]
            public decimal DecimalAsDecimal3 { get; set; }

            [Column(TypeName = "dec(3)")]
            public decimal DecimalAsDec3 { get; set; }

            [Column(TypeName = "numeric(3)")]
            public decimal DecimalAsNumeric3 { get; set; }

            [Column(TypeName = "time(3)")]
            public TimeOnly TimeOnlyAsTime3 { get; set; }

            [Column(TypeName = "time(3)")]
            public TimeSpan TimeSpanAsTime3 { get; set; }
        }

        protected class MappedPrecisionAndScaledDataTypesWithIdentity
        {
            public int Id { get; set; }
            public int Int { get; set; }

            [Column(TypeName = "decimal(5,2)")]
            public decimal DecimalAsDecimal52 { get; set; }

            [Column(TypeName = "dec(5,2)")]
            [Precision(7, 3)]
            public decimal DecimalAsDec52 { get; set; }

            [Column(TypeName = "numeric(5,2)")]
            public decimal DecimalAsNumeric52 { get; set; }
        }

        protected class MappedNullableDataTypesWithIdentity
        {
            public int Id { get; set; }

            [Column(TypeName = "int")]
            public int? Int { get; set; }

            [Column(TypeName = "bigint")]
            public long? LongAsBigint { get; set; }

            [Column(TypeName = "smallint")]
            public short? ShortAsSmallint { get; set; }

            [Column(TypeName = "tinyint")]
            public byte? ByteAsTinyint { get; set; }

            [Column(TypeName = "int")]
            public uint? UintAsInt { get; set; }

            [Column(TypeName = "bigint")]
            public ulong? UlongAsBigint { get; set; }

            [Column(TypeName = "smallint")]
            public ushort? UshortAsSmallint { get; set; }

            [Column(TypeName = "tinyint")]
            public sbyte? SbyteAsTinyint { get; set; }

            [Column(TypeName = "bit")]
            public bool? BoolAsBit { get; set; }

            [Column(TypeName = "money")]
            public decimal? DecimalAsMoney { get; set; }

            /*[Column(TypeName = "smallmoney")]
            public decimal? DecimalAsSmallmoney { get; set; }*/

            [Column(TypeName = "float")]
            public double? DoubleAsFloat { get; set; }

            [Column(TypeName = "real")]
            public float? FloatAsReal { get; set; }

            [Column(TypeName = "double precision")]
            public double? DoubleAsDoublePrecision { get; set; }

            [Column(TypeName = "date")]
            public DateOnly? DateOnlyAsDate { get; set; }

            [Column(TypeName = "date")]
            public DateTime? DateTimeAsDate { get; set; }

            /*[Column(TypeName = "datetimeoffset")]
            public DateTimeOffset? DateTimeOffsetAsDatetimeoffset { get; set; }

            [Column(TypeName = "datetime2")]
            public DateTime? DateTimeAsDatetime2 { get; set; }

            [Column(TypeName = "smalldatetime")]
            public DateTime? DateTimeAsSmalldatetime { get; set; }*/

            [Column(TypeName = "datetime")]
            public DateTime? DateTimeAsDatetime { get; set; }

            [Column(TypeName = "time")]
            public TimeOnly? TimeOnlyAsTime { get; set; }

            [Column(TypeName = "time")]
            public TimeSpan? TimeSpanAsTime { get; set; }

            [Column(TypeName = "varchar(255)")]
            public string StringAsVarcharMax { get; set; }

            [Column(TypeName = "char varying(255)")]
            public string StringAsCharVaryingMax { get; set; }

            [Column(TypeName = "character varying(255)")]
            public string StringAsCharacterVaryingMax { get; set; }

            [Column(TypeName = "nvarchar(255)")]
            public string StringAsNvarcharMax { get; set; }

            [Column(TypeName = "national char varying(255)")]
            public string StringAsNationalCharVaryingMax { get; set; }

            [Column(TypeName = "national character varying(255)")]
            public string StringAsNationalCharacterVaryingMax { get; set; }

            [Column(TypeName = "varchar(255)")]
            [Unicode]
            public string StringAsVarcharMaxUtf8 { get; set; }

            [Column(TypeName = "char varying(255)")]
            [Unicode]
            public string StringAsCharVaryingMaxUtf8 { get; set; }

            [Column(TypeName = "character varying(255)")]
            [Unicode]
            public string StringAsCharacterVaryingMaxUtf8 { get; set; }

            [Column(TypeName = "text")]
            public string StringAsText { get; set; }

            [Column(TypeName = "ntext")]
            public string StringAsNtext { get; set; }

            [Column(TypeName = "varbinary(510)")]
            public byte[] BytesAsVarbinaryMax { get; set; }

            [Column(TypeName = "binary varying(510)")]
            public byte[] BytesAsVaryingMax { get; set; }

            [Column(TypeName = "image")]
            public byte[] BytesAsImage { get; set; }

            [Column(TypeName = "decimal")]
            public decimal? Decimal { get; set; }

            [Column(TypeName = "dec")]
            public decimal? DecimalAsDec { get; set; }

            [Column(TypeName = "numeric")]
            public decimal? DecimalAsNumeric { get; set; }

            [Column(TypeName = "uniqueidentifier")]
            public Guid? GuidAsUniqueidentifier { get; set; }

            [Column(TypeName = "bigint")]
            public uint? UintAsBigint { get; set; }

            [Column(TypeName = "decimal(20,0)")]
            public ulong? UlongAsDecimal200 { get; set; }

            [Column(TypeName = "int")]
            public ushort? UShortAsInt { get; set; }

            [Column(TypeName = "smallint")]
            public sbyte? SByteAsSmallint { get; set; }

            [Column(TypeName = "varchar")]
            public char? CharAsVarchar { get; set; }

            [Column(TypeName = "char varying(1)")]
            public char? CharAsAsCharVarying { get; set; }

            [Column(TypeName = "character varying(255)")]
            public char? CharAsCharacterVaryingMax { get; set; }

            [Column(TypeName = "nvarchar(1)")]
            public char? CharAsNvarchar { get; set; }

            [Column(TypeName = "national char varying")]
            public char? CharAsNationalCharVarying { get; set; }

            [Column(TypeName = "national character varying(255)")]
            public char? CharAsNationalCharacterVaryingMax { get; set; }

            [Column(TypeName = "text")]
            public char? CharAsText { get; set; }

            [Column(TypeName = "ntext")]
            public char? CharAsNtext { get; set; }

            [Column(TypeName = "int")]
            public char? CharAsInt { get; set; }

            [Column(TypeName = "varchar(255)")]
            public StringEnum16? EnumAsVarcharMax { get; set; }

            [Column(TypeName = "nvarchar(20)")]
            public StringEnumU16? EnumAsNvarchar20 { get; set; }

            /*[Column(TypeName = "sql_variant")]
            public object SqlVariantString { get; set; }

            [Column(TypeName = "sql_variant")]
            public object SqlVariantInt { get; set; }*/
        }

        public class ColumnInfo
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

        public override async Task Can_insert_and_read_back_all_non_nullable_data_types()
        {
            using (var context = CreateContext())
            {
                context.Set<BuiltInDataTypes>().Add(
                    new BuiltInDataTypes
                    {
                        Id = 1,
                        PartitionId = 1,
                        TestInt16 = -1234,
                        TestInt32 = -123456789,
                        TestInt64 = -1234567890123456789L,
                        TestDouble = -1.23456789,
                        TestDecimal = -1234567890.01M,
                        TestDateTime = DateTime.Parse("01/01/2000 12:34:56", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal),
                        TestDateTimeOffset = new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0)),
                        TestTimeSpan = new TimeSpan(0, 10, 9, 8, 0),
                        TestDateOnly = new DateOnly(2020, 3, 1),
                        TestTimeOnly = new TimeOnly(12, 30, 45, 0),
                        TestSingle = -1.234F,
                        TestBoolean = true,
                        TestByte = 255,
                        TestUnsignedInt16 = 1234,
                        TestUnsignedInt32 = 1234565789U,
                        TestUnsignedInt64 = 1234567890123456789UL,
                        TestCharacter = 'a',
                        TestSignedByte = -128,
                        Enum64 = Enum64.SomeValue,
                        Enum32 = Enum32.SomeValue,
                        Enum16 = Enum16.SomeValue,
                        Enum8 = Enum8.SomeValue,
                        EnumU64 = EnumU64.SomeValue,
                        EnumU32 = EnumU32.SomeValue,
                        EnumU16 = EnumU16.SomeValue,
                        EnumS8 = EnumS8.SomeValue
                    });

                Assert.Equal(1, await context.SaveChangesAsync());
            }

            using (var context = CreateContext())
            {
                var dt = context.Set<BuiltInDataTypes>().Where(e => e.Id == 1).ToList().Single();

                var entityType = context.Model.FindEntityType(typeof(BuiltInDataTypes));
                AssertEqualIfMapped(entityType, (short)-1234, () => dt.TestInt16);
                AssertEqualIfMapped(entityType, -123456789, () => dt.TestInt32);
                AssertEqualIfMapped(entityType, -1234567890123456789L, () => dt.TestInt64);
                AssertEqualIfMapped(entityType, -1.23456789, () => dt.TestDouble);
                AssertEqualIfMapped(entityType, -1234567890.01M, () => dt.TestDecimal);
                AssertEqualIfMapped(
                    entityType, DateTime.Parse("01/01/2000 12:34:56", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal),
                    () => dt.TestDateTime);
                AssertEqualIfMapped(
                    entityType, JetTestHelpers.GetExpectedValue(new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0))),
                    () => dt.TestDateTimeOffset);
                AssertEqualIfMapped(entityType, new TimeSpan(0, 10, 9, 8, 0), () => dt.TestTimeSpan);
                AssertEqualIfMapped(entityType, new DateOnly(2020, 3, 1), () => dt.TestDateOnly);
                AssertEqualIfMapped(entityType, new TimeOnly(12, 30, 45, 0), () => dt.TestTimeOnly);
                AssertEqualIfMapped(entityType, -1.234F, () => dt.TestSingle);
                AssertEqualIfMapped(entityType, true, () => dt.TestBoolean);
                AssertEqualIfMapped(entityType, (byte)255, () => dt.TestByte);
                AssertEqualIfMapped(entityType, Enum64.SomeValue, () => dt.Enum64);
                AssertEqualIfMapped(entityType, Enum32.SomeValue, () => dt.Enum32);
                AssertEqualIfMapped(entityType, Enum16.SomeValue, () => dt.Enum16);
                AssertEqualIfMapped(entityType, Enum8.SomeValue, () => dt.Enum8);
                AssertEqualIfMapped(entityType, (ushort)1234, () => dt.TestUnsignedInt16);
                AssertEqualIfMapped(entityType, 1234565789U, () => dt.TestUnsignedInt32);
                AssertEqualIfMapped(entityType, 1234567890123456789UL, () => dt.TestUnsignedInt64);
                AssertEqualIfMapped(entityType, 'a', () => dt.TestCharacter);
                AssertEqualIfMapped(entityType, (sbyte)-128, () => dt.TestSignedByte);
                AssertEqualIfMapped(entityType, EnumU64.SomeValue, () => dt.EnumU64);
                AssertEqualIfMapped(entityType, EnumU32.SomeValue, () => dt.EnumU32);
                AssertEqualIfMapped(entityType, EnumU16.SomeValue, () => dt.EnumU16);
                AssertEqualIfMapped(entityType, EnumS8.SomeValue, () => dt.EnumS8);
            }
        }

        public override async Task Can_insert_and_read_back_all_nullable_data_types_with_values_set_to_non_null()
        {
            using (var context = CreateContext())
            {
                context.Set<BuiltInNullableDataTypes>().Add(
                    new BuiltInNullableDataTypes
                    {
                        Id = 101,
                        PartitionId = 101,
                        TestString = "TestString",
                        TestByteArray = new byte[] { 10, 9, 8, 7, 6 },
                        TestNullableInt16 = -1234,
                        TestNullableInt32 = -123456789,
                        TestNullableInt64 = -1234567890123456789L,
                        TestNullableDouble = -1.23456789,
                        TestNullableDecimal = -1234567890.01M,
                        TestNullableDateTime = DateTime.Parse("01/01/2000 12:34:56").ToUniversalTime(),
                        TestNullableDateTimeOffset = new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0)),
                        TestNullableDateOnly = new DateOnly(2020, 3, 1),
                        TestNullableTimeOnly = new TimeOnly(12, 30, 45, 0),
                        TestNullableTimeSpan = new TimeSpan(0, 10, 9, 8, 0),
                        TestNullableSingle = -1.234F,
                        TestNullableBoolean = false,
                        TestNullableByte = 255,
                        TestNullableUnsignedInt16 = 1234,
                        TestNullableUnsignedInt32 = 1234565789U,
                        TestNullableUnsignedInt64 = 1234567890123456789UL,
                        TestNullableCharacter = 'a',
                        TestNullableSignedByte = -128,
                        Enum64 = Enum64.SomeValue,
                        Enum32 = Enum32.SomeValue,
                        Enum16 = Enum16.SomeValue,
                        Enum8 = Enum8.SomeValue,
                        EnumU64 = EnumU64.SomeValue,
                        EnumU32 = EnumU32.SomeValue,
                        EnumU16 = EnumU16.SomeValue,
                        EnumS8 = EnumS8.SomeValue
                    });

                Assert.Equal(1, await context.SaveChangesAsync());
            }

            using (var context = CreateContext())
            {
                var dt = context.Set<BuiltInNullableDataTypes>().Where(ndt => ndt.Id == 101).ToList().Single();

                var entityType = context.Model.FindEntityType(typeof(BuiltInNullableDataTypes));
                AssertEqualIfMapped(entityType, "TestString", () => dt.TestString);
                AssertEqualIfMapped(entityType, new byte[] { 10, 9, 8, 7, 6 }, () => dt.TestByteArray);
                AssertEqualIfMapped(entityType, (short)-1234, () => dt.TestNullableInt16);
                AssertEqualIfMapped(entityType, -123456789, () => dt.TestNullableInt32);
                AssertEqualIfMapped(entityType, -1234567890123456789L, () => dt.TestNullableInt64);
                AssertEqualIfMapped(entityType, -1.23456789, () => dt.TestNullableDouble);
                AssertEqualIfMapped(entityType, -1234567890.01M, () => dt.TestNullableDecimal);
                AssertEqualIfMapped(entityType, DateTime.Parse("01/01/2000 12:34:56").ToUniversalTime(), () => dt.TestNullableDateTime);
                AssertEqualIfMapped(
                    entityType, JetTestHelpers.GetExpectedValue(new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0))),
                    () => dt.TestNullableDateTimeOffset);
                AssertEqualIfMapped(entityType, new TimeSpan(0, 10, 9, 8, 0), () => dt.TestNullableTimeSpan);
                AssertEqualIfMapped(entityType, new DateOnly(2020, 3, 1), () => dt.TestNullableDateOnly);
                AssertEqualIfMapped(entityType, new TimeOnly(12, 30, 45, 0), () => dt.TestNullableTimeOnly);
                AssertEqualIfMapped(entityType, -1.234F, () => dt.TestNullableSingle);
                AssertEqualIfMapped(entityType, false, () => dt.TestNullableBoolean);
                AssertEqualIfMapped(entityType, (byte)255, () => dt.TestNullableByte);
                AssertEqualIfMapped(entityType, Enum64.SomeValue, () => dt.Enum64);
                AssertEqualIfMapped(entityType, Enum32.SomeValue, () => dt.Enum32);
                AssertEqualIfMapped(entityType, Enum16.SomeValue, () => dt.Enum16);
                AssertEqualIfMapped(entityType, Enum8.SomeValue, () => dt.Enum8);
                AssertEqualIfMapped(entityType, (ushort)1234, () => dt.TestNullableUnsignedInt16);
                AssertEqualIfMapped(entityType, 1234565789U, () => dt.TestNullableUnsignedInt32);
                AssertEqualIfMapped(entityType, 1234567890123456789UL, () => dt.TestNullableUnsignedInt64);
                AssertEqualIfMapped(entityType, 'a', () => dt.TestNullableCharacter);
                AssertEqualIfMapped(entityType, (sbyte)-128, () => dt.TestNullableSignedByte);
                AssertEqualIfMapped(entityType, EnumU64.SomeValue, () => dt.EnumU64);
                AssertEqualIfMapped(entityType, EnumU32.SomeValue, () => dt.EnumU32);
                AssertEqualIfMapped(entityType, EnumU16.SomeValue, () => dt.EnumU16);
                AssertEqualIfMapped(entityType, EnumS8.SomeValue, () => dt.EnumS8);
            }
        }

        public override async Task Can_insert_and_read_back_object_backed_data_types()
        {
            using (var context = CreateContext())
            {
                context.Set<ObjectBackedDataTypes>().Add(
                    new ObjectBackedDataTypes
                    {
                        Id = 101,
                        PartitionId = 101,
                        String = "TestString",
                        Bytes = new byte[] { 10, 9, 8, 7, 6 },
                        Int16 = -1234,
                        Int32 = -123456789,
                        Int64 = -1234567890123456789L,
                        Double = -1.23456789,
                        Decimal = -1234567890.01M,
                        DateTime = DateTime.Parse("01/01/2000 12:34:56"),
                        DateTimeOffset = new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0)),
                        TimeSpan = new TimeSpan(0, 10, 9, 8, 0),
                        DateOnly = new DateOnly(2020, 3, 1),
                        TimeOnly = new TimeOnly(12, 30, 45, 0),
                        Single = -1.234F,
                        Boolean = false,
                        Byte = 255,
                        UnsignedInt16 = 1234,
                        UnsignedInt32 = 1234565789U,
                        UnsignedInt64 = 1234567890123456789UL,
                        Character = 'a',
                        SignedByte = -128,
                        Enum64 = Enum64.SomeValue,
                        Enum32 = Enum32.SomeValue,
                        Enum16 = Enum16.SomeValue,
                        Enum8 = Enum8.SomeValue,
                        EnumU64 = EnumU64.SomeValue,
                        EnumU32 = EnumU32.SomeValue,
                        EnumU16 = EnumU16.SomeValue,
                        EnumS8 = EnumS8.SomeValue
                    });

                Assert.Equal(1, await context.SaveChangesAsync());
            }

            using (var context = CreateContext())
            {
                var dt = context.Set<ObjectBackedDataTypes>().Where(ndt => ndt.Id == 101).ToList().Single();

                var entityType = context.Model.FindEntityType(typeof(ObjectBackedDataTypes));
                AssertEqualIfMapped(entityType, "TestString", () => dt.String);
                AssertEqualIfMapped(entityType, new byte[] { 10, 9, 8, 7, 6 }, () => dt.Bytes);
                AssertEqualIfMapped(entityType, (short)-1234, () => dt.Int16);
                AssertEqualIfMapped(entityType, -123456789, () => dt.Int32);
                AssertEqualIfMapped(entityType, -1234567890123456789L, () => dt.Int64);
                AssertEqualIfMapped(entityType, -1.23456789, () => dt.Double);
                AssertEqualIfMapped(entityType, -1234567890.01M, () => dt.Decimal);
                AssertEqualIfMapped(entityType, DateTime.Parse("01/01/2000 12:34:56"), () => dt.DateTime);
                AssertEqualIfMapped(
                    entityType, JetTestHelpers.GetExpectedValue(new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0))),
                    () => dt.DateTimeOffset);
                AssertEqualIfMapped(entityType, new TimeSpan(0, 10, 9, 8, 0), () => dt.TimeSpan);
                AssertEqualIfMapped(entityType, new DateOnly(2020, 3, 1), () => dt.DateOnly);
                AssertEqualIfMapped(entityType, new TimeOnly(12, 30, 45, 0), () => dt.TimeOnly);
                AssertEqualIfMapped(entityType, -1.234F, () => dt.Single);
                AssertEqualIfMapped(entityType, false, () => dt.Boolean);
                AssertEqualIfMapped(entityType, (byte)255, () => dt.Byte);
                AssertEqualIfMapped(entityType, Enum64.SomeValue, () => dt.Enum64);
                AssertEqualIfMapped(entityType, Enum32.SomeValue, () => dt.Enum32);
                AssertEqualIfMapped(entityType, Enum16.SomeValue, () => dt.Enum16);
                AssertEqualIfMapped(entityType, Enum8.SomeValue, () => dt.Enum8);
                AssertEqualIfMapped(entityType, (ushort)1234, () => dt.UnsignedInt16);
                AssertEqualIfMapped(entityType, 1234565789U, () => dt.UnsignedInt32);
                AssertEqualIfMapped(entityType, 1234567890123456789UL, () => dt.UnsignedInt64);
                AssertEqualIfMapped(entityType, 'a', () => dt.Character);
                AssertEqualIfMapped(entityType, (sbyte)-128, () => dt.SignedByte);
                AssertEqualIfMapped(entityType, EnumU64.SomeValue, () => dt.EnumU64);
                AssertEqualIfMapped(entityType, EnumU32.SomeValue, () => dt.EnumU32);
                AssertEqualIfMapped(entityType, EnumU16.SomeValue, () => dt.EnumU16);
                AssertEqualIfMapped(entityType, EnumS8.SomeValue, () => dt.EnumS8);
            }
        }

        public override async Task Can_insert_and_read_back_nullable_backed_data_types()
        {
            using (var context = CreateContext())
            {
                context.Set<NullableBackedDataTypes>().Add(
                    new NullableBackedDataTypes
                    {
                        Id = 101,
                        PartitionId = 101,
                        Int16 = -1234,
                        Int32 = -123456789,
                        Int64 = -1234567890123456789L,
                        Double = -1.23456789,
                        Decimal = -1234567890.01M,
                        DateTime = DateTime.Parse("01/01/2000 12:34:56"),
                        DateTimeOffset = new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0)),
                        TimeSpan = new TimeSpan(0, 10, 9, 8, 0),
                        DateOnly = new DateOnly(2020, 3, 1),
                        TimeOnly = new TimeOnly(12, 30, 45, 0),
                        Single = -1.234F,
                        Boolean = false,
                        Byte = 255,
                        UnsignedInt16 = 1234,
                        UnsignedInt32 = 1234565789U,
                        UnsignedInt64 = 1234567890123456789UL,
                        Character = 'a',
                        SignedByte = -128,
                        Enum64 = Enum64.SomeValue,
                        Enum32 = Enum32.SomeValue,
                        Enum16 = Enum16.SomeValue,
                        Enum8 = Enum8.SomeValue,
                        EnumU64 = EnumU64.SomeValue,
                        EnumU32 = EnumU32.SomeValue,
                        EnumU16 = EnumU16.SomeValue,
                        EnumS8 = EnumS8.SomeValue
                    });

                Assert.Equal(1, await context.SaveChangesAsync());
            }

            using (var context = CreateContext())
            {
                var dt = context.Set<NullableBackedDataTypes>().Where(ndt => ndt.Id == 101).ToList().Single();

                var entityType = context.Model.FindEntityType(typeof(NullableBackedDataTypes));
                AssertEqualIfMapped(entityType, (short)-1234, () => dt.Int16);
                AssertEqualIfMapped(entityType, -123456789, () => dt.Int32);
                AssertEqualIfMapped(entityType, -1234567890123456789L, () => dt.Int64);
                AssertEqualIfMapped(entityType, -1.23456789, () => dt.Double);
                AssertEqualIfMapped(entityType, -1234567890.01M, () => dt.Decimal);
                AssertEqualIfMapped(entityType, DateTime.Parse("01/01/2000 12:34:56"), () => dt.DateTime);
                AssertEqualIfMapped(
                    entityType, JetTestHelpers.GetExpectedValue(new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0))),
                    () => dt.DateTimeOffset);
                AssertEqualIfMapped(entityType, new TimeSpan(0, 10, 9, 8, 0), () => dt.TimeSpan);
                AssertEqualIfMapped(entityType, new DateOnly(2020, 3, 1), () => dt.DateOnly);
                AssertEqualIfMapped(entityType, new TimeOnly(12, 30, 45, 0), () => dt.TimeOnly);
                AssertEqualIfMapped(entityType, -1.234F, () => dt.Single);
                AssertEqualIfMapped(entityType, false, () => dt.Boolean);
                AssertEqualIfMapped(entityType, (byte)255, () => dt.Byte);
                AssertEqualIfMapped(entityType, Enum64.SomeValue, () => dt.Enum64);
                AssertEqualIfMapped(entityType, Enum32.SomeValue, () => dt.Enum32);
                AssertEqualIfMapped(entityType, Enum16.SomeValue, () => dt.Enum16);
                AssertEqualIfMapped(entityType, Enum8.SomeValue, () => dt.Enum8);
                AssertEqualIfMapped(entityType, (ushort)1234, () => dt.UnsignedInt16);
                AssertEqualIfMapped(entityType, 1234565789U, () => dt.UnsignedInt32);
                AssertEqualIfMapped(entityType, 1234567890123456789UL, () => dt.UnsignedInt64);
                AssertEqualIfMapped(entityType, 'a', () => dt.Character);
                AssertEqualIfMapped(entityType, (sbyte)-128, () => dt.SignedByte);
                AssertEqualIfMapped(entityType, EnumU64.SomeValue, () => dt.EnumU64);
                AssertEqualIfMapped(entityType, EnumU32.SomeValue, () => dt.EnumU32);
                AssertEqualIfMapped(entityType, EnumU16.SomeValue, () => dt.EnumU16);
                AssertEqualIfMapped(entityType, EnumS8.SomeValue, () => dt.EnumS8);
            }
        }

        public override async Task Can_insert_and_read_back_non_nullable_backed_data_types()
        {
            using (var context = CreateContext())
            {
                context.Set<NonNullableBackedDataTypes>().Add(
                    new NonNullableBackedDataTypes
                    {
                        Id = 101,
                        PartitionId = 101,
                        Int16 = -1234,
                        Int32 = -123456789,
                        Int64 = -1234567890123456789L,
                        Double = -1.23456789,
                        Decimal = -1234567890.01M,
                        DateTime = DateTime.Parse("01/01/2000 12:34:56"),
                        DateTimeOffset = new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0)),
                        TimeSpan = new TimeSpan(0, 10, 9, 8, 0),
                        DateOnly = new DateOnly(2020, 3, 1),
                        TimeOnly = new TimeOnly(12, 30, 45, 0),
                        Single = -1.234F,
                        Boolean = true,
                        Byte = 255,
                        UnsignedInt16 = 1234,
                        UnsignedInt32 = 1234565789U,
                        UnsignedInt64 = 1234567890123456789UL,
                        Character = 'a',
                        SignedByte = -128,
                        Enum64 = Enum64.SomeValue,
                        Enum32 = Enum32.SomeValue,
                        Enum16 = Enum16.SomeValue,
                        Enum8 = Enum8.SomeValue,
                        EnumU64 = EnumU64.SomeValue,
                        EnumU32 = EnumU32.SomeValue,
                        EnumU16 = EnumU16.SomeValue,
                        EnumS8 = EnumS8.SomeValue
                    });

                Assert.Equal(1, await context.SaveChangesAsync());
            }

            using (var context = CreateContext())
            {
                var dt = context.Set<NonNullableBackedDataTypes>().Where(ndt => ndt.Id == 101).ToList().Single();

                var entityType = context.Model.FindEntityType(typeof(NonNullableBackedDataTypes));
                AssertEqualIfMapped(entityType, (short)-1234, () => dt.Int16);
                AssertEqualIfMapped(entityType, -123456789, () => dt.Int32);
                AssertEqualIfMapped(entityType, -1234567890123456789L, () => dt.Int64);
                AssertEqualIfMapped(entityType, -1234567890123456789L, () => dt.Int64);
                AssertEqualIfMapped(entityType, -1.23456789, () => dt.Double);
                AssertEqualIfMapped(entityType, -1234567890.01M, () => dt.Decimal);
                AssertEqualIfMapped(entityType, DateTime.Parse("01/01/2000 12:34:56"), () => dt.DateTime);
                AssertEqualIfMapped(
                    entityType, JetTestHelpers.GetExpectedValue(new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0))),
                    () => dt.DateTimeOffset);
                AssertEqualIfMapped(entityType, new TimeSpan(0, 10, 9, 8, 0), () => dt.TimeSpan);
                AssertEqualIfMapped(entityType, new DateOnly(2020, 3, 1), () => dt.DateOnly);
                AssertEqualIfMapped(entityType, new TimeOnly(12, 30, 45, 0), () => dt.TimeOnly);
                AssertEqualIfMapped(entityType, -1.234F, () => dt.Single);
                AssertEqualIfMapped(entityType, true, () => dt.Boolean);
                AssertEqualIfMapped(entityType, (byte)255, () => dt.Byte);
                AssertEqualIfMapped(entityType, Enum64.SomeValue, () => dt.Enum64);
                AssertEqualIfMapped(entityType, Enum32.SomeValue, () => dt.Enum32);
                AssertEqualIfMapped(entityType, Enum16.SomeValue, () => dt.Enum16);
                AssertEqualIfMapped(entityType, Enum8.SomeValue, () => dt.Enum8);
                AssertEqualIfMapped(entityType, (ushort)1234, () => dt.UnsignedInt16);
                AssertEqualIfMapped(entityType, 1234565789U, () => dt.UnsignedInt32);
                AssertEqualIfMapped(entityType, 1234567890123456789UL, () => dt.UnsignedInt64);
                AssertEqualIfMapped(entityType, 'a', () => dt.Character);
                AssertEqualIfMapped(entityType, (sbyte)-128, () => dt.SignedByte);
                AssertEqualIfMapped(entityType, EnumU64.SomeValue, () => dt.EnumU64);
                AssertEqualIfMapped(entityType, EnumU32.SomeValue, () => dt.EnumU32);
                AssertEqualIfMapped(entityType, EnumU16.SomeValue, () => dt.EnumU16);
                AssertEqualIfMapped(entityType, EnumS8.SomeValue, () => dt.EnumS8);
            }
        }

        public override async Task Can_query_using_any_data_type()
        {
            using var context = CreateContext();
            var source = AddTestBuiltInDataTypes(context.Set<BuiltInDataTypes>());

            Assert.Equal(1, await context.SaveChangesAsync());

            QueryBuiltInDataTypesTest(source);
        }

        public override async Task Can_query_using_any_data_type_shadow()
        {
            using var context = CreateContext();
            var source = AddTestBuiltInDataTypes(context.Set<BuiltInDataTypesShadow>());

            Assert.Equal(1, await context.SaveChangesAsync());

            QueryBuiltInDataTypesTest(source);
        }

        private void QueryBuiltInDataTypesTest<TEntity>(EntityEntry<TEntity> source)
            where TEntity : BuiltInDataTypesBase
        {
            using var context = CreateContext();
            var set = context.Set<TEntity>();
            var entity = set.Where(e => e.Id == 11).ToList().Single();
            var entityType = context.Model.FindEntityType(typeof(TEntity));

            var param1 = (short)-1234;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<short>(e, nameof(BuiltInDataTypes.TestInt16)) == param1).ToList().Single());

            var param2 = -123456789;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<int>(e, nameof(BuiltInDataTypes.TestInt32)) == param2).ToList().Single());

            var param3 = -1234567890123456789L;
            if (Fixture.IntegerPrecision == 64)
            {
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<long>(e, nameof(BuiltInDataTypes.TestInt64)) == param3).ToList().Single());
            }

            double? param4 = -1.23456789;
            if (Fixture.StrictEquality)
            {
                Assert.Same(
                    entity, set.Where(
                        e => e.Id == 11
                            && EF.Property<double>(e, nameof(BuiltInDataTypes.TestDouble)) == param4).ToList().Single());
            }
            else if (Fixture.SupportsDecimalComparisons)
            {
                double? param4l = -1.234567891;
                double? param4h = -1.234567889;
                Assert.Same(
                    entity, set.Where(
                            e => e.Id == 11
                                && (EF.Property<double>(e, nameof(BuiltInDataTypes.TestDouble)) == param4
                                    || (EF.Property<double>(e, nameof(BuiltInDataTypes.TestDouble)) > param4l
                                        && EF.Property<double>(e, nameof(BuiltInDataTypes.TestDouble)) < param4h)))
                        .ToList().Single());
            }

            var param5 = -1234567890.01M;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<decimal>(e, nameof(BuiltInDataTypes.TestDecimal)) == param5).ToList()
                    .Single());

            var param6 = Fixture.DefaultDateTime;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<DateTime>(e, nameof(BuiltInDataTypes.TestDateTime)) == param6).ToList()
                    .Single());

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestDateTimeOffset)) != null)
            {
                var param7 = new DateTimeOffset(new DateTime(), TimeSpan.FromHours(0.0));
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<DateTimeOffset>(e, nameof(BuiltInDataTypes.TestDateTimeOffset)) == param7)
                        .ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestTimeSpan)) != null)
            {
                var param8 = new TimeSpan(0, 10, 9, 8, 7);
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<TimeSpan>(e, nameof(BuiltInDataTypes.TestTimeSpan)) == param8).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestDateOnly)) != null)
            {
                var param9 = new DateOnly(2020, 3, 1);
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<DateOnly>(e, nameof(BuiltInDataTypes.TestDateOnly)) == param9).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestTimeOnly)) != null)
            {
                var param10 = new TimeOnly(12, 30, 45, 0);
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<TimeOnly>(e, nameof(BuiltInDataTypes.TestTimeOnly)) == param10).ToList()
                        .Single());
            }

            var param11 = -1.234F;
            if (Fixture.StrictEquality)
            {
                Assert.Same(
                    entity, set.Where(
                        e => e.Id == 11
                            && EF.Property<float>(e, nameof(BuiltInDataTypes.TestSingle)) == param11).ToList().Single());
            }
            else if (Fixture.SupportsDecimalComparisons)
            {
                var param11l = -1.2341F;
                var param11h = -1.2339F;
                Assert.Same(
                    entity, set.Where(
                        e => e.Id == 11
                            && (EF.Property<float>(e, nameof(BuiltInDataTypes.TestSingle)) == param11
                                || (EF.Property<float>(e, nameof(BuiltInDataTypes.TestSingle)) > param11l
                                    && EF.Property<float>(e, nameof(BuiltInDataTypes.TestSingle)) < param11h))).ToList().Single());
            }

            var param12 = true;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<bool>(e, nameof(BuiltInDataTypes.TestBoolean)) == param12).ToList().Single());

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestByte)) != null)
            {
                var param13 = (byte)255;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<byte>(e, nameof(BuiltInDataTypes.TestByte)) == param13).ToList().Single());
            }

            var param14 = Enum64.SomeValue;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<Enum64>(e, nameof(BuiltInDataTypes.Enum64)) == param14).ToList().Single());

            var param15 = Enum32.SomeValue;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<Enum32>(e, nameof(BuiltInDataTypes.Enum32)) == param15).ToList().Single());

            var param16 = Enum16.SomeValue;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<Enum16>(e, nameof(BuiltInDataTypes.Enum16)) == param16).ToList().Single());

            if (entityType.FindProperty(nameof(BuiltInDataTypes.Enum8)) != null)
            {
                var param17 = Enum8.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum8>(e, nameof(BuiltInDataTypes.Enum8)) == param17).ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestUnsignedInt16)) != null)
            {
                var param18 = (ushort)1234;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<ushort>(e, nameof(BuiltInDataTypes.TestUnsignedInt16)) == param18).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestUnsignedInt32)) != null)
            {
                var param19 = 1234565789U;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<uint>(e, nameof(BuiltInDataTypes.TestUnsignedInt32)) == param19).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestUnsignedInt64)) != null)
            {
                var param20 = 1234567890123456789UL;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<ulong>(e, nameof(BuiltInDataTypes.TestUnsignedInt64)) == param20).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestCharacter)) != null)
            {
                var param21 = 'a';
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<char>(e, nameof(BuiltInDataTypes.TestCharacter)) == param21).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestSignedByte)) != null)
            {
                var param22 = (sbyte)-128;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<sbyte>(e, nameof(BuiltInDataTypes.TestSignedByte)) == param22).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.EnumU64)) != null)
            {
                var param23 = EnumU64.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<EnumU64>(e, nameof(BuiltInDataTypes.EnumU64)) == param23).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.EnumU32)) != null)
            {
                var param24 = EnumU32.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<EnumU32>(e, nameof(BuiltInDataTypes.EnumU32)) == param24).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.EnumU16)) != null)
            {
                var param25 = EnumU16.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<EnumU16>(e, nameof(BuiltInDataTypes.EnumU16)) == param25).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.EnumS8)) != null)
            {
                var param26 = EnumS8.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<EnumS8>(e, nameof(BuiltInDataTypes.EnumS8)) == param26).ToList().Single());
            }

            if (UnwrapNullableType(entityType.FindProperty(nameof(BuiltInDataTypes.Enum64))?.GetProviderClrType()) == typeof(long))
            {
                var param27 = 1;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum64>(e, nameof(BuiltInDataTypes.Enum64)) == (Enum64)param27).ToList()
                        .Single());
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && (int)EF.Property<Enum64>(e, nameof(BuiltInDataTypes.Enum64)) == param27).ToList()
                        .Single());
            }

            if (UnwrapNullableType(entityType.FindProperty(nameof(BuiltInDataTypes.Enum32))?.GetProviderClrType()) == typeof(int))
            {
                var param28 = 1;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum32>(e, nameof(BuiltInDataTypes.Enum32)) == (Enum32)param28).ToList()
                        .Single());
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && (int)EF.Property<Enum32>(e, nameof(BuiltInDataTypes.Enum32)) == param28).ToList()
                        .Single());
            }

            if (UnwrapNullableType(entityType.FindProperty(nameof(BuiltInDataTypes.Enum16))?.GetProviderClrType()) == typeof(short))
            {
                var param29 = 1;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum16>(e, nameof(BuiltInDataTypes.Enum16)) == (Enum16)param29).ToList()
                        .Single());
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && (int)EF.Property<Enum16>(e, nameof(BuiltInDataTypes.Enum16)) == param29).ToList()
                        .Single());
            }

            if (UnwrapNullableType(entityType.FindProperty(nameof(BuiltInDataTypes.Enum8))?.GetProviderClrType()) == typeof(byte))
            {
                var param30 = 1;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum8>(e, nameof(BuiltInDataTypes.Enum8)) == (Enum8)param30).ToList()
                        .Single());
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && (int)EF.Property<Enum8>(e, nameof(BuiltInDataTypes.Enum8)) == param30).ToList()
                        .Single());
            }

            foreach (var propertyEntry in context.Entry(entity).Properties)
            {
                if (propertyEntry.Metadata.ValueGenerated != ValueGenerated.Never)
                {
                    continue;
                }

                Assert.Equal(
                    source.Property(propertyEntry.Metadata).CurrentValue,
                    propertyEntry.CurrentValue);
            }
        }

        protected new EntityEntry<TEntity> AddTestBuiltInDataTypes<TEntity>(DbSet<TEntity> set)
            where TEntity : BuiltInDataTypesBase, new()
        {
            var entityEntry = set.Add(
                new TEntity { Id = 11 });

            entityEntry.CurrentValues.SetValues(
                new BuiltInDataTypes
                {
                    Id = 11,
                    PartitionId = 1,
                    TestInt16 = -1234,
                    TestInt32 = -123456789,
                    TestInt64 = -1234567890123456789L,
                    TestDouble = -1.23456789,
                    TestDecimal = -1234567890.01M,
                    TestDateTime = Fixture.DefaultDateTime,
                    TestDateTimeOffset = new DateTimeOffset(new DateTime(), TimeSpan.FromHours(-8.0)),
                    TestTimeSpan = new TimeSpan(0, 10, 9, 8, 7),
                    TestDateOnly = new DateOnly(2020, 3, 1),
                    TestTimeOnly = new TimeOnly(12, 30, 45, 0),
                    TestSingle = -1.234F,
                    TestBoolean = true,
                    TestByte = 255,
                    TestUnsignedInt16 = 1234,
                    TestUnsignedInt32 = 1234565789U,
                    TestUnsignedInt64 = 1234567890123456789UL,
                    TestCharacter = 'a',
                    TestSignedByte = -128,
                    Enum64 = Enum64.SomeValue,
                    Enum32 = Enum32.SomeValue,
                    Enum16 = Enum16.SomeValue,
                    Enum8 = Enum8.SomeValue,
                    EnumU64 = EnumU64.SomeValue,
                    EnumU32 = EnumU32.SomeValue,
                    EnumU16 = EnumU16.SomeValue,
                    EnumS8 = EnumS8.SomeValue
                });

            return entityEntry;
        }

        public override async Task Can_query_using_any_nullable_data_type()
        {
            using var context = CreateContext();
            var source = AddTestBuiltInNullableDataTypes(context.Set<BuiltInNullableDataTypes>());

            Assert.Equal(1, await context.SaveChangesAsync());

            QueryBuiltInNullableDataTypesTest(source);
        }

        public override async Task Can_query_using_any_data_type_nullable_shadow()
        {
            using var context = CreateContext();
            var source = AddTestBuiltInNullableDataTypes(context.Set<BuiltInNullableDataTypesShadow>());

            Assert.Equal(1, await context.SaveChangesAsync());

            QueryBuiltInNullableDataTypesTest(source);
        }

        private void QueryBuiltInNullableDataTypesTest<TEntity>(EntityEntry<TEntity> source)
            where TEntity : BuiltInNullableDataTypesBase
        {
            using var context = CreateContext();
            var set = context.Set<TEntity>();
            var entity = set.Where(e => e.Id == 11).ToList().Single();
            var entityType = context.Model.FindEntityType(typeof(TEntity));

            short? param1 = -1234;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<short?>(e, nameof(BuiltInNullableDataTypes.TestNullableInt16)) == param1)
                    .ToList().Single());

            int? param2 = -123456789;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<int?>(e, nameof(BuiltInNullableDataTypes.TestNullableInt32)) == param2)
                    .ToList().Single());

            long? param3 = -1234567890123456789L;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<long?>(e, nameof(BuiltInNullableDataTypes.TestNullableInt64)) == param3)
                    .ToList().Single());

            double? param4 = -1.23456789;
            if (Fixture.StrictEquality)
            {
                Assert.Same(
                    entity, set.Where(
                            e => e.Id == 11
                                && EF.Property<double?>(e, nameof(BuiltInNullableDataTypes.TestNullableDouble)) == param4).ToList()
                        .Single());
            }
            else if (Fixture.SupportsDecimalComparisons)
            {
                double? param4l = -1.234567891;
                double? param4h = -1.234567889;
                Assert.Same(
                    entity, set.Where(
                            e => e.Id == 11
                                && (EF.Property<double?>(e, nameof(BuiltInNullableDataTypes.TestNullableDouble)) == param4
                                    || (EF.Property<double?>(e, nameof(BuiltInNullableDataTypes.TestNullableDouble)) > param4l
                                        && EF.Property<double?>(e, nameof(BuiltInNullableDataTypes.TestNullableDouble)) < param4h)))
                        .ToList().Single());
            }

            decimal? param5 = -1234567890.01M;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<decimal?>(e, nameof(BuiltInNullableDataTypes.TestNullableDecimal)) == param5)
                    .ToList().Single());

            DateTime? param6 = Fixture.DefaultDateTime;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<DateTime?>(e, nameof(BuiltInNullableDataTypes.TestNullableDateTime)) == param6)
                    .ToList().Single());

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableDateTimeOffset)) != null)
            {
                DateTimeOffset? param7 = new DateTimeOffset(new DateTime(), TimeSpan.FromHours(-8.0));
                Assert.Same(
                    entity,
                    set.Where(
                        e => e.Id == 11
                            && EF.Property<DateTimeOffset?>(e, nameof(BuiltInNullableDataTypes.TestNullableDateTimeOffset))
                            == param7).ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableTimeSpan)) != null)
            {
                TimeSpan? param8 = new TimeSpan(0, 10, 9, 8, 7);
                Assert.Same(
                    entity,
                    set.Where(
                            e => e.Id == 11
                                && EF.Property<TimeSpan?>(e, nameof(BuiltInNullableDataTypes.TestNullableTimeSpan))
                                == param8)
                        .ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableDateOnly)) != null)
            {
                DateOnly? param9 = new DateOnly(2020, 3, 1);
                Assert.Same(
                    entity,
                    set.Where(
                            e => e.Id == 11
                                && EF.Property<DateOnly?>(e, nameof(BuiltInNullableDataTypes.TestNullableDateOnly))
                                == param9)
                        .ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableTimeOnly)) != null)
            {
                TimeOnly? param10 = new TimeOnly(12, 30, 45, 0);
                Assert.Same(
                    entity,
                    set.Where(
                            e => e.Id == 11
                                && EF.Property<TimeOnly?>(e, nameof(BuiltInNullableDataTypes.TestNullableTimeOnly))
                                == param10)
                        .ToList().Single());
            }

            float? param11 = -1.234F;
            if (Fixture.StrictEquality)
            {
                Assert.Same(
                    entity, set.Where(
                            e => e.Id == 11
                                && EF.Property<float?>(e, nameof(BuiltInNullableDataTypes.TestNullableSingle)) == param11).ToList()
                        .Single());
            }
            else if (Fixture.SupportsDecimalComparisons)
            {
                float? param11l = -1.2341F;
                float? param11h = -1.2339F;
                Assert.Same(
                    entity, set.Where(
                            e => e.Id == 11
                                && (EF.Property<float?>(e, nameof(BuiltInNullableDataTypes.TestNullableSingle)) == param11
                                    || (EF.Property<float?>(e, nameof(BuiltInNullableDataTypes.TestNullableSingle)) > param11l
                                        && EF.Property<float?>(e, nameof(BuiltInNullableDataTypes.TestNullableSingle)) < param11h)))
                        .ToList().Single());
            }

            bool? param12 = true;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<bool?>(e, nameof(BuiltInNullableDataTypes.TestNullableBoolean)) == param12)
                    .ToList().Single());

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableByte)) != null)
            {
                byte? param13 = 255;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<byte?>(e, nameof(BuiltInNullableDataTypes.TestNullableByte)) == param13)
                        .ToList().Single());
            }

            Enum64? param14 = Enum64.SomeValue;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<Enum64?>(e, nameof(BuiltInNullableDataTypes.Enum64)) == param14).ToList()
                    .Single());

            Enum32? param15 = Enum32.SomeValue;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<Enum32?>(e, nameof(BuiltInNullableDataTypes.Enum32)) == param15).ToList()
                    .Single());

            Enum16? param16 = Enum16.SomeValue;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<Enum16?>(e, nameof(BuiltInNullableDataTypes.Enum16)) == param16).ToList()
                    .Single());

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.Enum8)) != null)
            {
                Enum8? param17 = Enum8.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum8?>(e, nameof(BuiltInNullableDataTypes.Enum8)) == param17).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableUnsignedInt16)) != null)
            {
                ushort? param18 = 1234;
                Assert.Same(
                    entity,
                    set.Where(
                        e => e.Id == 11
                            && EF.Property<ushort?>(e, nameof(BuiltInNullableDataTypes.TestNullableUnsignedInt16))
                            == param18).ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableUnsignedInt32)) != null)
            {
                uint? param19 = 1234565789U;
                Assert.Same(
                    entity,
                    set.Where(
                            e => e.Id == 11
                                && EF.Property<uint?>(e, nameof(BuiltInNullableDataTypes.TestNullableUnsignedInt32))
                                == param19)
                        .ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableUnsignedInt64)) != null)
            {
                ulong? param20 = 1234567890123456789UL;
                Assert.Same(
                    entity,
                    set.Where(
                        e => e.Id == 11
                            && EF.Property<ulong?>(
                                e, nameof(BuiltInNullableDataTypes.TestNullableUnsignedInt64))
                            == param20).ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableCharacter)) != null)
            {
                char? param21 = 'a';
                Assert.Same(
                    entity,
                    set.Where(
                            e => e.Id == 11 && EF.Property<char?>(e, nameof(BuiltInNullableDataTypes.TestNullableCharacter)) == param21)
                        .ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableSignedByte)) != null)
            {
                sbyte? param22 = -128;
                Assert.Same(
                    entity,
                    set.Where(
                            e => e.Id == 11
                                && EF.Property<sbyte?>(e, nameof(BuiltInNullableDataTypes.TestNullableSignedByte))
                                == param22)
                        .ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.EnumU64)) != null)
            {
                var param23 = EnumU64.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<EnumU64?>(e, nameof(BuiltInNullableDataTypes.EnumU64)) == param23).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.EnumU32)) != null)
            {
                var param24 = EnumU32.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<EnumU32?>(e, nameof(BuiltInNullableDataTypes.EnumU32)) == param24).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.EnumU16)) != null)
            {
                var param25 = EnumU16.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<EnumU16?>(e, nameof(BuiltInNullableDataTypes.EnumU16)) == param25).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.EnumS8)) != null)
            {
                var param26 = EnumS8.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<EnumS8?>(e, nameof(BuiltInNullableDataTypes.EnumS8)) == param26).ToList()
                        .Single());
            }

            if (UnwrapNullableType(entityType.FindProperty(nameof(BuiltInNullableDataTypes.Enum64))?.GetProviderClrType())
                == typeof(long))
            {
                int? param27 = 1;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum64?>(e, nameof(BuiltInNullableDataTypes.Enum64)) == (Enum64)param27)
                        .ToList().Single());
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && (int)EF.Property<Enum64?>(e, nameof(BuiltInNullableDataTypes.Enum64)) == param27)
                        .ToList().Single());
            }

            if (UnwrapNullableType(entityType.FindProperty(nameof(BuiltInNullableDataTypes.Enum32))?.GetProviderClrType())
                == typeof(int))
            {
                int? param28 = 1;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum32?>(e, nameof(BuiltInNullableDataTypes.Enum32)) == (Enum32)param28)
                        .ToList().Single());
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && (int)EF.Property<Enum32?>(e, nameof(BuiltInNullableDataTypes.Enum32)) == param28)
                        .ToList().Single());
            }

            if (UnwrapNullableType(entityType.FindProperty(nameof(BuiltInNullableDataTypes.Enum16))?.GetProviderClrType())
                == typeof(short))
            {
                int? param29 = 1;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum16?>(e, nameof(BuiltInNullableDataTypes.Enum16)) == (Enum16)param29)
                        .ToList().Single());
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && (int)EF.Property<Enum16?>(e, nameof(BuiltInNullableDataTypes.Enum16)) == param29)
                        .ToList().Single());
            }

            if (UnwrapNullableType(entityType.FindProperty(nameof(BuiltInNullableDataTypes.Enum8))?.GetProviderClrType())
                == typeof(byte))
            {
                int? param30 = 1;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum8?>(e, nameof(BuiltInNullableDataTypes.Enum8)) == (Enum8)param30)
                        .ToList().Single());
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && (int)EF.Property<Enum8?>(e, nameof(BuiltInNullableDataTypes.Enum8)) == param30)
                        .ToList().Single());
            }

            foreach (var propertyEntry in context.Entry(entity).Properties)
            {
                if (propertyEntry.Metadata.ValueGenerated != ValueGenerated.Never)
                {
                    continue;
                }

                Assert.Equal(
                    source.Property(propertyEntry.Metadata).CurrentValue,
                    propertyEntry.CurrentValue);
            }
        }

        protected new virtual EntityEntry<TEntity> AddTestBuiltInNullableDataTypes<TEntity>(DbSet<TEntity> set)
            where TEntity : BuiltInNullableDataTypesBase, new()
        {
            var entityEntry = set.Add(
                new TEntity { Id = 11 });

            entityEntry.CurrentValues.SetValues(
                new BuiltInNullableDataTypes
                {
                    Id = 11,
                    PartitionId = 1,
                    TestNullableInt16 = -1234,
                    TestNullableInt32 = -123456789,
                    TestNullableInt64 = -1234567890123456789L,
                    TestNullableDouble = -1.23456789,
                    TestNullableDecimal = -1234567890.01M,
                    TestNullableDateTime = Fixture.DefaultDateTime,
                    TestNullableDateTimeOffset = new DateTimeOffset(new DateTime(), TimeSpan.FromHours(-8.0)),
                    TestNullableTimeSpan = new TimeSpan(0, 10, 9, 8, 7),
                    TestNullableDateOnly = new DateOnly(2020, 3, 1),
                    TestNullableTimeOnly = new TimeOnly(12, 30, 45, 0),
                    TestNullableSingle = -1.234F,
                    TestNullableBoolean = true,
                    TestNullableByte = 255,
                    TestNullableUnsignedInt16 = 1234,
                    TestNullableUnsignedInt32 = 1234565789U,
                    TestNullableUnsignedInt64 = 1234567890123456789UL,
                    TestNullableCharacter = 'a',
                    TestNullableSignedByte = -128,
                    Enum64 = Enum64.SomeValue,
                    Enum32 = Enum32.SomeValue,
                    Enum16 = Enum16.SomeValue,
                    Enum8 = Enum8.SomeValue,
                    EnumU64 = EnumU64.SomeValue,
                    EnumU32 = EnumU32.SomeValue,
                    EnumU16 = EnumU16.SomeValue,
                    EnumS8 = EnumS8.SomeValue
                });

            return entityEntry;
        }



        private void AssertEqualIfMapped<T>(IEntityType entityType, T expected, Expression<Func<T>> actualExpression)
        {
            if (entityType.FindProperty(((MemberExpression)actualExpression.Body).Member.Name) != null)
            {
                var actual = actualExpression.Compile()();
                var type = UnwrapNullableEnumType(typeof(T));
                if (IsSignedInteger(type))
                {
                    Assert.True(Equal(Convert.ToInt64(expected), Convert.ToInt64(actual)), $"Expected:\t{expected}\r\nActual:\t{actual}");
                }
                else if (IsUnsignedInteger(type))
                {
                    Assert.True(Equal(Convert.ToUInt64(expected), Convert.ToUInt64(actual)), $"Expected:\t{expected}\r\nActual:\t{actual}");
                }
                else if (type == typeof(DateTime))
                {
                    Assert.True(
                        Equal((DateTime)(object)expected, (DateTime)(object)actual), $"Expected:\t{expected:O}\r\nActual:\t{actual:O}");
                }
                else if (type == typeof(DateTimeOffset))
                {
                    Assert.True(
                        Equal((DateTimeOffset)(object)expected, (DateTimeOffset)(object)actual),
                        $"Expected:\t{expected:O}\r\nActual:\t{actual:O}");
                }
                else
                {
                    Assert.Equal(expected, actual);
                }
            }
        }

        private bool Equal(long left, long right)
        {
            if (left >= 0
                && right >= 0)
            {
                return Equal((ulong)left, (ulong)right);
            }

            if (left < 0
                && right < 0)
            {
                return Equal((ulong)-left, (ulong)-right);
            }

            return false;
        }

        private bool Equal(ulong left, ulong right)
        {
            if (Fixture.IntegerPrecision < 64)
            {
                var largestPrecise = 1ul << Fixture.IntegerPrecision;
                while (left > largestPrecise)
                {
                    left >>= 1;
                    right >>= 1;
                }
            }

            return left == right;
        }

        private bool Equal(DateTime left, DateTime right)
            => left.Equals(right) && (!Fixture.PreservesDateTimeKind || left.Kind == right.Kind);

        private bool Equal(DateTimeOffset left, DateTimeOffset right)
            => left.EqualsExact(right);

        private static Type UnwrapNullableType(Type type)
            => type == null ? null : Nullable.GetUnderlyingType(type) ?? type;

        private static bool IsSignedInteger(Type type)
            => type == typeof(int)
                || type == typeof(long)
                || type == typeof(short)
                || type == typeof(sbyte);

        private static bool IsUnsignedInteger(Type type)
            => type == typeof(byte)
                || type == typeof(uint)
                || type == typeof(ulong)
                || type == typeof(ushort)
                || type == typeof(char);
    }
}
