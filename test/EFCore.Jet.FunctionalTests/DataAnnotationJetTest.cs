using System;
using EntityFrameworkCore.Jet.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class DataAnnotationJetTest : DataAnnotationTestBase<JetTestStore, DataAnnotationJetFixture>
    {
        public DataAnnotationJetTest(DataAnnotationJetFixture fixture)
            : base(fixture)
        {
            fixture.TestSqlLoggerFactory.Clear();
        }

        public override ModelBuilder Non_public_annotations_are_enabled()
        {
            var modelBuilder = base.Non_public_annotations_are_enabled();

            var relational = GetProperty<PrivateMemberAnnotationClass>(modelBuilder, "PersonFirstName").Relational();
            Assert.Equal("dsdsd", relational.ColumnName);
            Assert.Equal("nvarchar(128)", relational.ColumnType);

            return modelBuilder;
        }

        public override ModelBuilder Key_and_column_work_together()
        {
            var modelBuilder = base.Key_and_column_work_together();

            var relational = GetProperty<ColumnKeyAnnotationClass1>(modelBuilder, "PersonFirstName").Relational();
            Assert.Equal("dsdsd", relational.ColumnName);
            Assert.Equal("nvarchar(128)", relational.ColumnType);

            return modelBuilder;
        }

        public override ModelBuilder Key_and_MaxLength_64_produce_nvarchar_64()
        {
            var modelBuilder = base.Key_and_MaxLength_64_produce_nvarchar_64();

            var property = GetProperty<ColumnKeyAnnotationClass2>(modelBuilder, "PersonFirstName");
            Assert.Equal("nvarchar(64)", new JetTypeMapper(new RelationalTypeMapperDependencies()).FindMapping(property).StoreType);

            return modelBuilder;
        }

        public override ModelBuilder Timestamp_takes_precedence_over_MaxLength()
        {
            var modelBuilder = base.Timestamp_takes_precedence_over_MaxLength();

            var property = GetProperty<TimestampAndMaxlen>(modelBuilder, "MaxTimestamp");
            Assert.Equal("rowversion", new JetTypeMapper(new RelationalTypeMapperDependencies()).FindMapping(property).StoreType);

            return modelBuilder;
        }

        public override ModelBuilder Timestamp_takes_precedence_over_MaxLength_with_value()
        {
            var modelBuilder = base.Timestamp_takes_precedence_over_MaxLength_with_value();

            var property = GetProperty<TimestampAndMaxlen>(modelBuilder, "NonMaxTimestamp");
            Assert.Equal("rowversion", new JetTypeMapper(new RelationalTypeMapperDependencies()).FindMapping(property).StoreType);

            return modelBuilder;
        }

        public override ModelBuilder TableNameAttribute_affects_table_name_in_TPH()
        {
            var modelBuilder = base.TableNameAttribute_affects_table_name_in_TPH();

            var relational = modelBuilder.Model.FindEntityType(typeof(TNAttrBase)).Relational();
            Assert.Equal("A", relational.TableName);

            return modelBuilder;
        }

        public override void ConcurrencyCheckAttribute_throws_if_value_in_database_changed()
        {
            using (var context = CreateContext())
            {
                context.Database.EnsureDeleted();
                context.Database.EnsureCreated();
                DataAnnotationModelInitializer.Seed(context);

                Fixture.TestSqlLoggerFactory.Clear();
            }
            base.ConcurrencyCheckAttribute_throws_if_value_in_database_changed();

            Assert.Equal(@"SELECT TOP(1) [r].[UniqueNo], [r].[MaxLengthProperty], [r].[Name], [r].[RowVersion]
FROM [Sample] AS [r]
WHERE [r].[UniqueNo] = 1

SELECT TOP(1) [r].[UniqueNo], [r].[MaxLengthProperty], [r].[Name], [r].[RowVersion]
FROM [Sample] AS [r]
WHERE [r].[UniqueNo] = 1

@p2='1'
@p0='ModifiedData' (Nullable = false) (Size = 4000)
@p1='00000000-0000-0000-0003-000000000001'
@p3='00000001-0000-0000-0000-000000000001'

UPDATE [Sample] SET [Name] = @p0, [RowVersion] = @p1
WHERE [UniqueNo] = @p2 AND [RowVersion] = @p3

@p2='1'
@p0='ChangedData' (Nullable = false) (Size = 4000)
@p1='00000000-0000-0000-0002-000000000001'
@p3='00000001-0000-0000-0000-000000000001'

UPDATE [Sample] SET [Name] = @p0, [RowVersion] = @p1
WHERE [UniqueNo] = @p2 AND [RowVersion] = @p3", Sql);
        }

        public override void DatabaseGeneratedAttribute_autogenerates_values_when_set_to_identity()
        {
            base.DatabaseGeneratedAttribute_autogenerates_values_when_set_to_identity();
            Assert.Equal(@"@p0='' (Size = 10) (DbType = String)
@p1='Third' (Nullable = false) (Size = 4000)
@p2='00000000-0000-0000-0000-000000000003'

INSERT INTO [Sample] ([MaxLengthProperty], [Name], [RowVersion])
VALUES (@p0, @p1, @p2)

@p0='' (Size = 10) (DbType = String)
@p1='Third' (Nullable = false) (Size = 4000)
@p2='00000000-0000-0000-0000-000000000003'

SELECT [UniqueNo]
FROM [Sample]
WHERE 1 = 1 AND [UniqueNo] = CAST (@@IDENTITY AS int)",
                Sql);
        }

        public override void MaxLengthAttribute_throws_while_inserting_value_longer_than_max_length()
        {
            base.MaxLengthAttribute_throws_while_inserting_value_longer_than_max_length();

            Assert.Equal(@"@p0='Short' (Size = 10)
@p1='ValidString' (Nullable = false) (Size = 4000)
@p2='00000000-0000-0000-0000-000000000001'

INSERT INTO [Sample] ([MaxLengthProperty], [Name], [RowVersion])
VALUES (@p0, @p1, @p2)

@p0='Short' (Size = 10)
@p1='ValidString' (Nullable = false) (Size = 4000)
@p2='00000000-0000-0000-0000-000000000001'

SELECT [UniqueNo]
FROM [Sample]
WHERE 1 = 1 AND [UniqueNo] = CAST (@@IDENTITY AS int)

@p0='VeryVeryVeryVeryVeryVeryLongString'
@p1='ValidString' (Nullable = false) (Size = 4000)
@p2='00000000-0000-0000-0000-000000000002'

INSERT INTO [Sample] ([MaxLengthProperty], [Name], [RowVersion])
VALUES (@p0, @p1, @p2)",
                Sql);
        }

        public override void RequiredAttribute_for_navigation_throws_while_inserting_null_value()
        {
            base.RequiredAttribute_for_navigation_throws_while_inserting_null_value();

            Assert.Equal(@"@p0='' (DbType = Int32)
@p1='Book1' (Nullable = false) (Size = 256)

INSERT INTO [BookDetail] ([AdditionalBookDetailId], [BookId])
VALUES (@p0, @p1)

@p0='' (DbType = Int32)
@p1='Book1' (Nullable = false) (Size = 256)

SELECT [Id]
FROM [BookDetail]
WHERE 1 = 1 AND [Id] = CAST (@@IDENTITY AS int)

@p0='' (DbType = Int32)
@p1='' (Nullable = false) (Size = 256) (DbType = String)

INSERT INTO [BookDetail] ([AdditionalBookDetailId], [BookId])
VALUES (@p0, @p1)",
                Sql);
        }

        public override void RequiredAttribute_for_property_throws_while_inserting_null_value()
        {
            base.RequiredAttribute_for_property_throws_while_inserting_null_value();

            Assert.Equal(@"@p0='' (Size = 10) (DbType = String)
@p1='ValidString' (Nullable = false) (Size = 4000)
@p2='00000000-0000-0000-0000-000000000001'

INSERT INTO [Sample] ([MaxLengthProperty], [Name], [RowVersion])
VALUES (@p0, @p1, @p2)

@p0='' (Size = 10) (DbType = String)
@p1='ValidString' (Nullable = false) (Size = 4000)
@p2='00000000-0000-0000-0000-000000000001'

SELECT [UniqueNo]
FROM [Sample]
WHERE 1 = 1 AND [UniqueNo] = CAST (@@IDENTITY AS int)

@p0='' (Size = 10) (DbType = String)
@p1='' (Nullable = false) (Size = 4000) (DbType = String)
@p2='00000000-0000-0000-0000-000000000002'

INSERT INTO [Sample] ([MaxLengthProperty], [Name], [RowVersion])
VALUES (@p0, @p1, @p2)",
                Sql);
        }

        public override void StringLengthAttribute_throws_while_inserting_value_longer_than_max_length()
        {
            Fixture.TestSqlLoggerFactory.Clear();
            base.StringLengthAttribute_throws_while_inserting_value_longer_than_max_length();

            Assert.Equal(@"@p0='ValidString' (Size = 16)

INSERT INTO [Two] ([Data])
VALUES (@p0)

@p0='ValidString' (Size = 16)

SELECT [Id], [Timestamp]
FROM [Two]
WHERE 1 = 1 AND [Id] = CAST (@@IDENTITY AS int)

@p0='ValidButLongString'

INSERT INTO [Two] ([Data])
VALUES (@p0)",
                Sql);
        }

        public override void TimestampAttribute_throws_if_value_in_database_changed()
        {
            using (var context = CreateContext())
            {
                context.Database.EnsureDeleted();
                context.Database.EnsureCreated();
                DataAnnotationModelInitializer.Seed(context);

                Assert.True(context.Model.FindEntityType(typeof(Two)).FindProperty("Timestamp").IsConcurrencyToken);
            }

            base.TimestampAttribute_throws_if_value_in_database_changed();

            // Not validating SQL because not significantly different from other tests and 
            // row version value is not stable. 
        }

        private const string FileLineEnding = @"
";
        private  string Sql => Fixture.TestSqlLoggerFactory.Sql.Replace(Environment.NewLine, FileLineEnding);
    }
}
