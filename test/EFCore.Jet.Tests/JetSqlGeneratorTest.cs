using System;
using EntityFrameworkCore.Jet.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace EntityFrameworkCore.Jet.Tests
{
    public class JetSqlGeneratorTest
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
            var literal = new JetTypeMapper(new RelationalTypeMapperDependencies())
                .GetMapping(typeof(byte[])).GenerateSqlLiteral(value);
            Assert.Equal("0xDA7A", literal);
        }

        [Fact]
        public void GenerateLiteral_returns_DateTime_literal()
        {
            var value = new DateTime(1969, 09, 15, 20, 21, 22, 371);
            var literal = new JetTypeMapper(new RelationalTypeMapperDependencies())
                .GetMapping(typeof(DateTime)).GenerateSqlLiteral(value);
            Assert.Equal("#09/15/1969 20:21:22#", literal);
        }

        protected ISqlGenerationHelper CreateSqlGenerator()
            => new JetSqlGenerationHelper(new RelationalSqlGenerationHelperDependencies());
    }
}
