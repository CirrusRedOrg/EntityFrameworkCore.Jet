using System;
using Microsoft.EntityFrameworkCore.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Xunit;

namespace Microsoft.EntityFrameworkCore
{
    public class ScaffoldingTypeMapperSqlCeTest
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Maps_int_column(bool isKeyOrIndex)
        {
            var mapping = CreateMapper().FindMapping("int", isKeyOrIndex, rowVersion: false);

            AssertMapping<int>(mapping, inferred: true, maxLength: null, unicode: null);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Maps_bigint_column(bool isKeyOrIndex)
        {
            var mapping = CreateMapper().FindMapping("bigint", isKeyOrIndex, rowVersion: false);

            AssertMapping<long>(mapping, inferred: true, maxLength: null, unicode: null);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Maps_bit_column(bool isKeyOrIndex)
        {
            var mapping = CreateMapper().FindMapping("bit", isKeyOrIndex, rowVersion: false);

            AssertMapping<bool>(mapping, inferred: true, maxLength: null, unicode: null);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Maps_datetime_column(bool isKeyOrIndex)
        {
            var mapping = CreateMapper().FindMapping("datetime", isKeyOrIndex, rowVersion: false);

            AssertMapping<DateTime>(mapping, inferred: true, maxLength: null, unicode: null);
        }

        [Fact]
        public void Maps_normal_varbinary_max_column()
        {
            var mapping = CreateMapper().FindMapping("image", keyOrIndex: false, rowVersion: false);

            AssertMapping<byte[]>(mapping, inferred: false, maxLength: null, unicode: null);
        }

        [Fact]
        public void Maps_normal_varbinary_sized_column()
        {
            var mapping = CreateMapper().FindMapping("varbinary(200)", keyOrIndex: false, rowVersion: false);

            AssertMapping<byte[]>(mapping, inferred: true, maxLength: 200, unicode: null);
        }

        [Fact]
        public void Maps_normal_binary_max_column()
        {
            var mapping = CreateMapper().FindMapping("binary(8000)", keyOrIndex: false, rowVersion: false);

            AssertMapping<byte[]>(mapping, inferred: false, maxLength: null, unicode: null);
        }

        [Fact]
        public void Maps_normal_binary_sized_column()
        {
            var mapping = CreateMapper().FindMapping("binary(200)", keyOrIndex: false, rowVersion: false);

            AssertMapping<byte[]>(mapping, inferred: false, maxLength: null, unicode: null);
        }

        [Fact]
        public void Maps_key_varbinary_max_column()
        {
            var mapping = CreateMapper().FindMapping("image", keyOrIndex: true, rowVersion: false);

            AssertMapping<byte[]>(mapping, inferred: false, maxLength: null, unicode: null);
        }

        [Fact]
        public void Maps_key_varbinary_sized_column()
        {
            var mapping = CreateMapper().FindMapping("varbinary(200)", keyOrIndex: true, rowVersion: false);

            AssertMapping<byte[]>(mapping, inferred: true, maxLength: 200, unicode: null);
        }

        [Fact]
        public void Maps_key_varbinary_default_sized_column()
        {
            var mapping = CreateMapper().FindMapping("varbinary(512)", keyOrIndex: true, rowVersion: false);

            AssertMapping<byte[]>(mapping, inferred: true, maxLength: null, unicode: null);
        }

        [Fact]
        public void Maps_key_binary_max_column()
        {
            var mapping = CreateMapper().FindMapping("binary(8000)", keyOrIndex: true, rowVersion: false);

            AssertMapping<byte[]>(mapping, inferred: false, maxLength: null, unicode: null);
        }

        [Fact]
        public void Maps_key_binary_sized_column()
        {
            var mapping = CreateMapper().FindMapping("binary(200)", keyOrIndex: true, rowVersion: false);

            AssertMapping<byte[]>(mapping, inferred: false, maxLength: null, unicode: null);
        }

        [Fact]
        public void Maps_key_binary_default_sized_column()
        {
            var mapping = CreateMapper().FindMapping("binary(512)", keyOrIndex: true, rowVersion: false);

            AssertMapping<byte[]>(mapping, inferred: false, maxLength: null, unicode: null);
        }

        [Fact]
        public void Maps_rowversion_rowversion_column()
        {
            var mapping = CreateMapper().FindMapping("rowversion", keyOrIndex: false, rowVersion: true);

            AssertMapping<byte[]>(mapping, inferred: true, maxLength: null, unicode: null);
        }

        [Fact]
        public void Maps_normal_nvarchar_max_column()
        {
            var mapping = CreateMapper().FindMapping("nvarchar(4000)", keyOrIndex: false, rowVersion: false);

            AssertMapping<string>(mapping, inferred: true, maxLength: null, unicode: null);
        }

        [Fact]
        public void Maps_normal_nvarchar_sized_column()
        {
            var mapping = CreateMapper().FindMapping("nvarchar(200)", keyOrIndex: false, rowVersion: false);

            AssertMapping<string>(mapping, inferred: true, maxLength: 200, unicode: null);
        }

        [Fact]
        public void Maps_key_nvarchar_max_column()
        {
            var mapping = CreateMapper().FindMapping("ntext", keyOrIndex: true, rowVersion: false);

            AssertMapping<string>(mapping, inferred: false, maxLength: null, unicode: null);
        }

        [Fact]
        public void Maps_key_nvarchar_sized_column()
        {
            var mapping = CreateMapper().FindMapping("nvarchar(200)", keyOrIndex: true, rowVersion: false);

            AssertMapping<string>(mapping, inferred: true, maxLength: 200, unicode: null);
        }

        [Fact]
        public void Maps_key_nvarchar_default_sized_column()
        {
            var mapping = CreateMapper().FindMapping("nvarchar(256)", keyOrIndex: true, rowVersion: false);

            AssertMapping<string>(mapping, inferred: true, maxLength: null, unicode: null);
        }

        private static void AssertMapping<T>(TypeScaffoldingInfo mapping, bool inferred, int? maxLength, bool? unicode)
        {
            Assert.Same(typeof(T), mapping.ClrType);
            Assert.Equal(inferred, mapping.IsInferred);
            Assert.Equal(maxLength, mapping.ScaffoldMaxLength);
            Assert.Equal(unicode, mapping.ScaffoldUnicode);
        }

        private static ScaffoldingTypeMapper CreateMapper()
            => new ScaffoldingTypeMapper(
                new SqlCeTypeMapper(new RelationalTypeMapperDependencies()));
    }
}