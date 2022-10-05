// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public abstract class FindJetTest : FindTestBase<FindJetTest.FindJetFixture>
    {
        protected FindJetTest(FindJetFixture fixture)
            : base(fixture)
        {
            fixture.TestSqlLoggerFactory.Clear();
        }

        public class FindJetTestSet : FindJetTest
        {
            public FindJetTestSet(FindJetFixture fixture)
                : base(fixture)
            {
            }

            protected override TEntity Find<TEntity>(DbContext context, params object[] keyValues)
                => context.Set<TEntity>().Find(keyValues);

            protected override ValueTask<TEntity> FindAsync<TEntity>(DbContext context, params object[] keyValues)
                => context.Set<TEntity>().FindAsync(keyValues);
        }

        public class FindJetTestContext : FindJetTest
        {
            public FindJetTestContext(FindJetFixture fixture)
                : base(fixture)
            {
            }

            protected override TEntity Find<TEntity>(DbContext context, params object[] keyValues)
                => context.Find<TEntity>(keyValues);

            protected override ValueTask<TEntity> FindAsync<TEntity>(DbContext context, params object[] keyValues)
                => context.FindAsync<TEntity>(keyValues);
        }

        public class FindJetTestNonGeneric : FindJetTest
        {
            public FindJetTestNonGeneric(FindJetFixture fixture)
                : base(fixture)
            {
            }

            protected override TEntity Find<TEntity>(DbContext context, params object[] keyValues)
                => (TEntity)context.Find(typeof(TEntity), keyValues);

            protected override async ValueTask<TEntity> FindAsync<TEntity>(DbContext context, params object[] keyValues)
                => (TEntity)await context.FindAsync(typeof(TEntity), keyValues);
        }

        public override void Find_int_key_tracked()
        {
            base.Find_int_key_tracked();

            Assert.Equal("", Sql);
        }

        public override void Find_int_key_from_store()
        {
            base.Find_int_key_from_store();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='77'")}

SELECT TOP 1 `i`.`Id`, `i`.`Foo`
FROM `IntKey` AS `i`
WHERE `i`.`Id` = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override void Returns_null_for_int_key_not_in_store()
        {
            base.Returns_null_for_int_key_not_in_store();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='99'")}

SELECT TOP 1 `i`.`Id`, `i`.`Foo`
FROM `IntKey` AS `i`
WHERE `i`.`Id` = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override void Find_nullable_int_key_tracked()
        {
            base.Find_int_key_tracked();

            Assert.Equal("", Sql);
        }

        public override void Find_nullable_int_key_from_store()
        {
            base.Find_int_key_from_store();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='77'")}

SELECT TOP 1 `i`.`Id`, `i`.`Foo`
FROM `IntKey` AS `i`
WHERE `i`.`Id` = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override void Returns_null_for_nullable_int_key_not_in_store()
        {
            base.Returns_null_for_int_key_not_in_store();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='99'")}

SELECT TOP 1 `i`.`Id`, `i`.`Foo`
FROM `IntKey` AS `i`
WHERE `i`.`Id` = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override void Find_string_key_tracked()
        {
            base.Find_string_key_tracked();

            Assert.Equal("", Sql);
        }

        public override void Find_string_key_from_store()
        {
            base.Find_string_key_from_store();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='Cat' (Size = 255)")}

SELECT TOP 1 `s`.`Id`, `s`.`Foo`
FROM `StringKey` AS `s`
WHERE `s`.`Id` = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override void Returns_null_for_string_key_not_in_store()
        {
            base.Returns_null_for_string_key_not_in_store();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='Fox' (Size = 255)")}

SELECT TOP 1 `s`.`Id`, `s`.`Foo`
FROM `StringKey` AS `s`
WHERE `s`.`Id` = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override void Find_composite_key_tracked()
        {
            base.Find_composite_key_tracked();

            Assert.Equal("", Sql);
        }

        public override void Find_composite_key_from_store()
        {
            base.Find_composite_key_from_store();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='77'")}
{AssertSqlHelper.Declaration("@__p_1='Dog' (Size = 255)")}

SELECT TOP 1 `c`.`Id1`, `c`.`Id2`, `c`.`Foo`
FROM `CompositeKey` AS `c`
WHERE (`c`.`Id1` = {AssertSqlHelper.Parameter("@__p_0")}) AND (`c`.`Id2` = {AssertSqlHelper.Parameter("@__p_1")})");
        }

        public override void Returns_null_for_composite_key_not_in_store()
        {
            base.Returns_null_for_composite_key_not_in_store();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='77'")}
{AssertSqlHelper.Declaration("@__p_1='Fox' (Size = 255)")}

SELECT TOP 1 `c`.`Id1`, `c`.`Id2`, `c`.`Foo`
FROM `CompositeKey` AS `c`
WHERE (`c`.`Id1` = {AssertSqlHelper.Parameter("@__p_0")}) AND (`c`.`Id2` = {AssertSqlHelper.Parameter("@__p_1")})");
        }

        public override void Find_base_type_tracked()
        {
            base.Find_base_type_tracked();

            Assert.Equal("", Sql);
        }

        public override async Task Find_base_type_tracked_async()
        {
            await base.Find_base_type_tracked_async();

            Assert.Equal("", Sql);
        }

        public override void Find_base_type_from_store()
        {
            base.Find_base_type_from_store();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='77'")}

SELECT TOP 1 `b`.`Id`, `b`.`Discriminator`, `b`.`Foo`, `b`.`Boo`
FROM `BaseType` AS `b`
WHERE `b`.`Id` = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override async Task Find_base_type_from_store_async()
        {
            await base.Find_base_type_from_store_async();
            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='77'")}

SELECT TOP 1 `b`.`Id`, `b`.`Discriminator`, `b`.`Foo`, `b`.`Boo`
FROM `BaseType` AS `b`
WHERE `b`.`Id` = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override void Returns_null_for_base_type_not_in_store()
        {
            base.Returns_null_for_base_type_not_in_store();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='99'")}

SELECT TOP 1 `b`.`Id`, `b`.`Discriminator`, `b`.`Foo`, `b`.`Boo`
FROM `BaseType` AS `b`
WHERE `b`.`Id` = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override void Find_derived_type_tracked()
        {
            base.Find_derived_type_tracked();

            Assert.Equal("", Sql);
        }

        public override void Find_derived_type_from_store()
        {
            base.Find_derived_type_from_store();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='78'")}

SELECT TOP 1 `b`.`Id`, `b`.`Discriminator`, `b`.`Foo`, `b`.`Boo`
FROM `BaseType` AS `b`
WHERE (`b`.`Discriminator` = 'DerivedType') AND (`b`.`Id` = {AssertSqlHelper.Parameter("@__p_0")})");
        }

        public override void Returns_null_for_derived_type_not_in_store()
        {
            base.Returns_null_for_derived_type_not_in_store();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='99'")}

SELECT TOP 1 `b`.`Id`, `b`.`Discriminator`, `b`.`Foo`, `b`.`Boo`
FROM `BaseType` AS `b`
WHERE (`b`.`Discriminator` = 'DerivedType') AND (`b`.`Id` = {AssertSqlHelper.Parameter("@__p_0")})");
        }

        public override void Find_base_type_using_derived_set_tracked()
        {
            base.Find_base_type_using_derived_set_tracked();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='88'")}

SELECT TOP 1 `b`.`Id`, `b`.`Discriminator`, `b`.`Foo`, `b`.`Boo`
FROM `BaseType` AS `b`
WHERE (`b`.`Discriminator` = 'DerivedType') AND (`b`.`Id` = {AssertSqlHelper.Parameter("@__p_0")})");
        }

        public override void Find_base_type_using_derived_set_from_store()
        {
            base.Find_base_type_using_derived_set_from_store();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='77'")}

SELECT TOP 1 `b`.`Id`, `b`.`Discriminator`, `b`.`Foo`, `b`.`Boo`
FROM `BaseType` AS `b`
WHERE (`b`.`Discriminator` = 'DerivedType') AND (`b`.`Id` = {AssertSqlHelper.Parameter("@__p_0")})");
        }

        public override void Find_derived_type_using_base_set_tracked()
        {
            base.Find_derived_type_using_base_set_tracked();

            Assert.Equal("", Sql);
        }

        public override void Find_derived_using_base_set_type_from_store()
        {
            base.Find_derived_using_base_set_type_from_store();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='78'")}

SELECT TOP 1 `b`.`Id`, `b`.`Discriminator`, `b`.`Foo`, `b`.`Boo`
FROM `BaseType` AS `b`
WHERE `b`.`Id` = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override void Find_shadow_key_tracked()
        {
            base.Find_shadow_key_tracked();

            Assert.Equal("", Sql);
        }

        public override void Find_shadow_key_from_store()
        {
            base.Find_shadow_key_from_store();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='77'")}

SELECT TOP 1 `s`.`Id`, `s`.`Foo`
FROM `ShadowKey` AS `s`
WHERE `s`.`Id` = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override void Returns_null_for_shadow_key_not_in_store()
        {
            base.Returns_null_for_shadow_key_not_in_store();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='99'")}

SELECT TOP 1 `s`.`Id`, `s`.`Foo`
FROM `ShadowKey` AS `s`
WHERE `s`.`Id` = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        private string Sql => Fixture.TestSqlLoggerFactory.Sql;

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        public class FindJetFixture : FindFixtureBase
        {
            public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
        }
    }
}
