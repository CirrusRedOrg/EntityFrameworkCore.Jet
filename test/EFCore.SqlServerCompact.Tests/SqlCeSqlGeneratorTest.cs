using System;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Tests
{
    public class SqlCeSqlGeneratorTest
    {
        [Fact]
        public void BatchSeparator_returns_seperator()
        {
            Assert.Equal("GO" + Environment.NewLine + Environment.NewLine, CreateSqlGenerator().BatchTerminator);
        }

        [Fact]
        public void GenerateLiteral_returns_ByteArray_literal()
        {
            var value = new byte[] { 0xDA, 0x7A };
            var literal = new SqlCeTypeMapper(new RelationalTypeMapperDependencies())
                .GetMapping(typeof(byte[])).GenerateSqlLiteral(value);
            Assert.Equal("0xDA7A", literal);
        }

        [Fact]
        public void GenerateLiteral_returns_DateTime_literal()
        {
            var value = new DateTime(2015, 3, 12, 13, 36, 37, 371);
            var literal = new SqlCeTypeMapper(new RelationalTypeMapperDependencies())
                .GetMapping(typeof(DateTime)).GenerateSqlLiteral(value);
            Assert.Equal("'2015-03-12T13:36:37.371'", literal);
        }

        protected ISqlGenerationHelper CreateSqlGenerator()
            => new SqlCeSqlGenerationHelper(new RelationalSqlGenerationHelperDependencies());
    }
}
