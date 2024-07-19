// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using EntityFrameworkCore.Jet.Diagnostics.Internal;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

#nullable disable

// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class DataAnnotationJetTest : DataAnnotationRelationalTestBase<DataAnnotationJetTest.DataAnnotationJetFixture>
    {
        // ReSharper disable once UnusedParameter.Local
        public DataAnnotationJetTest(DataAnnotationJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            fixture.TestSqlLoggerFactory.Clear();
            fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
            => facade.UseTransaction(transaction.GetDbTransaction());

        protected override TestHelpers TestHelpers
        => JetTestHelpers.Instance;

        [ConditionalFact]
        public virtual ModelBuilder Default_for_key_string_column_throws()
        {
            var modelBuilder = CreateModelBuilder();

            modelBuilder.Entity<Login1>().Property(l => l.UserName).HasDefaultValue("default");
            modelBuilder.Ignore<Profile1>();

            Assert.Equal(
                CoreStrings.WarningAsErrorTemplate(
                    RelationalEventId.ModelValidationKeyDefaultValueWarning,
                    RelationalResources.LogKeyHasDefaultValue(new TestLogger<JetLoggingDefinitions>())
                        .GenerateMessage(nameof(Login1.UserName), nameof(Login1)),
                    "RelationalEventId.ModelValidationKeyDefaultValueWarning"),
                Assert.Throws<InvalidOperationException>(() => Validate(modelBuilder)).Message);

            return modelBuilder;
        }

        public override IModel Non_public_annotations_are_enabled()
        {
            var modelBuilder = base.Non_public_annotations_are_enabled();

            var relational = GetProperty<PrivateMemberAnnotationClass>(modelBuilder, "PersonFirstName");
            Assert.Equal("dsdsd", relational.GetColumnName());
            Assert.Equal("nvarchar(128)", relational.GetColumnType());

            return modelBuilder;
        }

        public override IModel Field_annotations_are_enabled()
        {
            var modelBuilder = base.Field_annotations_are_enabled();

            var relational = GetProperty<FieldAnnotationClass>(modelBuilder, "_personFirstName");
            Assert.Equal("dsdsd", relational.GetColumnName());
            Assert.Equal("nvarchar(128)", relational.GetColumnType());

            return modelBuilder;
        }

        public override IModel Key_and_column_work_together()
        {
            var modelBuilder = base.Key_and_column_work_together();

            var relational = GetProperty<ColumnKeyAnnotationClass1>(modelBuilder, "PersonFirstName");
            Assert.Equal("dsdsd", relational.GetColumnName());
            Assert.Equal("nvarchar(128)", relational.GetColumnType());

            return modelBuilder;
        }

        public override IModel Key_and_MaxLength_64_produce_nvarchar_64()
        {
            var modelBuilder = base.Key_and_MaxLength_64_produce_nvarchar_64();

            var property = GetProperty<ColumnKeyAnnotationClass2>(modelBuilder, "PersonFirstName");

            var storeType = property.GetRelationalTypeMapping().StoreType;

            Assert.Equal("varchar(64)", storeType);

            return modelBuilder;
        }

        public override IModel Timestamp_takes_precedence_over_MaxLength()
        {
            var modelBuilder = CreateModelBuilder();
            modelBuilder.Entity<TimestampAndMaxlength>().Ignore(x => x.NonMaxTimestamp);

            var model = Validate(modelBuilder);

            Assert.Null(GetProperty<TimestampAndMaxlength>(model, "MaxTimestamp").GetMaxLength());

            return model;
        }

        public override IModel TableNameAttribute_affects_table_name_in_TPH()
        {
            var modelBuilder = base.TableNameAttribute_affects_table_name_in_TPH();

            Assert.Equal("A", modelBuilder.FindEntityType(typeof(TNAttrBase)).GetTableName());

            return modelBuilder;
        }

        public override IModel DatabaseGeneratedOption_configures_the_property_correctly()
        {
            var modelBuilder = base.DatabaseGeneratedOption_configures_the_property_correctly();

            var identity = modelBuilder.FindEntityType(typeof(GeneratedEntity)).FindProperty(nameof(GeneratedEntity.Identity));
            Assert.Equal(JetValueGenerationStrategy.IdentityColumn, identity.GetValueGenerationStrategy());

            return modelBuilder;
        }

        public override IModel DatabaseGeneratedOption_Identity_does_not_throw_on_noninteger_properties()
        {
            var modelBuilder = base.DatabaseGeneratedOption_Identity_does_not_throw_on_noninteger_properties();

            var entity = modelBuilder.FindEntityType(typeof(GeneratedEntityNonInteger));

            var stringProperty = entity.FindProperty(nameof(GeneratedEntityNonInteger.String));
            Assert.Equal(JetValueGenerationStrategy.None, stringProperty.GetValueGenerationStrategy());

            var dateTimeProperty = entity.FindProperty(nameof(GeneratedEntityNonInteger.DateTime));
            Assert.Equal(JetValueGenerationStrategy.None, dateTimeProperty.GetValueGenerationStrategy());

            var guidProperty = entity.FindProperty(nameof(GeneratedEntityNonInteger.Guid));
            Assert.Equal(JetValueGenerationStrategy.None, guidProperty.GetValueGenerationStrategy());

            return modelBuilder;
        }

        public override async Task ConcurrencyCheckAttribute_throws_if_value_in_database_changed()
        {
            await base.ConcurrencyCheckAttribute_throws_if_value_in_database_changed();

            AssertSql(
                $@"SELECT TOP 1 `s`.`Unique_No`, `s`.`MaxLengthProperty`, `s`.`Name`, `s`.`RowVersion`, `s`.`AdditionalDetails_Name`, `s`.`AdditionalDetails_Value`, `s`.`Details_Name`, `s`.`Details_Value`
FROM `Sample` AS `s`
WHERE `s`.`Unique_No` = 1",
                //
                $@"SELECT TOP 1 `s`.`Unique_No`, `s`.`MaxLengthProperty`, `s`.`Name`, `s`.`RowVersion`, `s`.`AdditionalDetails_Name`, `s`.`AdditionalDetails_Value`, `s`.`Details_Name`, `s`.`Details_Value`
FROM `Sample` AS `s`
WHERE `s`.`Unique_No` = 1",
                //
                $@"{AssertSqlHelper.Declaration("@p0='ModifiedData' (Nullable = false) (Size = 255)")}
{AssertSqlHelper.Declaration("@p1='00000000-0000-0000-0003-000000000001'")}
{AssertSqlHelper.Declaration("@p2='1'")}
{AssertSqlHelper.Declaration("@p3='00000001-0000-0000-0000-000000000001'")}

UPDATE `Sample` SET `Name` = {AssertSqlHelper.Parameter("@p0")}, `RowVersion` = {AssertSqlHelper.Parameter("@p1")}
WHERE `Unique_No` = {AssertSqlHelper.Parameter("@p2")} AND `RowVersion` = {AssertSqlHelper.Parameter("@p3")};
SELECT @@ROWCOUNT;",
                //
                $@"{AssertSqlHelper.Declaration("@p0='ChangedData' (Nullable = false) (Size = 255)")}
{AssertSqlHelper.Declaration("@p1='00000000-0000-0000-0002-000000000001'")}
{AssertSqlHelper.Declaration("@p2='1'")}
{AssertSqlHelper.Declaration("@p3='00000001-0000-0000-0000-000000000001'")}

UPDATE `Sample` SET `Name` = {AssertSqlHelper.Parameter("@p0")}, `RowVersion` = {AssertSqlHelper.Parameter("@p1")}
WHERE `Unique_No` = {AssertSqlHelper.Parameter("@p2")} AND `RowVersion` = {AssertSqlHelper.Parameter("@p3")};
SELECT @@ROWCOUNT;");
        }

        public override async Task DatabaseGeneratedAttribute_autogenerates_values_when_set_to_identity()
        {
            await base.DatabaseGeneratedAttribute_autogenerates_values_when_set_to_identity();

            AssertSql(
                $@"@p0=NULL (Size = 10)
{AssertSqlHelper.Declaration("@p1='Third' (Nullable = false) (Size = 255)")}
{AssertSqlHelper.Declaration("@p2='00000000-0000-0000-0000-000000000003'")}
{AssertSqlHelper.Declaration("@p3='Third Additional Name' (Size = 255)")}
{AssertSqlHelper.Declaration("@p4='0' (Nullable = true)")}
{AssertSqlHelper.Declaration("@p5='Third Name' (Size = 255)")}
{AssertSqlHelper.Declaration("@p6='0' (Nullable = true)")}

INSERT INTO `Sample` (`MaxLengthProperty`, `Name`, `RowVersion`, `AdditionalDetails_Name`, `AdditionalDetails_Value`, `Details_Name`, `Details_Value`)
VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")}, {AssertSqlHelper.Parameter("@p2")}, {AssertSqlHelper.Parameter("@p3")}, {AssertSqlHelper.Parameter("@p4")}, {AssertSqlHelper.Parameter("@p5")}, {AssertSqlHelper.Parameter("@p6")});
SELECT `Unique_No`
FROM `Sample`
WHERE @@ROWCOUNT = 1 AND `Unique_No` = @@identity;");
        }

        public override async Task MaxLengthAttribute_throws_while_inserting_value_longer_than_max_length()
        {
            await base.MaxLengthAttribute_throws_while_inserting_value_longer_than_max_length();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@p0='Short' (Size = 10)")}
{AssertSqlHelper.Declaration("@p1='ValidString' (Nullable = false) (Size = 255)")}
{AssertSqlHelper.Declaration("@p2='00000000-0000-0000-0000-000000000001'")}
{AssertSqlHelper.Declaration("@p3='Third Additional Name' (Size = 255)")}
{AssertSqlHelper.Declaration("@p4='0' (Nullable = true)")}
{AssertSqlHelper.Declaration("@p5='Third Name' (Size = 255)")}
{AssertSqlHelper.Declaration("@p6='0' (Nullable = true)")}

INSERT INTO `Sample` (`MaxLengthProperty`, `Name`, `RowVersion`, `AdditionalDetails_Name`, `AdditionalDetails_Value`, `Details_Name`, `Details_Value`)
VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")}, {AssertSqlHelper.Parameter("@p2")}, {AssertSqlHelper.Parameter("@p3")}, {AssertSqlHelper.Parameter("@p4")}, {AssertSqlHelper.Parameter("@p5")}, {AssertSqlHelper.Parameter("@p6")});
SELECT `Unique_No`
FROM `Sample`
WHERE @@ROWCOUNT = 1 AND `Unique_No` = @@identity;",
                //
                $@"{AssertSqlHelper.Declaration("@p0='VeryVeryVeryVeryVeryVeryLongString' (Size = -1)")}
{AssertSqlHelper.Declaration("@p1='ValidString' (Nullable = false) (Size = 255)")}
{AssertSqlHelper.Declaration("@p2='00000000-0000-0000-0000-000000000002'")}
{AssertSqlHelper.Declaration("@p3='Third Additional Name' (Size = 255)")}
{AssertSqlHelper.Declaration("@p4='0' (Nullable = true)")}
{AssertSqlHelper.Declaration("@p5='Third Name' (Size = 255)")}
{AssertSqlHelper.Declaration("@p6='0' (Nullable = true)")}

INSERT INTO `Sample` (`MaxLengthProperty`, `Name`, `RowVersion`, `AdditionalDetails_Name`, `AdditionalDetails_Value`, `Details_Name`, `Details_Value`)
VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")}, {AssertSqlHelper.Parameter("@p2")}, {AssertSqlHelper.Parameter("@p3")}, {AssertSqlHelper.Parameter("@p4")}, {AssertSqlHelper.Parameter("@p5")}, {AssertSqlHelper.Parameter("@p6")});
SELECT `Unique_No`
FROM `Sample`
WHERE @@ROWCOUNT = 1 AND `Unique_No` = @@identity;");
        }

        public override async Task RequiredAttribute_for_navigation_throws_while_inserting_null_value()
        {
            await base.RequiredAttribute_for_navigation_throws_while_inserting_null_value();

            AssertSql(
                $@"@p0=NULL (DbType = Int32)
{AssertSqlHelper.Declaration("@p1='1'")}

INSERT INTO `BookDetails` (`AdditionalBookDetailsId`, `AnotherBookId`)
VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")});
SELECT `Id`
FROM `BookDetails`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;",
                //
                $@"@p0=NULL (DbType = Int32)
@p1=NULL (Nullable = false) (DbType = Int32)

INSERT INTO `BookDetails` (`AdditionalBookDetailsId`, `AnotherBookId`)
VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")});
SELECT `Id`
FROM `BookDetails`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;");
        }

        public override async Task RequiredAttribute_for_property_throws_while_inserting_null_value()
        {
            await base.RequiredAttribute_for_property_throws_while_inserting_null_value();

            AssertSql(
                $@"@p0=NULL (Size = 10)
{AssertSqlHelper.Declaration("@p1='ValidString' (Nullable = false) (Size = 255)")}
{AssertSqlHelper.Declaration("@p2='00000000-0000-0000-0000-000000000001'")}
{AssertSqlHelper.Declaration("@p3='Two' (Size = 255)")}
{AssertSqlHelper.Declaration("@p4='0' (Nullable = true)")}
{AssertSqlHelper.Declaration("@p5='One' (Size = 255)")}
{AssertSqlHelper.Declaration("@p6='0' (Nullable = true)")}

INSERT INTO `Sample` (`MaxLengthProperty`, `Name`, `RowVersion`, `AdditionalDetails_Name`, `AdditionalDetails_Value`, `Details_Name`, `Details_Value`)
VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")}, {AssertSqlHelper.Parameter("@p2")}, {AssertSqlHelper.Parameter("@p3")}, {AssertSqlHelper.Parameter("@p4")}, {AssertSqlHelper.Parameter("@p5")}, {AssertSqlHelper.Parameter("@p6")});
SELECT `Unique_No`
FROM `Sample`
WHERE @@ROWCOUNT = 1 AND `Unique_No` = @@identity;",
                //
                $@"@p0=NULL (Size = 10)
@p1=NULL (Nullable = false) (Size = 255)
{AssertSqlHelper.Declaration("@p2='00000000-0000-0000-0000-000000000002'")}
{AssertSqlHelper.Declaration("@p3='Two' (Size = 255)")}
{AssertSqlHelper.Declaration("@p4='0' (Nullable = true)")}
{AssertSqlHelper.Declaration("@p5='One' (Size = 255)")}
{AssertSqlHelper.Declaration("@p6='0' (Nullable = true)")}

INSERT INTO `Sample` (`MaxLengthProperty`, `Name`, `RowVersion`, `AdditionalDetails_Name`, `AdditionalDetails_Value`, `Details_Name`, `Details_Value`)
VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")}, {AssertSqlHelper.Parameter("@p2")}, {AssertSqlHelper.Parameter("@p3")}, {AssertSqlHelper.Parameter("@p4")}, {AssertSqlHelper.Parameter("@p5")}, {AssertSqlHelper.Parameter("@p6")});
SELECT `Unique_No`
FROM `Sample`
WHERE @@ROWCOUNT = 1 AND `Unique_No` = @@identity;");
        }

        public override async Task StringLengthAttribute_throws_while_inserting_value_longer_than_max_length()
        {
            await base.StringLengthAttribute_throws_while_inserting_value_longer_than_max_length();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@p0='ValidString' (Size = 16)")}

INSERT INTO `Two` (`Data`)
VALUES ({AssertSqlHelper.Parameter("@p0")});
SELECT `Id`, `Timestamp`
FROM `Two`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;",
                //
                $@"{AssertSqlHelper.Declaration("@p0='ValidButLongString' (Size = -1)")}

INSERT INTO `Two` (`Data`)
VALUES ({AssertSqlHelper.Parameter("@p0")});
SELECT `Id`, `Timestamp`
FROM `Two`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;");
        }

        public override async Task TimestampAttribute_throws_if_value_in_database_changed()
        {
            await base.TimestampAttribute_throws_if_value_in_database_changed();

            // Not validating SQL because not significantly different from other tests and
            // row version value is not stable.
        }

        private static readonly string _eol = Environment.NewLine;

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        public class DataAnnotationJetFixture : DataAnnotationRelationalFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
            public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;
        }
    }
}
