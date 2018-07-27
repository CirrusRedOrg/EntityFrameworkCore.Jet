using System;
using System.Data;
using System.Data.Common;
using EntityFrameworkCore.Jet.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace EntityFrameworkCore.Jet.Tests
{
    public class JetTypeMapperTest
    {
        [Fact]
        public void Does_simple_Jet_mappings_to_DDL_types()
        {
            Assert.Equal("int", GetTypeMapping(typeof(int)).StoreType);
            Assert.Equal("datetime", GetTypeMapping(typeof(DateTime)).StoreType);
            Assert.Equal("guid", GetTypeMapping(typeof(Guid)).StoreType);
            Assert.Equal("byte", GetTypeMapping(typeof(byte)).StoreType);
            Assert.Equal("double", GetTypeMapping(typeof(double)).StoreType);
            Assert.Equal("smallint", GetTypeMapping(typeof(bool)).StoreType);
            Assert.Equal("smallint", GetTypeMapping(typeof(short)).StoreType);
            Assert.Equal("int", GetTypeMapping(typeof(long)).StoreType);
            Assert.Equal("single", GetTypeMapping(typeof(float)).StoreType);
           
        }

        [Fact(Skip = "Actually there are not unsupported types")]
        public void Breaks_Mapping_To_Unsupported()
        {
            Assert.Throws<InvalidOperationException>(() => GetTypeMapping(typeof(DateTimeOffset)).StoreType);
        }

        [Fact]
        public void Does_simple_Jet_mappings_for_nullable_CLR_types_to_DDL_types()
        {
            Assert.Equal("int", GetTypeMapping(typeof(int?)).StoreType);
            Assert.Equal("datetime", GetTypeMapping(typeof(DateTime?)).StoreType);
            Assert.Equal("guid", GetTypeMapping(typeof(Guid?)).StoreType);
            Assert.Equal("byte", GetTypeMapping(typeof(byte?)).StoreType);
            Assert.Equal("double", GetTypeMapping(typeof(double?)).StoreType);
            Assert.Equal("smallint", GetTypeMapping(typeof(bool?)).StoreType);
            Assert.Equal("smallint", GetTypeMapping(typeof(short?)).StoreType);
            Assert.Equal("int", GetTypeMapping(typeof(long?)).StoreType);
            Assert.Equal("single", GetTypeMapping(typeof(float?)).StoreType);
        }

        [Fact]
        public void Does_simple_Jet_mappings_for_enums_to_DDL_types()
        {
            Assert.Equal("int", GetTypeMapping(typeof(IntEnum)).StoreType);
            Assert.Equal("byte", GetTypeMapping(typeof(ByteEnum)).StoreType);
            Assert.Equal("smallint", GetTypeMapping(typeof(ShortEnum)).StoreType);
            Assert.Equal("int", GetTypeMapping(typeof(LongEnum)).StoreType);
            Assert.Equal("int", GetTypeMapping(typeof(IntEnum?)).StoreType);
            Assert.Equal("byte", GetTypeMapping(typeof(ByteEnum?)).StoreType);
            Assert.Equal("smallint", GetTypeMapping(typeof(ShortEnum?)).StoreType);
            Assert.Equal("int", GetTypeMapping(typeof(LongEnum?)).StoreType);
        }

        [Fact]
        public void Does_simple_Jet_mappings_to_DbTypes()
        {
            Assert.Equal(DbType.Int32, GetTypeMapping(typeof(int)).DbType);
            Assert.Null(GetTypeMapping(typeof(string)).DbType);
            Assert.Equal(DbType.Binary, GetTypeMapping(typeof(byte[])).DbType);
            Assert.Equal(DbType.Guid, GetTypeMapping(typeof(Guid)).DbType);
            Assert.Equal(DbType.Byte, GetTypeMapping(typeof(byte)).DbType);
            Assert.Equal(DbType.Double, GetTypeMapping(typeof(double)).DbType);
            Assert.Equal(DbType.Boolean, GetTypeMapping(typeof(bool)).DbType);
            Assert.Equal(DbType.Int16, GetTypeMapping(typeof(short)).DbType);
            Assert.Equal(DbType.Int64, GetTypeMapping(typeof(long)).DbType);
            Assert.Null(GetTypeMapping(typeof(float)).DbType);
            Assert.Equal(DbType.DateTime, GetTypeMapping(typeof(DateTime)).DbType);
        }

        [Fact]
        public void Does_simple_Jet_mappings_for_nullable_CLR_types_to_DbTypes()
        {
            Assert.Equal(DbType.Int32, GetTypeMapping(typeof(int?)).DbType);
            Assert.Null(GetTypeMapping(typeof(string)).DbType);
            Assert.Equal(DbType.Binary, GetTypeMapping(typeof(byte[])).DbType);
            Assert.Equal(DbType.Guid, GetTypeMapping(typeof(Guid?)).DbType);
            Assert.Equal(DbType.Byte, GetTypeMapping(typeof(byte?)).DbType);
            Assert.Equal(DbType.Double, GetTypeMapping(typeof(double?)).DbType);
            Assert.Equal(DbType.Boolean, GetTypeMapping(typeof(bool?)).DbType);
            Assert.Equal(DbType.Int16, GetTypeMapping(typeof(short?)).DbType);
            Assert.Equal(DbType.Int64, GetTypeMapping(typeof(long?)).DbType);
            Assert.Null(GetTypeMapping(typeof(float?)).DbType);
            Assert.Equal(DbType.DateTime, GetTypeMapping(typeof(DateTime?)).DbType);
        }

        [Fact]
        public void Does_simple_Jet_mappings_for_enums_to_DbTypes()
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
        public void Does_non_key_Jet_string_mapping()
        {
            var typeMapping = GetTypeMapping(typeof(string));

            Assert.Null(typeMapping.DbType);
            Assert.Equal("text", typeMapping.StoreType);
            Assert.Equal(null, typeMapping.Size);
            Assert.True(typeMapping.IsUnicode);
            Assert.Equal(255, typeMapping.CreateParameter(new TestCommand(), "Name", "Value").Size);
        }


        [Fact]
        public void Does_non_key_SQL_Server_string_mapping_with_max_length_with_long_string()
        {
            var typeMapping = GetTypeMapping(typeof(string), null, 3);

            Assert.Null(typeMapping.DbType);
            Assert.Equal("varchar(3)", typeMapping.StoreType);
            Assert.Equal(3, typeMapping.Size);
            Assert.True(typeMapping.IsUnicode);
            Assert.Equal(-1, typeMapping.CreateParameter(new TestCommand(), "Name", new string('X', 4001)).Size);
        }

        [Fact]
        public void Does_non_key_Jet_required_string_mapping()
        {
            var typeMapping = GetTypeMapping(typeof(string), isNullable: false);

            Assert.Null(typeMapping.DbType);
            Assert.Equal("text", typeMapping.StoreType);
            Assert.Equal(null, typeMapping.Size);
            Assert.True(typeMapping.IsUnicode);
            Assert.Equal(255, typeMapping.CreateParameter(new TestCommand(), "Name", "Value").Size);
        }

        public static RelationalTypeMapping GetMapping(Property value)
            => (RelationalTypeMapping)new JetTypeMappingSource(
                    TestServiceFactory.Instance.Create<TypeMappingSourceDependencies>(),
                    TestServiceFactory.Instance.Create<RelationalTypeMappingSourceDependencies>())
                .GetMapping(value);

        public static RelationalTypeMapping GetMapping(Type type)
            => (RelationalTypeMapping)new JetTypeMappingSource(
                    TestServiceFactory.Instance.Create<TypeMappingSourceDependencies>(),
                    TestServiceFactory.Instance.Create<RelationalTypeMappingSourceDependencies>())
                .FindMapping(type);

        public static RelationalTypeMapping GetMappingForValue(object value)
            => (RelationalTypeMapping)new JetTypeMappingSource(
                    TestServiceFactory.Instance.Create<TypeMappingSourceDependencies>(),
                    TestServiceFactory.Instance.Create<RelationalTypeMappingSourceDependencies>())
                .GetMappingForValue(value);


        [Fact]
        public void Does_key_Jet_string_mapping()
        {
            Property property = CreateEntityType().AddProperty("MyProp", typeof(string));
            property.IsNullable = false;
            property.DeclaringEntityType.SetPrimaryKey(property);

            var typeMapping = GetMapping(property);

            Assert.Null(typeMapping.DbType);
            Assert.Equal("varchar(255)", typeMapping.StoreType);
            Assert.Equal(255, typeMapping.Size);
            Assert.True(typeMapping.IsUnicode);
            Assert.Equal(255, typeMapping.CreateParameter(new TestCommand(), "Name", "Value").Size);
        }

        [Fact]
        public void Does_foreign_key_Jet_string_mapping()
        {
            var property = CreateEntityType().AddProperty("MyProp", typeof(string));
            property.IsNullable = false;
            var fkProperty = property.DeclaringEntityType.AddProperty("FK", typeof(string));
            var pk = property.DeclaringEntityType.SetPrimaryKey(property);
            property.DeclaringEntityType.AddForeignKey(fkProperty, pk, property.DeclaringEntityType);

            var typeMapping = GetMapping(fkProperty);

            Assert.Null(typeMapping.DbType);
            Assert.Equal("varchar(255)", typeMapping.StoreType);
            Assert.Equal(255, typeMapping.Size);
            Assert.True(typeMapping.IsUnicode);
            Assert.Equal(255, typeMapping.CreateParameter(new TestCommand(), "Name", "Value").Size);
        }

        [Fact]
        public void Does_required_foreign_key_Jet_string_mapping()
        {
            var property = CreateEntityType().AddProperty("MyProp", typeof(string));
            property.IsNullable = false;
            var fkProperty = property.DeclaringEntityType.AddProperty("FK", typeof(string));
            var pk = property.DeclaringEntityType.SetPrimaryKey(property);
            property.DeclaringEntityType.AddForeignKey(fkProperty, pk, property.DeclaringEntityType);
            fkProperty.IsNullable = false;

            var typeMapping = GetMapping(fkProperty);

            Assert.Null(typeMapping.DbType);
            Assert.Equal("varchar(255)", typeMapping.StoreType);
            Assert.Equal(255, typeMapping.Size);
            Assert.True(typeMapping.IsUnicode);
            Assert.Equal(255, typeMapping.CreateParameter(new TestCommand(), "Name", "Value").Size);
        }

        [Fact]
        public void Does_indexed_column_Jet_string_mapping()
        {
            var entityType = CreateEntityType();
            var property = entityType.AddProperty("MyProp", typeof(string));
            entityType.AddIndex(property);

            var typeMapping = GetMapping(property);

            Assert.Null(typeMapping.DbType);
            Assert.Equal("varchar(255)", typeMapping.StoreType);
            Assert.Equal(255, typeMapping.Size);
            Assert.True(typeMapping.IsUnicode);
            Assert.Equal(255, typeMapping.CreateParameter(new TestCommand(), "Name", "Value").Size);
        }

        [Fact]
        public void Does_non_key_Jet_binary_mapping()
        {
            var typeMapping = GetTypeMapping(typeof(byte[]));

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("image", typeMapping.StoreType);
            Assert.Equal(null, typeMapping.Size);
            Assert.Equal(510, typeMapping.CreateParameter(new TestCommand(), "Name", new byte[3]).Size);
        }

        [Fact]
        public void Does_non_key_Jet_required_binary_mapping()
        {
            var typeMapping = GetTypeMapping(typeof(byte[]), isNullable: false);

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("image", typeMapping.StoreType);
            Assert.Equal(null, typeMapping.Size);
            Assert.Equal(510, typeMapping.CreateParameter(new TestCommand(), "Name", new byte[3]).Size);
        }

        [Fact]
        public void Does_key_Jet_binary_mapping()
        {
            var property = CreateEntityType().AddProperty("MyProp", typeof(byte[]));
            property.IsNullable = false;
            property.DeclaringEntityType.SetPrimaryKey(property);

            var typeMapping = GetMapping(property);

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("varbinary(510)", typeMapping.StoreType);
            Assert.Equal(510, typeMapping.CreateParameter(new TestCommand(), "Name", new byte[3]).Size);
        }

        [Fact]
        public void Does_foreign_key_Jet_binary_mapping()
        {
            var property = CreateEntityType().AddProperty("MyProp", typeof(byte[]));
            property.IsNullable = false;
            var fkProperty = property.DeclaringEntityType.AddProperty("FK", typeof(byte[]));
            var pk = property.DeclaringEntityType.SetPrimaryKey(property);
            property.DeclaringEntityType.AddForeignKey(fkProperty, pk, property.DeclaringEntityType);

            var typeMapping = GetMapping(fkProperty);

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("varbinary(510)", typeMapping.StoreType);
            Assert.Equal(510, typeMapping.CreateParameter(new TestCommand(), "Name", new byte[3]).Size);
        }

        [Fact]
        public void Does_required_foreign_key_Jet_binary_mapping()
        {
            var property = CreateEntityType().AddProperty("MyProp", typeof(byte[]));
            property.IsNullable = false;
            var fkProperty = property.DeclaringEntityType.AddProperty("FK", typeof(byte[]));
            var pk = property.DeclaringEntityType.SetPrimaryKey(property);
            property.DeclaringEntityType.AddForeignKey(fkProperty, pk, property.DeclaringEntityType);
            fkProperty.IsNullable = false;

            var typeMapping = GetMapping(fkProperty);

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("varbinary(510)", typeMapping.StoreType);
            Assert.Equal(510, typeMapping.CreateParameter(new TestCommand(), "Name", new byte[3]).Size);
        }

        [Fact]
        public void Does_indexed_column_Jet_binary_mapping()
        {
            var entityType = CreateEntityType();
            var property = entityType.AddProperty("MyProp", typeof(byte[]));
            entityType.AddIndex(property);

            var typeMapping = GetMapping(property);

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("varbinary(510)", typeMapping.StoreType);
            Assert.Equal(510, typeMapping.CreateParameter(new TestCommand(), "Name", new byte[3]).Size);
        }

        [Fact]
        public void Does_non_key_Jet_rowversion_mapping()
        {
            var property = CreateEntityType().AddProperty("MyProp", typeof(byte[]));
            property.IsConcurrencyToken = true;
            property.ValueGenerated = ValueGenerated.OnAddOrUpdate;

            var typeMapping = GetMapping(property);

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("varbinary(8)", typeMapping.StoreType);
            Assert.Equal(8, typeMapping.Size);
            Assert.Equal(8, typeMapping.CreateParameter(new TestCommand(), "Name", new byte[8]).Size);
        }

        [Fact]
        public void Does_non_key_Jet_required_rowversion_mapping()
        {
            var property = CreateEntityType().AddProperty("MyProp", typeof(byte[]));
            property.IsConcurrencyToken = true;
            property.ValueGenerated = ValueGenerated.OnAddOrUpdate;
            property.IsNullable = false;

            var typeMapping = GetMapping(property);

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("varbinary(8)", typeMapping.StoreType);
            Assert.Equal(8, typeMapping.Size);
            Assert.Equal(8, typeMapping.CreateParameter(new TestCommand(), "Name", new byte[8]).Size);
        }

        [Fact]
        public void Does_not_do_rowversion_mapping_for_non_computed_concurrency_tokens()
        {
            var property = CreateEntityType().AddProperty("MyProp", typeof(byte[]));
            property.IsConcurrencyToken = true;

            var typeMapping = (JetByteArrayTypeMapping)GetMapping(property);

            Assert.Equal(DbType.Binary, typeMapping.DbType);
            Assert.Equal("image", typeMapping.StoreType);
        }

        private static RelationalTypeMapping GetTypeMapping(Type propertyType, bool? isNullable = null, int? maxLength = null)
        {
            var property = CreateEntityType().AddProperty("MyProp", propertyType);

            if (isNullable.HasValue)
                property.IsNullable = isNullable.Value;

            if (maxLength.HasValue)
                property.SetMaxLength(maxLength);

            return GetMapping(property);
        }

        private static EntityType CreateEntityType() => new Model().AddEntityType("MyType");

        [Fact]
        public void Does_default_mappings_for_sequence_types()
        {
            Assert.Equal("int", GetMapping(typeof(int)).StoreType);
            Assert.Equal("smallint", GetMapping(typeof(short)).StoreType);
            Assert.Equal("int", GetMapping(typeof(long)).StoreType);
            Assert.Equal("byte", GetMapping(typeof(byte)).StoreType);
        }

        [Fact]
        public void Does_default_mappings_for_strings_and_byte_arrays()
        {
            Assert.Equal("text", GetMapping(typeof(string)).StoreType);
            Assert.Equal("image", GetMapping(typeof(byte[])).StoreType);
        }

        [Fact]
        public void Does_default_mappings_for_values()
        {
            Assert.Equal("text", GetMappingForValue("Cheese").StoreType);
            Assert.Equal("image", GetMappingForValue(new byte[1]).StoreType);
            Assert.Equal("datetime", GetMappingForValue(new DateTime()).StoreType);
        }

        [Fact]
        public void Does_default_mappings_for_null_values()
        {
            Assert.Equal("NULL", GetMappingForValue(null).StoreType);
            Assert.Equal("NULL", GetMappingForValue(DBNull.Value).StoreType);
            // Now no TypeMapper no party
            //Assert.Equal("NULL", RelationalTypeMapperExtensions.GetMappingForValue(null, "Itz").StoreType);
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
