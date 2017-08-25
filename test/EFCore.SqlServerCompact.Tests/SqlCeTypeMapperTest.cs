using System;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Tests
{
    public class SqlCeTypeMapperTest
    {
        [Fact]
        public void Does_simple_SQL_Server_mappings_to_DDL_types()
        {
            Assert.Equal("int", GetTypeMapping(typeof(int)).StoreType);
            Assert.Equal("datetime", GetTypeMapping(typeof(DateTime)).StoreType);
            Assert.Equal("uniqueidentifier", GetTypeMapping(typeof(Guid)).StoreType);
            Assert.Equal("tinyint", GetTypeMapping(typeof(byte)).StoreType);
            Assert.Equal("float", GetTypeMapping(typeof(double)).StoreType);
            Assert.Equal("bit", GetTypeMapping(typeof(bool)).StoreType);
            Assert.Equal("smallint", GetTypeMapping(typeof(short)).StoreType);
            Assert.Equal("bigint", GetTypeMapping(typeof(long)).StoreType);
            Assert.Equal("real", GetTypeMapping(typeof(float)).StoreType);
           
        }

        [Fact]
        public void Breaks_Mapping_To_Unsupported()
        {
            Assert.Throws<InvalidOperationException>(() => GetTypeMapping(typeof(DateTimeOffset)).StoreType);
            Assert.Throws<InvalidOperationException>(() => GetTypeMapping(typeof(TimeSpan)).StoreType);
        }

        [Fact]
        public void Does_simple_SQL_Server_mappings_for_nullable_CLR_types_to_DDL_types()
        {
            Assert.Equal("int", GetTypeMapping(typeof(int?)).StoreType);
            Assert.Equal("datetime", GetTypeMapping(typeof(DateTime?)).StoreType);
            Assert.Equal("uniqueidentifier", GetTypeMapping(typeof(Guid?)).StoreType);
            Assert.Equal("tinyint", GetTypeMapping(typeof(byte?)).StoreType);
            Assert.Equal("float", GetTypeMapping(typeof(double?)).StoreType);
            Assert.Equal("bit", GetTypeMapping(typeof(bool?)).StoreType);
            Assert.Equal("smallint", GetTypeMapping(typeof(short?)).StoreType);
            Assert.Equal("bigint", GetTypeMapping(typeof(long?)).StoreType);
            Assert.Equal("real", GetTypeMapping(typeof(float?)).StoreType);
        }

        [Fact]
        public void Does_simple_SQL_Server_mappings_for_enums_to_DDL_types()
        {
            Assert.Equal("int", GetTypeMapping(typeof(IntEnum)).StoreType);
            Assert.Equal("tinyint", GetTypeMapping(typeof(ByteEnum)).StoreType);
            Assert.Equal("smallint", GetTypeMapping(typeof(ShortEnum)).StoreType);
            Assert.Equal("bigint", GetTypeMapping(typeof(LongEnum)).StoreType);
            Assert.Equal("int", GetTypeMapping(typeof(IntEnum?)).StoreType);
            Assert.Equal("tinyint", GetTypeMapping(typeof(ByteEnum?)).StoreType);
            Assert.Equal("smallint", GetTypeMapping(typeof(ShortEnum?)).StoreType);
            Assert.Equal("bigint", GetTypeMapping(typeof(LongEnum?)).StoreType);
        }

        [Fact]
        public void Does_simple_SQL_Server_mappings_to_DbTypes()
        {
            Assert.Equal(DbType.Int32, GetTypeMapping(typeof(int)).DbType);
            Assert.Equal(DbType.String, GetTypeMapping(typeof(string)).DbType);
            Assert.Equal(DbType.Binary, GetTypeMapping(typeof(byte[])).DbType);
            Assert.Equal(DbType.Guid, GetTypeMapping(typeof(Guid)).DbType);
            Assert.Equal(DbType.Byte, GetTypeMapping(typeof(byte)).DbType);
            Assert.Equal(DbType.Double, GetTypeMapping(typeof(double)).DbType);
            Assert.Equal(DbType.Boolean, GetTypeMapping(typeof(bool)).DbType);
            Assert.Equal(DbType.Int16, GetTypeMapping(typeof(short)).DbType);
            Assert.Equal(DbType.Int64, GetTypeMapping(typeof(long)).DbType);
            Assert.Equal(DbType.Single, GetTypeMapping(typeof(float)).DbType);
            Assert.Equal(DbType.DateTime, GetTypeMapping(typeof(DateTime)).DbType);
        }

        [Fact]
        public void Does_simple_SQL_Server_mappings_for_nullable_CLR_types_to_DbTypes()
        {
            Assert.Equal(DbType.Int32, GetTypeMapping(typeof(int?)).DbType);
            Assert.Equal(DbType.String, GetTypeMapping(typeof(string)).DbType);
            Assert.Equal(DbType.Binary, GetTypeMapping(typeof(byte[])).DbType);
            Assert.Equal(DbType.Guid, GetTypeMapping(typeof(Guid?)).DbType);
            Assert.Equal(DbType.Byte, GetTypeMapping(typeof(byte?)).DbType);
            Assert.Equal(DbType.Double, GetTypeMapping(typeof(double?)).DbType);
            Assert.Equal(DbType.Boolean, GetTypeMapping(typeof(bool?)).DbType);
            Assert.Equal(DbType.Int16, GetTypeMapping(typeof(short?)).DbType);
            Assert.Equal(DbType.Int64, GetTypeMapping(typeof(long?)).DbType);
            Assert.Equal(DbType.Single, GetTypeMapping(typeof(float?)).DbType);
            Assert.Equal(DbType.DateTime, GetTypeMapping(typeof(DateTime?)).DbType);
        }

        [Fact]
        public void Does_simple_SQL_Server_mappings_for_enums_to_DbTypes()
        {
            Assert.Equal(DbType.Int32, GetTypeMapping(typeof(IntEnum)).DbType);
            Assert.Equal(DbType.Byte, GetTypeMapping(typeof(ByteEnum)).DbType);
            Assert.Equal(DbType.Int16, GetTypeMapping(typeof(ShortEnum)).DbType);
            Assert.Equal(DbType.Int64, GetTypeMapping(typeof(LongEnum)).DbType);
            Assert.Equal(DbType.Int32, GetTypeMapping(typeof(IntEnum?)).DbType);
            Assert.Equal(DbType.Byte, GetTypeMapping(typeof(ByteEnum?)).DbType);
            Assert.Equal(DbType.Int16, GetTypeMapping(typeof(ShortEnum?)).DbType);
            Assert.Equal(DbType.Int64, GetTypeMapping(typeof(LongEnum?)).DbType);
        }

        [Fact]
        public void Does_decimal_mapping()
        {
            var typeMapping = GetTypeMapping(typeof(decimal));

            Assert.Equal(DbType.Decimal, typeMapping.DbType);
            Assert.Equal("decimal(18, 2)", typeMapping.StoreType);
        }

        [Fact]
        public void Does_decimal_mapping_for_nullable_CLR_types()
        {
            var typeMapping = GetTypeMapping(typeof(decimal?));

            Assert.Equal(DbType.Decimal, typeMapping.DbType);
            Assert.Equal("decimal(18, 2)", typeMapping.StoreType);
        }

        [Fact]
        public void Does_non_key_SQL_Server_string_mapping()
        {
            var typeMapping = GetTypeMapping(typeof(string));

            Assert.Equal(DbType.String, typeMapping.DbType);
            Assert.Equal("nvarchar(4000)", typeMapping.StoreType);
            Assert.Equal(4000, typeMapping.Size);
            Assert.True(typeMapping.IsUnicode);
            Assert.Equal(4000, typeMapping.CreateParameter(new TestCommand(), "Name", "Value").Size);
        }

        [Fact]
        public void Does_non_key_SQL_Server_required_string_mapping()
        {
            var typeMapping = GetTypeMapping(typeof(string), isNullable: false);

            Assert.Equal(DbType.String, typeMapping.DbType);
            Assert.Equal("nvarchar(4000)", typeMapping.StoreType);
            Assert.Equal(4000, typeMapping.Size);
            Assert.True(typeMapping.IsUnicode);
            Assert.Equal(4000, typeMapping.CreateParameter(new TestCommand(), "Name", "Value").Size);
        }

        [Fact]
        public void Does_key_SQL_Server_string_mapping()
        {
            var property = CreateEntityType().AddProperty("MyProp", typeof(string));
            property.IsNullable = false;
            property.DeclaringEntityType.SetPrimaryKey(property);

            var typeMapping = new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(property);

            Assert.Equal(DbType.String, typeMapping.DbType);
            Assert.Equal("nvarchar(256)", typeMapping.StoreType);
            Assert.Equal(256, typeMapping.Size);
            Assert.True(typeMapping.IsUnicode);
            Assert.Equal(256, typeMapping.CreateParameter(new TestCommand(), "Name", "Value").Size);
        }

        [Fact]
        public void Does_foreign_key_SQL_Server_string_mapping()
        {
            var property = CreateEntityType().AddProperty("MyProp", typeof(string));
            property.IsNullable = false;
            var fkProperty = property.DeclaringEntityType.AddProperty("FK", typeof(string));
            var pk = property.DeclaringEntityType.SetPrimaryKey(property);
            property.DeclaringEntityType.AddForeignKey(fkProperty, pk, property.DeclaringEntityType);

            var typeMapping = new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(fkProperty);

            Assert.Equal(DbType.String, typeMapping.DbType);
            Assert.Equal("nvarchar(256)", typeMapping.StoreType);
            Assert.Equal(256, typeMapping.Size);
            Assert.True(typeMapping.IsUnicode);
            Assert.Equal(256, typeMapping.CreateParameter(new TestCommand(), "Name", "Value").Size);
        }

        [Fact]
        public void Does_required_foreign_key_SQL_Server_string_mapping()
        {
            var property = CreateEntityType().AddProperty("MyProp", typeof(string));
            property.IsNullable = false;
            var fkProperty = property.DeclaringEntityType.AddProperty("FK", typeof(string));
            var pk = property.DeclaringEntityType.SetPrimaryKey(property);
            property.DeclaringEntityType.AddForeignKey(fkProperty, pk, property.DeclaringEntityType);
            fkProperty.IsNullable = false;

            var typeMapping = new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(fkProperty);

            Assert.Equal(DbType.String, typeMapping.DbType);
            Assert.Equal("nvarchar(256)", typeMapping.StoreType);
            Assert.Equal(256, typeMapping.Size);
            Assert.True(typeMapping.IsUnicode);
            Assert.Equal(256, typeMapping.CreateParameter(new TestCommand(), "Name", "Value").Size);
        }

        [Fact]
        public void Does_indexed_column_SQL_Server_string_mapping()
        {
            var entityType = CreateEntityType();
            var property = entityType.AddProperty("MyProp", typeof(string));
            entityType.AddIndex(property);

            var typeMapping = new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(property);

            Assert.Equal(DbType.String, typeMapping.DbType);
            Assert.Equal("nvarchar(256)", typeMapping.StoreType);
            Assert.Equal(256, typeMapping.Size);
            Assert.True(typeMapping.IsUnicode);
            Assert.Equal(256, typeMapping.CreateParameter(new TestCommand(), "Name", "Value").Size);
        }

        [Fact]
        public void Does_non_key_SQL_Server_binary_mapping()
        {
            var typeMapping = GetTypeMapping(typeof(byte[]));

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("varbinary(8000)", typeMapping.StoreType);
            Assert.Equal(8000, typeMapping.Size);
            Assert.Equal(8000, typeMapping.CreateParameter(new TestCommand(), "Name", new byte[3]).Size);
        }

        [Fact]
        public void Does_non_key_SQL_Server_required_binary_mapping()
        {
            var typeMapping = GetTypeMapping(typeof(byte[]), isNullable: false);

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("varbinary(8000)", typeMapping.StoreType);
            Assert.Equal(8000, typeMapping.Size);
            Assert.Equal(8000, typeMapping.CreateParameter(new TestCommand(), "Name", new byte[3]).Size);
        }

        [Fact]
        public void Does_key_SQL_Server_binary_mapping()
        {
            var property = CreateEntityType().AddProperty("MyProp", typeof(byte[]));
            property.IsNullable = false;
            property.DeclaringEntityType.SetPrimaryKey(property);

            var typeMapping = new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(property);

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("varbinary(512)", typeMapping.StoreType);
            Assert.Equal(512, typeMapping.CreateParameter(new TestCommand(), "Name", new byte[3]).Size);
        }

        [Fact]
        public void Does_foreign_key_SQL_Server_binary_mapping()
        {
            var property = CreateEntityType().AddProperty("MyProp", typeof(byte[]));
            property.IsNullable = false;
            var fkProperty = property.DeclaringEntityType.AddProperty("FK", typeof(byte[]));
            var pk = property.DeclaringEntityType.SetPrimaryKey(property);
            property.DeclaringEntityType.AddForeignKey(fkProperty, pk, property.DeclaringEntityType);

            var typeMapping = new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(fkProperty);

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("varbinary(512)", typeMapping.StoreType);
            Assert.Equal(512, typeMapping.CreateParameter(new TestCommand(), "Name", new byte[3]).Size);
        }

        [Fact]
        public void Does_required_foreign_key_SQL_Server_binary_mapping()
        {
            var property = CreateEntityType().AddProperty("MyProp", typeof(byte[]));
            property.IsNullable = false;
            var fkProperty = property.DeclaringEntityType.AddProperty("FK", typeof(byte[]));
            var pk = property.DeclaringEntityType.SetPrimaryKey(property);
            property.DeclaringEntityType.AddForeignKey(fkProperty, pk, property.DeclaringEntityType);
            fkProperty.IsNullable = false;

            var typeMapping = new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(fkProperty);

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("varbinary(512)", typeMapping.StoreType);
            Assert.Equal(512, typeMapping.CreateParameter(new TestCommand(), "Name", new byte[3]).Size);
        }

        [Fact]
        public void Does_indexed_column_SQL_Server_binary_mapping()
        {
            var entityType = CreateEntityType();
            var property = entityType.AddProperty("MyProp", typeof(byte[]));
            entityType.AddIndex(property);

            var typeMapping = new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(property);

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("varbinary(512)", typeMapping.StoreType);
            Assert.Equal(512, typeMapping.CreateParameter(new TestCommand(), "Name", new byte[3]).Size);
        }

        [Fact]
        public void Does_non_key_SQL_Server_rowversion_mapping()
        {
            var property = CreateEntityType().AddProperty("MyProp", typeof(byte[]));
            property.IsConcurrencyToken = true;
            property.ValueGenerated = ValueGenerated.OnAddOrUpdate;

            var typeMapping = new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(property);

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("rowversion", typeMapping.StoreType);
            Assert.Equal(8, typeMapping.Size);
            Assert.Equal(8, typeMapping.CreateParameter(new TestCommand(), "Name", new byte[8]).Size);
        }

        [Fact]
        public void Does_non_key_SQL_Server_required_rowversion_mapping()
        {
            var property = CreateEntityType().AddProperty("MyProp", typeof(byte[]));
            property.IsConcurrencyToken = true;
            property.ValueGenerated = ValueGenerated.OnAddOrUpdate;
            property.IsNullable = false;

            var typeMapping = new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(property);

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("rowversion", typeMapping.StoreType);
            Assert.Equal(8, typeMapping.Size);
            Assert.Equal(8, typeMapping.CreateParameter(new TestCommand(), "Name", new byte[8]).Size);
        }

        [Fact]
        public void Does_not_do_rowversion_mapping_for_non_computed_concurrency_tokens()
        {
            var property = CreateEntityType().AddProperty("MyProp", typeof(byte[]));
            property.IsConcurrencyToken = true;

            var typeMapping = (SqlCeByteArrayTypeMapping)new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(property);

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("varbinary(8000)", typeMapping.StoreType);
        }

        private static RelationalTypeMapping GetTypeMapping(Type propertyType, bool? isNullable = null)
        {
            var property = CreateEntityType().AddProperty("MyProp", propertyType);

            if (isNullable.HasValue)
            {
                property.IsNullable = isNullable.Value;
            }

            return new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(property);
        }

        private static EntityType CreateEntityType() => new Model().AddEntityType("MyType");

        [Fact]
        public void Does_default_mappings_for_sequence_types()
        {
            Assert.Equal("int", new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(typeof(int)).StoreType);
            Assert.Equal("smallint", new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(typeof(short)).StoreType);
            Assert.Equal("bigint", new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(typeof(long)).StoreType);
            Assert.Equal("tinyint", new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(typeof(byte)).StoreType);
        }

        [Fact]
        public void Does_default_mappings_for_strings_and_byte_arrays()
        {
            Assert.Equal("nvarchar", new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(typeof(string)).StoreType);
            Assert.Equal("image", new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMapping(typeof(byte[])).StoreType);
        }

        [Fact]
        public void Does_default_mappings_for_values()
        {
            Assert.Equal("nvarchar", new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMappingForValue("Cheese").StoreType);
            Assert.Equal("image", new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMappingForValue(new byte[1]).StoreType);
            Assert.Equal("datetime", new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMappingForValue(new DateTime()).StoreType);
        }

        [Fact]
        public void Does_default_mappings_for_null_values()
        {
            Assert.Equal("NULL", new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMappingForValue((object)null).StoreType);
            Assert.Equal("NULL", new SqlCeTypeMapper(new RelationalTypeMapperDependencies()).GetMappingForValue(DBNull.Value).StoreType);
            Assert.Equal("NULL", RelationalTypeMapperExtensions.GetMappingForValue(null, "Itz").StoreType);
        }

        private enum LongEnum : long
        {
        }

        private enum IntEnum
        {
        }

        private enum ShortEnum : short
        {
        }

        private enum ByteEnum : byte
        {
        }

        private enum ULongEnum : ulong
        {
        }

        private enum UIntEnum : uint
        {
        }

        private enum UShortEnum : ushort
        {
        }

        private enum SByteEnum : sbyte
        {
        }

        private class TestParameter : DbParameter
        {
            public override void ResetDbType()
            {
            }

            public override DbType DbType { get; set; }
            public override ParameterDirection Direction { get; set; }
            public override bool IsNullable { get; set; }
            public override string ParameterName { get; set; }
            public override string SourceColumn { get; set; }
            public override DataRowVersion SourceVersion { get; set; }
            public override object Value { get; set; }
            public override bool SourceColumnNullMapping { get; set; }
            public override int Size { get; set; }
        }

        private class TestCommand : DbCommand
        {
            public override void Prepare()
            {
            }

            public override string CommandText { get; set; }
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection DbConnection { get; set; }
            protected override DbParameterCollection DbParameterCollection { get; }
            protected override DbTransaction DbTransaction { get; set; }
            public override bool DesignTimeVisible { get; set; }

            public override void Cancel()
            {
            }

            protected override DbParameter CreateDbParameter()
            {
                return new TestParameter();
            }

            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            {
                throw new NotImplementedException();
            }

            public override int ExecuteNonQuery()
            {
                throw new NotImplementedException();
            }

            public override object ExecuteScalar()
            {
                throw new NotImplementedException();
            }
        }
    }
}
