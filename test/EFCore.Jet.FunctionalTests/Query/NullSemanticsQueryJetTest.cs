// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.NullSemanticsModel;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class NullSemanticsQueryJetTest : NullSemanticsQueryTestBase<NullSemanticsQueryJetFixture>
    {
        // ReSharper disable once UnusedParameter.Local
        public NullSemanticsQueryJetTest(NullSemanticsQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            //Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        public override async Task Compare_bool_with_bool_equal(bool async)
        {
            await base.Compare_bool_with_bool_equal(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` = `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` = `e`.`NullableBoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableBoolA` = `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` = `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL AND `e`.`NullableBoolB` IS NULL)");
        }

        public override async Task Compare_negated_bool_with_bool_equal(bool async)
        {
            await base.Compare_negated_bool_with_bool_equal(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` <> `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`BoolA` <> `e`.`NullableBoolB`) AND `e`.`NullableBoolB` IS NOT NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` <> `e`.`BoolB`) AND `e`.`NullableBoolA` IS NOT NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableBoolA` <> `e`.`NullableBoolB`) AND (`e`.`NullableBoolA` IS NOT NULL AND `e`.`NullableBoolB` IS NOT NULL)) OR (`e`.`NullableBoolA` IS NULL AND `e`.`NullableBoolB` IS NULL)");
        }

        public override async Task Compare_bool_with_negated_bool_equal(bool async)
        {
            await base.Compare_bool_with_negated_bool_equal(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` <> `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`BoolA` <> `e`.`NullableBoolB`) AND `e`.`NullableBoolB` IS NOT NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` <> `e`.`BoolB`) AND `e`.`NullableBoolA` IS NOT NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableBoolA` <> `e`.`NullableBoolB`) AND (`e`.`NullableBoolA` IS NOT NULL AND `e`.`NullableBoolB` IS NOT NULL)) OR (`e`.`NullableBoolA` IS NULL AND `e`.`NullableBoolB` IS NULL)");
        }

        public override async Task Compare_negated_bool_with_negated_bool_equal(bool async)
        {
            await base.Compare_negated_bool_with_negated_bool_equal(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` = `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`BoolA` = `e`.`NullableBoolB`) AND `e`.`NullableBoolB` IS NOT NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` = `e`.`BoolB`) AND `e`.`NullableBoolA` IS NOT NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableBoolA` = `e`.`NullableBoolB`) AND (`e`.`NullableBoolA` IS NOT NULL AND `e`.`NullableBoolB` IS NOT NULL)) OR (`e`.`NullableBoolA` IS NULL AND `e`.`NullableBoolB` IS NULL)");
        }

        public override async Task Compare_bool_with_bool_equal_negated(bool async)
        {
            await base.Compare_bool_with_bool_equal_negated(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` <> `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`BoolA` <> `e`.`NullableBoolB`) OR `e`.`NullableBoolB` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` <> `e`.`BoolB`) OR `e`.`NullableBoolA` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableBoolA` <> `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL OR `e`.`NullableBoolB` IS NULL)) AND (`e`.`NullableBoolA` IS NOT NULL OR `e`.`NullableBoolB` IS NOT NULL)");
        }

        public override async Task Compare_negated_bool_with_bool_equal_negated(bool async)
        {
            await base.Compare_negated_bool_with_bool_equal_negated(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` = `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`BoolA` = `e`.`NullableBoolB`) OR `e`.`NullableBoolB` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` = `e`.`BoolB`) OR `e`.`NullableBoolA` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableBoolA` = `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL OR `e`.`NullableBoolB` IS NULL)) AND (`e`.`NullableBoolA` IS NOT NULL OR `e`.`NullableBoolB` IS NOT NULL)");
        }

        public override async Task Compare_bool_with_negated_bool_equal_negated(bool async)
        {
            await base.Compare_bool_with_negated_bool_equal_negated(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` = `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`BoolA` = `e`.`NullableBoolB`) OR `e`.`NullableBoolB` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` = `e`.`BoolB`) OR `e`.`NullableBoolA` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableBoolA` = `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL OR `e`.`NullableBoolB` IS NULL)) AND (`e`.`NullableBoolA` IS NOT NULL OR `e`.`NullableBoolB` IS NOT NULL)");
        }

        public override async Task Compare_negated_bool_with_negated_bool_equal_negated(bool async)
        {
            await base.Compare_negated_bool_with_negated_bool_equal_negated(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` <> `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`BoolA` <> `e`.`NullableBoolB`) OR `e`.`NullableBoolB` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` <> `e`.`BoolB`) OR `e`.`NullableBoolA` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableBoolA` <> `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL OR `e`.`NullableBoolB` IS NULL)) AND (`e`.`NullableBoolA` IS NOT NULL OR `e`.`NullableBoolB` IS NOT NULL)");
        }

        public override async Task Compare_bool_with_bool_not_equal(bool async)
        {
            await base.Compare_bool_with_bool_not_equal(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` <> `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`BoolA` <> `e`.`NullableBoolB`) OR `e`.`NullableBoolB` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` <> `e`.`BoolB`) OR `e`.`NullableBoolA` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableBoolA` <> `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL OR `e`.`NullableBoolB` IS NULL)) AND (`e`.`NullableBoolA` IS NOT NULL OR `e`.`NullableBoolB` IS NOT NULL)");
        }

        public override async Task Compare_negated_bool_with_bool_not_equal(bool async)
        {
            await base.Compare_negated_bool_with_bool_not_equal(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` = `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`BoolA` = `e`.`NullableBoolB`) OR `e`.`NullableBoolB` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` = `e`.`BoolB`) OR `e`.`NullableBoolA` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableBoolA` = `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL OR `e`.`NullableBoolB` IS NULL)) AND (`e`.`NullableBoolA` IS NOT NULL OR `e`.`NullableBoolB` IS NOT NULL)");
        }

        public override async Task Compare_bool_with_negated_bool_not_equal(bool async)
        {
            await base.Compare_bool_with_negated_bool_not_equal(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` = `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`BoolA` = `e`.`NullableBoolB`) OR `e`.`NullableBoolB` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` = `e`.`BoolB`) OR `e`.`NullableBoolA` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableBoolA` = `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL OR `e`.`NullableBoolB` IS NULL)) AND (`e`.`NullableBoolA` IS NOT NULL OR `e`.`NullableBoolB` IS NOT NULL)");
        }

        public override async Task Compare_negated_bool_with_negated_bool_not_equal(bool async)
        {
            await base.Compare_negated_bool_with_negated_bool_not_equal(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` <> `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`BoolA` <> `e`.`NullableBoolB`) OR `e`.`NullableBoolB` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` <> `e`.`BoolB`) OR `e`.`NullableBoolA` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableBoolA` <> `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL OR `e`.`NullableBoolB` IS NULL)) AND (`e`.`NullableBoolA` IS NOT NULL OR `e`.`NullableBoolB` IS NOT NULL)");
        }

        public override async Task Compare_bool_with_bool_not_equal_negated(bool async)
        {
            await base.Compare_bool_with_bool_not_equal_negated(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` = `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`BoolA` = `e`.`NullableBoolB`) AND `e`.`NullableBoolB` IS NOT NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` = `e`.`BoolB`) AND `e`.`NullableBoolA` IS NOT NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableBoolA` = `e`.`NullableBoolB`) AND (`e`.`NullableBoolA` IS NOT NULL AND `e`.`NullableBoolB` IS NOT NULL)) OR (`e`.`NullableBoolA` IS NULL AND `e`.`NullableBoolB` IS NULL)");
        }

        public override async Task Compare_negated_bool_with_bool_not_equal_negated(bool async)
        {
            await base.Compare_negated_bool_with_bool_not_equal_negated(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` <> `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`BoolA` <> `e`.`NullableBoolB`) AND `e`.`NullableBoolB` IS NOT NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` <> `e`.`BoolB`) AND `e`.`NullableBoolA` IS NOT NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableBoolA` <> `e`.`NullableBoolB`) AND (`e`.`NullableBoolA` IS NOT NULL AND `e`.`NullableBoolB` IS NOT NULL)) OR (`e`.`NullableBoolA` IS NULL AND `e`.`NullableBoolB` IS NULL)");
        }

        public override async Task Compare_bool_with_negated_bool_not_equal_negated(bool async)
        {
            await base.Compare_bool_with_negated_bool_not_equal_negated(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` <> `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`BoolA` <> `e`.`NullableBoolB`) AND `e`.`NullableBoolB` IS NOT NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` <> `e`.`BoolB`) AND `e`.`NullableBoolA` IS NOT NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableBoolA` <> `e`.`NullableBoolB`) AND (`e`.`NullableBoolA` IS NOT NULL AND `e`.`NullableBoolB` IS NOT NULL)) OR (`e`.`NullableBoolA` IS NULL AND `e`.`NullableBoolB` IS NULL)");
        }

        public override async Task Compare_negated_bool_with_negated_bool_not_equal_negated(bool async)
        {
            await base.Compare_negated_bool_with_negated_bool_not_equal_negated(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` = `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`BoolA` = `e`.`NullableBoolB`) AND `e`.`NullableBoolB` IS NOT NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` = `e`.`BoolB`) AND `e`.`NullableBoolA` IS NOT NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableBoolA` = `e`.`NullableBoolB`) AND (`e`.`NullableBoolA` IS NOT NULL AND `e`.`NullableBoolB` IS NOT NULL)) OR (`e`.`NullableBoolA` IS NULL AND `e`.`NullableBoolB` IS NULL)");
        }

        public override async Task Compare_equals_method(bool async)
        {
            await base.Compare_equals_method(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` = `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` = `e`.`NullableBoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableBoolA` = `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` = `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL AND `e`.`NullableBoolB` IS NULL)");
        }

        public override async Task Compare_equals_method_static(bool async)
        {
            await base.Compare_equals_method_static(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` = `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` = `e`.`NullableBoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableBoolA` = `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` = `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL AND `e`.`NullableBoolB` IS NULL)");
        }

        public override async Task Compare_equals_method_negated(bool async)
        {
            await base.Compare_equals_method_negated(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` <> `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`BoolA` <> `e`.`NullableBoolB`) OR `e`.`NullableBoolB` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` <> `e`.`BoolB`) OR `e`.`NullableBoolA` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableBoolA` <> `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL OR `e`.`NullableBoolB` IS NULL)) AND (`e`.`NullableBoolA` IS NOT NULL OR `e`.`NullableBoolB` IS NOT NULL)");
        }

        public override async Task Compare_equals_method_negated_static(bool async)
        {
            await base.Compare_equals_method_negated_static(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` <> `e`.`BoolB`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`BoolA` <> `e`.`NullableBoolB`) OR `e`.`NullableBoolB` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` <> `e`.`BoolB`) OR `e`.`NullableBoolA` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableBoolA` <> `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL OR `e`.`NullableBoolB` IS NULL)) AND (`e`.`NullableBoolA` IS NOT NULL OR `e`.`NullableBoolB` IS NOT NULL)");
        }

        public override async Task Compare_complex_equal_equal_equal(bool async)
        {
            await base.Compare_complex_equal_equal_equal(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF(`e`.`BoolA` = `e`.`BoolB`, 1, 0) = IIF(`e`.`IntA` = `e`.`IntB`, 1, 0)",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF((`e`.`NullableBoolA` = `e`.`BoolB`) AND `e`.`NullableBoolA` IS NOT NULL, 1, 0) = IIF((`e`.`IntA` = `e`.`NullableIntB`) AND `e`.`NullableIntB` IS NOT NULL, 1, 0)",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF(((`e`.`NullableBoolA` = `e`.`NullableBoolB`) AND (`e`.`NullableBoolA` IS NOT NULL AND `e`.`NullableBoolB` IS NOT NULL)) OR (`e`.`NullableBoolA` IS NULL AND `e`.`NullableBoolB` IS NULL), 1, 0) = IIF(((`e`.`NullableIntA` = `e`.`NullableIntB`) AND (`e`.`NullableIntA` IS NOT NULL AND `e`.`NullableIntB` IS NOT NULL)) OR (`e`.`NullableIntA` IS NULL AND `e`.`NullableIntB` IS NULL), 1, 0)");
        }

        public override async Task Compare_complex_equal_not_equal_equal(bool async)
        {
            await base.Compare_complex_equal_not_equal_equal(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF(`e`.`BoolA` = `e`.`BoolB`, 1, 0) <> IIF(`e`.`IntA` = `e`.`IntB`, 1, 0)",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF((`e`.`NullableBoolA` = `e`.`BoolB`) AND `e`.`NullableBoolA` IS NOT NULL, 1, 0) <> IIF((`e`.`IntA` = `e`.`NullableIntB`) AND `e`.`NullableIntB` IS NOT NULL, 1, 0)",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF(((`e`.`NullableBoolA` = `e`.`NullableBoolB`) AND (`e`.`NullableBoolA` IS NOT NULL AND `e`.`NullableBoolB` IS NOT NULL)) OR (`e`.`NullableBoolA` IS NULL AND `e`.`NullableBoolB` IS NULL), 1, 0) <> IIF(((`e`.`NullableIntA` = `e`.`NullableIntB`) AND (`e`.`NullableIntA` IS NOT NULL AND `e`.`NullableIntB` IS NOT NULL)) OR (`e`.`NullableIntA` IS NULL AND `e`.`NullableIntB` IS NULL), 1, 0)");
        }

        public override async Task Compare_complex_not_equal_equal_equal(bool async)
        {
            await base.Compare_complex_not_equal_equal_equal(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF(`e`.`BoolA` <> `e`.`BoolB`, 1, 0) = IIF(`e`.`IntA` = `e`.`IntB`, 1, 0)",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF((`e`.`NullableBoolA` <> `e`.`BoolB`) OR `e`.`NullableBoolA` IS NULL, 1, 0) = IIF((`e`.`IntA` = `e`.`NullableIntB`) AND `e`.`NullableIntB` IS NOT NULL, 1, 0)",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF(((`e`.`NullableBoolA` <> `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL OR `e`.`NullableBoolB` IS NULL)) AND (`e`.`NullableBoolA` IS NOT NULL OR `e`.`NullableBoolB` IS NOT NULL), 1, 0) = IIF(((`e`.`NullableIntA` = `e`.`NullableIntB`) AND (`e`.`NullableIntA` IS NOT NULL AND `e`.`NullableIntB` IS NOT NULL)) OR (`e`.`NullableIntA` IS NULL AND `e`.`NullableIntB` IS NULL), 1, 0)");
        }

        public override async Task Compare_complex_not_equal_not_equal_equal(bool async)
        {
            await base.Compare_complex_not_equal_not_equal_equal(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF(`e`.`BoolA` <> `e`.`BoolB`, 1, 0) <> IIF(`e`.`IntA` = `e`.`IntB`, 1, 0)",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF((`e`.`NullableBoolA` <> `e`.`BoolB`) OR `e`.`NullableBoolA` IS NULL, 1, 0) <> IIF((`e`.`IntA` = `e`.`NullableIntB`) AND `e`.`NullableIntB` IS NOT NULL, 1, 0)",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF(((`e`.`NullableBoolA` <> `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL OR `e`.`NullableBoolB` IS NULL)) AND (`e`.`NullableBoolA` IS NOT NULL OR `e`.`NullableBoolB` IS NOT NULL), 1, 0) <> IIF(((`e`.`NullableIntA` = `e`.`NullableIntB`) AND (`e`.`NullableIntA` IS NOT NULL AND `e`.`NullableIntB` IS NOT NULL)) OR (`e`.`NullableIntA` IS NULL AND `e`.`NullableIntB` IS NULL), 1, 0)");
        }

        public override async Task Compare_complex_not_equal_equal_not_equal(bool async)
        {
            await base.Compare_complex_not_equal_equal_not_equal(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF(`e`.`BoolA` <> `e`.`BoolB`, 1, 0) = IIF(`e`.`IntA` <> `e`.`IntB`, 1, 0)",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF((`e`.`NullableBoolA` <> `e`.`BoolB`) OR `e`.`NullableBoolA` IS NULL, 1, 0) = IIF((`e`.`IntA` <> `e`.`NullableIntB`) OR `e`.`NullableIntB` IS NULL, 1, 0)",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF(((`e`.`NullableBoolA` <> `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL OR `e`.`NullableBoolB` IS NULL)) AND (`e`.`NullableBoolA` IS NOT NULL OR `e`.`NullableBoolB` IS NOT NULL), 1, 0) = IIF(((`e`.`NullableIntA` <> `e`.`NullableIntB`) OR (`e`.`NullableIntA` IS NULL OR `e`.`NullableIntB` IS NULL)) AND (`e`.`NullableIntA` IS NOT NULL OR `e`.`NullableIntB` IS NOT NULL), 1, 0)");
        }

        public override async Task Compare_complex_not_equal_not_equal_not_equal(bool async)
        {
            await base.Compare_complex_not_equal_not_equal_not_equal(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF(`e`.`BoolA` <> `e`.`BoolB`, 1, 0) <> IIF(`e`.`IntA` <> `e`.`IntB`, 1, 0)",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF((`e`.`NullableBoolA` <> `e`.`BoolB`) OR `e`.`NullableBoolA` IS NULL, 1, 0) <> IIF((`e`.`IntA` <> `e`.`NullableIntB`) OR `e`.`NullableIntB` IS NULL, 1, 0)",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIF(((`e`.`NullableBoolA` <> `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL OR `e`.`NullableBoolB` IS NULL)) AND (`e`.`NullableBoolA` IS NOT NULL OR `e`.`NullableBoolB` IS NOT NULL), 1, 0) <> IIF(((`e`.`NullableIntA` <> `e`.`NullableIntB`) OR (`e`.`NullableIntA` IS NULL OR `e`.`NullableIntB` IS NULL)) AND (`e`.`NullableIntA` IS NOT NULL OR `e`.`NullableIntB` IS NOT NULL), 1, 0)");
        }

        public override async Task Compare_nullable_with_null_parameter_equal(bool async)
        {
            await base.Compare_nullable_with_null_parameter_equal(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableStringA` IS NULL");
        }

        public override async Task Compare_nullable_with_non_null_parameter_not_equal(bool async)
        {
            await base.Compare_nullable_with_non_null_parameter_not_equal(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__prm_0='Foo' (Size = 4000)")}

SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableStringA` = {AssertSqlHelper.Parameter("@__prm_0")}");
        }

        public override async Task Join_uses_database_semantics(bool async)
        {
            await base.Join_uses_database_semantics(async);

            AssertSql(
                $@"SELECT `e`.`Id` AS `Id1`, `e0`.`Id` AS `Id2`, `e`.`NullableIntA`, `e0`.`NullableIntB`
FROM `Entities1` AS `e`
INNER JOIN `Entities2` AS `e0` ON `e`.`NullableIntA` = `e0`.`NullableIntB`");
        }

        public override async Task Contains_with_local_array_closure_with_null(bool async)
        {
            await base.Contains_with_local_array_closure_with_null(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableStringA` IN ('Foo') OR `e`.`NullableStringA` IS NULL");
        }

        public override async Task Contains_with_local_array_closure_false_with_null(bool async)
        {
            await base.Contains_with_local_array_closure_false_with_null(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableStringA` NOT IN ('Foo') AND `e`.`NullableStringA` IS NOT NULL");
        }

        public override async Task Contains_with_local_nullable_array_closure_negated(bool async)
        {
            await base.Contains_with_local_nullable_array_closure_negated(async);

            AssertSql(
                $@"");
        }

        public override async Task Contains_with_local_array_closure_with_multiple_nulls(bool async)
        {
            await base.Contains_with_local_array_closure_with_multiple_nulls(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableStringA` IN ('Foo') OR `e`.`NullableStringA` IS NULL");
        }

        public override async Task Where_multiple_ors_with_null(bool async)
        {
            await base.Where_multiple_ors_with_null(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableStringA` = 'Foo') OR (`e`.`NullableStringA` = 'Blah')) OR `e`.`NullableStringA` IS NULL");
        }

        public override async Task Where_multiple_ands_with_null(bool async)
        {
            await base.Where_multiple_ands_with_null(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (((`e`.`NullableStringA` <> 'Foo') OR `e`.`NullableStringA` IS NULL) AND ((`e`.`NullableStringA` <> 'Blah') OR `e`.`NullableStringA` IS NULL)) AND `e`.`NullableStringA` IS NOT NULL");
        }

        public override async Task Where_multiple_ors_with_nullable_parameter(bool async)
        {
            await base.Where_multiple_ors_with_nullable_parameter(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableStringA` = 'Foo') OR `e`.`NullableStringA` IS NULL");
        }

        public override async Task Where_multiple_ands_with_nullable_parameter_and_constant(bool async)
        {
            await base.Where_multiple_ands_with_nullable_parameter_and_constant(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__prm3_2='Blah' (Size = 4000)")}

SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((((`e`.`NullableStringA` <> 'Foo') OR `e`.`NullableStringA` IS NULL) AND `e`.`NullableStringA` IS NOT NULL) AND `e`.`NullableStringA` IS NOT NULL) AND ((`e`.`NullableStringA` <> {AssertSqlHelper.Parameter("@__prm3_2")}) OR `e`.`NullableStringA` IS NULL)");
        }

        public override async Task Where_multiple_ands_with_nullable_parameter_and_constant_not_optimized(bool async)
        {
            await base.Where_multiple_ands_with_nullable_parameter_and_constant_not_optimized(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__prm3_2='Blah' (Size = 4000)")}

SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (((`e`.`NullableStringB` IS NOT NULL AND ((`e`.`NullableStringA` <> 'Foo') OR `e`.`NullableStringA` IS NULL)) AND `e`.`NullableStringA` IS NOT NULL) AND `e`.`NullableStringA` IS NOT NULL) AND ((`e`.`NullableStringA` <> {AssertSqlHelper.Parameter("@__prm3_2")}) OR `e`.`NullableStringA` IS NULL)");
        }

        public override async Task Where_coalesce(bool async)
        {
            await base.Where_coalesce(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIf(`e`.`NullableBoolA` IS NULL, NULL, `e`.`NullableBoolA`) = True");
        }

        public override async Task Where_equal_nullable_with_null_value_parameter(bool async)
        {
            await base.Where_equal_nullable_with_null_value_parameter(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableStringA` IS NULL");
        }

        public override async Task Where_not_equal_nullable_with_null_value_parameter(bool async)
        {
            await base.Where_not_equal_nullable_with_null_value_parameter(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableStringA` IS NOT NULL");
        }

        public override async Task Where_equal_with_coalesce(bool async)
        {
            await base.Where_equal_with_coalesce(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (IIf(`e`.`NullableStringA` IS NULL, NULL, `e`.`NullableStringA`) = `e`.`NullableStringC`) OR ((`e`.`NullableStringA` IS NULL AND `e`.`NullableStringB` IS NULL) AND `e`.`NullableStringC` IS NULL)");
        }

        public override async Task Where_not_equal_with_coalesce(bool async)
        {
            await base.Where_not_equal_with_coalesce(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((IIf(`e`.`NullableStringA` IS NULL, NULL, `e`.`NullableStringA`) <> `e`.`NullableStringC`) OR ((`e`.`NullableStringA` IS NULL AND `e`.`NullableStringB` IS NULL) OR `e`.`NullableStringC` IS NULL)) AND ((`e`.`NullableStringA` IS NOT NULL OR `e`.`NullableStringB` IS NOT NULL) OR `e`.`NullableStringC` IS NOT NULL)");
        }

        public override async Task Where_equal_with_coalesce_both_sides(bool async)
        {
            await base.Where_equal_with_coalesce_both_sides(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE IIf(`e`.`NullableStringA` IS NULL, NULL, `e`.`NullableStringA`) = IIf(`e`.`StringA` IS NULL, NULL, `e`.`StringA`)");
        }

        public override async Task Where_not_equal_with_coalesce_both_sides(bool async)
        {
            await base.Where_not_equal_with_coalesce_both_sides(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((IIf(`e`.`NullableIntA` IS NULL, NULL, `e`.`NullableIntA`) <> IIf(`e`.`NullableIntC` IS NULL, NULL, `e`.`NullableIntC`)) OR ((`e`.`NullableIntA` IS NULL AND `e`.`NullableIntB` IS NULL) OR (`e`.`NullableIntC` IS NULL AND `e`.`NullableIntB` IS NULL))) AND ((`e`.`NullableIntA` IS NOT NULL OR `e`.`NullableIntB` IS NOT NULL) OR (`e`.`NullableIntC` IS NOT NULL OR `e`.`NullableIntB` IS NOT NULL))");
        }

        public override async Task Where_equal_with_conditional(bool async)
        {
            await base.Where_equal_with_conditional(async);

            // issue #15994
//            AssertSql(
//                $@"SELECT `e`.`Id`
//FROM `Entities1` AS `e`
//WHERE (CASE
//    WHEN (`e`.`NullableStringA` = `e`.`NullableStringB`) OR (`e`.`NullableStringA` IS NULL AND `e`.`NullableStringB` IS NULL)
//    THEN `e`.`NullableStringA` ELSE `e`.`NullableStringB`
//END = `e`.`NullableStringC`) OR (CASE
//    WHEN (`e`.`NullableStringA` = `e`.`NullableStringB`) OR (`e`.`NullableStringA` IS NULL AND `e`.`NullableStringB` IS NULL)
//    THEN `e`.`NullableStringA` ELSE `e`.`NullableStringB`
//END IS NULL AND `e`.`NullableStringC` IS NULL)");
        }

        public override async Task Where_not_equal_with_conditional(bool async)
        {
            await base.Where_not_equal_with_conditional(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableStringC` <> CASE
    WHEN (`e`.`NullableStringA` = `e`.`NullableStringB`) OR (`e`.`NullableStringA` IS NULL AND `e`.`NullableStringB` IS NULL) THEN `e`.`NullableStringA`
    ELSE `e`.`NullableStringB`
END) OR (`e`.`NullableStringC` IS NULL OR CASE
    WHEN (`e`.`NullableStringA` = `e`.`NullableStringB`) OR (`e`.`NullableStringA` IS NULL AND `e`.`NullableStringB` IS NULL) THEN `e`.`NullableStringA`
    ELSE `e`.`NullableStringB`
END IS NULL)) AND (`e`.`NullableStringC` IS NOT NULL OR CASE
    WHEN (`e`.`NullableStringA` = `e`.`NullableStringB`) OR (`e`.`NullableStringA` IS NULL AND `e`.`NullableStringB` IS NULL) THEN `e`.`NullableStringA`
    ELSE `e`.`NullableStringB`
END IS NOT NULL)");
        }

        public override async Task Where_equal_with_conditional_non_nullable(bool async)
        {
            await base.Where_equal_with_conditional_non_nullable(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableStringC` <> CASE
    WHEN (`e`.`NullableStringA` = `e`.`NullableStringB`) OR (`e`.`NullableStringA` IS NULL AND `e`.`NullableStringB` IS NULL) THEN `e`.`StringA`
    ELSE `e`.`StringB`
END) OR `e`.`NullableStringC` IS NULL");
        }

        public override async Task Where_equal_with_and_and_contains(bool async)
        {
            await base.Where_equal_with_and_and_contains(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((`e`.`NullableStringB` = '') OR (CHARINDEX(`e`.`NullableStringB`, `e`.`NullableStringA`) > 0)) AND (`e`.`BoolA` = True)");
        }

        public override async Task Null_comparison_in_selector_with_relational_nulls(bool async)
        {
            await base.Null_comparison_in_selector_with_relational_nulls(async);

            AssertSql(
                $@"SELECT IIF(`e`.`NullableStringA` <> 'Foo', 1, 0)
FROM `Entities1` AS `e`");
        }

        public override async Task Null_comparison_in_order_by_with_relational_nulls(bool async)
        {
            await base.Null_comparison_in_order_by_with_relational_nulls(async);

            AssertSql(
                $@"SELECT `e`.`Id`, `e`.`BoolA`, `e`.`BoolB`, `e`.`BoolC`, `e`.`IntA`, `e`.`IntB`, `e`.`IntC`, `e`.`NullableBoolA`, `e`.`NullableBoolB`, `e`.`NullableBoolC`, `e`.`NullableIntA`, `e`.`NullableIntB`, `e`.`NullableIntC`, `e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`, `e`.`StringA`, `e`.`StringB`, `e`.`StringC`
FROM `Entities1` AS `e`
ORDER BY `e`.`NullableStringA` <> 'Foo', `e`.`NullableIntB` <> 10");
        }

        public override async Task Null_comparison_in_join_key_with_relational_nulls(bool async)
        {
            await base.Null_comparison_in_join_key_with_relational_nulls(async);

            AssertSql(
                $@"SELECT `e1`.`Id`, `e1`.`BoolA`, `e1`.`BoolB`, `e1`.`BoolC`, `e1`.`IntA`, `e1`.`IntB`, `e1`.`IntC`, `e1`.`NullableBoolA`, `e1`.`NullableBoolB`, `e1`.`NullableBoolC`, `e1`.`NullableIntA`, `e1`.`NullableIntB`, `e1`.`NullableIntC`, `e1`.`NullableStringA`, `e1`.`NullableStringB`, `e1`.`NullableStringC`, `e1`.`StringA`, `e1`.`StringB`, `e1`.`StringC`, `e2`.`Id`, `e2`.`BoolA`, `e2`.`BoolB`, `e2`.`BoolC`, `e2`.`IntA`, `e2`.`IntB`, `e2`.`IntC`, `e2`.`NullableBoolA`, `e2`.`NullableBoolB`, `e2`.`NullableBoolC`, `e2`.`NullableIntA`, `e2`.`NullableIntB`, `e2`.`NullableIntC`, `e2`.`NullableStringA`, `e2`.`NullableStringB`, `e2`.`NullableStringC`, `e2`.`StringA`, `e2`.`StringB`, `e2`.`StringC`
FROM `Entities1` AS `e1`
INNER JOIN `Entities2` AS `e2` ON CASE
    WHEN `e1`.`NullableStringA` <> 'Foo'
    THEN True ELSE False
END = CASE
    WHEN `e2`.`NullableBoolB` <> True
    THEN True ELSE False
END");
        }

        public override async Task Where_conditional_search_condition_in_result(bool async)
        {
            await base.Where_conditional_search_condition_in_result(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`StringA` IN ('Foo', 'Bar')",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`StringA` LIKE 'A' & '%'");
        }

        public override async Task Where_nested_conditional_search_condition_in_result(bool async)
        {
            await base.Where_nested_conditional_search_condition_in_result(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`");
        }

        public override void Where_equal_using_relational_null_semantics()
        {
            base.Where_equal_using_relational_null_semantics();

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableBoolA` = `e`.`NullableBoolB`");
        }

        public override async Task Where_nullable_bool(bool async)
        {
            await base.Where_nullable_bool(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableBoolA` = True");
        }

        public override async Task Where_nullable_bool_equal_with_constant(bool async)
        {
            await base.Where_nullable_bool_equal_with_constant(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableBoolA` = True");
        }

        public override async Task Where_nullable_bool_with_null_check(bool async)
        {
            await base.Where_nullable_bool_with_null_check(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableBoolA` IS NOT NULL AND (`e`.`NullableBoolA` = True)");
        }

        public override void Where_equal_using_relational_null_semantics_with_parameter()
        {
            base.Where_equal_using_relational_null_semantics_with_parameter();

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableBoolA` IS NULL");
        }

        public override void Where_equal_using_relational_null_semantics_complex_with_parameter()
        {
            base.Where_equal_using_relational_null_semantics_complex_with_parameter();

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableBoolA` = `e`.`NullableBoolB`");
        }

        public override void Where_not_equal_using_relational_null_semantics()
        {
            base.Where_not_equal_using_relational_null_semantics();

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableBoolA` <> `e`.`NullableBoolB`");
        }

        public override void Where_not_equal_using_relational_null_semantics_with_parameter()
        {
            base.Where_not_equal_using_relational_null_semantics_with_parameter();

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableBoolA` IS NOT NULL");
        }

        public override void Where_not_equal_using_relational_null_semantics_complex_with_parameter()
        {
            base.Where_not_equal_using_relational_null_semantics_complex_with_parameter();

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableBoolA` <> `e`.`NullableBoolB`");
        }

        public override async Task Where_comparison_null_constant_and_null_parameter(bool async)
        {
            await base.Where_comparison_null_constant_and_null_parameter(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='True'")}

SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE {AssertSqlHelper.Parameter("@__p_0")} = True",
                //
                $@"{AssertSqlHelper.Declaration("@__p_0='False'")}

SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE {AssertSqlHelper.Parameter("@__p_0")} = True");
        }

        public override async Task Where_comparison_null_constant_and_nonnull_parameter(bool async)
        {
            await base.Where_comparison_null_constant_and_nonnull_parameter(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='False'")}

SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE {AssertSqlHelper.Parameter("@__p_0")} = True",
                //
                $@"{AssertSqlHelper.Declaration("@__p_0='True'")}

SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE {AssertSqlHelper.Parameter("@__p_0")} = True");
        }

        public override async Task Where_comparison_nonnull_constant_and_null_parameter(bool async)
        {
            await base.Where_comparison_nonnull_constant_and_null_parameter(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='False'")}

SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE {AssertSqlHelper.Parameter("@__p_0")} = True",
                //
                $@"{AssertSqlHelper.Declaration("@__p_0='True'")}

SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE {AssertSqlHelper.Parameter("@__p_0")} = True");
        }

        public override async Task Where_comparison_null_semantics_optimization_works_with_complex_predicates(bool async)
        {
            await base.Where_comparison_null_semantics_optimization_works_with_complex_predicates(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableStringA` IS NULL");
        }

        public override void Switching_null_semantics_produces_different_cache_entry()
        {
            base.Switching_null_semantics_produces_different_cache_entry();

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` = `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL AND `e`.`NullableBoolB` IS NULL)",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableBoolA` = `e`.`NullableBoolB`");
        }

        public override void Switching_parameter_value_to_null_produces_different_cache_entry()
        {
            base.Switching_parameter_value_to_null_produces_different_cache_entry();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='True'")}

SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE {AssertSqlHelper.Parameter("@__p_0")} = True",
                //
                $@"{AssertSqlHelper.Declaration("@__p_0='False'")}

SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE {AssertSqlHelper.Parameter("@__p_0")} = True");
        }

        public override void From_sql_composed_with_relational_null_comparison()
        {
            base.From_sql_composed_with_relational_null_comparison();

            AssertSql(
                $@"SELECT `e`.`Id`, `e`.`BoolA`, `e`.`BoolB`, `e`.`BoolC`, `e`.`IntA`, `e`.`IntB`, `e`.`IntC`, `e`.`NullableBoolA`, `e`.`NullableBoolB`, `e`.`NullableBoolC`, `e`.`NullableIntA`, `e`.`NullableIntB`, `e`.`NullableIntC`, `e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`, `e`.`StringA`, `e`.`StringB`, `e`.`StringC`
FROM (
    SELECT * FROM ""Entities1""
) AS `e`
WHERE `e`.`StringA` = `e`.`StringB`");
        }

        public override async Task Projecting_nullable_bool_with_coalesce(bool async)
        {
            await base.Projecting_nullable_bool_with_coalesce(async);

            AssertSql(
                $@"SELECT `e`.`Id`, IIf(`e`.`NullableBoolA` IS NULL, NULL, `e`.`NullableBoolA`) AS `Coalesce`
FROM `Entities1` AS `e`");
        }

        public override async Task Projecting_nullable_bool_with_coalesce_nested(bool async)
        {
            await base.Projecting_nullable_bool_with_coalesce_nested(async);

            AssertSql(
                $@"SELECT `e`.`Id`, IIf(`e`.`NullableBoolA` IS NULL, NULL, `e`.`NullableBoolA`)) AS `Coalesce`
FROM `Entities1` AS `e`");
        }

        public override async Task Null_semantics_applied_when_comparing_function_with_nullable_argument_to_a_nullable_column(bool async)
        {
            await base.Null_semantics_applied_when_comparing_function_with_nullable_argument_to_a_nullable_column(async);

            // issue #15994
//            AssertSql(
//                $@"SELECT `e`.`Id`
//FROM `Entities1` AS `e`
//WHERE ((CHARINDEX('oo', `e`.`NullableStringA`) - 1) = `e`.`NullableIntA`) OR (`e`.`NullableStringA` IS NULL AND `e`.`NullableIntA` IS NULL)",
//                //
//                $@"SELECT `e`.`Id`
//FROM `Entities1` AS `e`
//WHERE ((CHARINDEX('ar', `e`.`NullableStringA`) - 1) = `e`.`NullableIntA`) OR (`e`.`NullableStringA` IS NULL AND `e`.`NullableIntA` IS NULL)",
//                //
//                $@"SELECT `e`.`Id`
//FROM `Entities1` AS `e`
//WHERE (((CHARINDEX('oo', `e`.`NullableStringA`) - 1) <> `e`.`NullableIntB`) OR (`e`.`NullableStringA` IS NULL OR `e`.`NullableIntB` IS NULL)) AND (`e`.`NullableStringA` IS NOT NULL OR `e`.`NullableIntB` IS NOT NULL)");
        }

        public override async Task Null_semantics_applied_when_comparing_two_functions_with_nullable_arguments(bool async)
        {
            await base.Null_semantics_applied_when_comparing_two_functions_with_nullable_arguments(async);

            // issue #15994
//            AssertSql(
//                $@"SELECT `e`.`Id`
//FROM `Entities1` AS `e`
//WHERE ((CHARINDEX('oo', `e`.`NullableStringA`) - 1) = (CHARINDEX('ar', `e`.`NullableStringB`) - 1)) OR (`e`.`NullableStringA` IS NULL AND `e`.`NullableStringB` IS NULL)",
//                //
//                $@"SELECT `e`.`Id`
//FROM `Entities1` AS `e`
//WHERE (((CHARINDEX('oo', `e`.`NullableStringA`) - 1) <> (CHARINDEX('ar', `e`.`NullableStringB`) - 1)) OR (`e`.`NullableStringA` IS NULL OR `e`.`NullableStringB` IS NULL)) AND (`e`.`NullableStringA` IS NOT NULL OR `e`.`NullableStringB` IS NOT NULL)",
//                //
//                $@"SELECT `e`.`Id`
//FROM `Entities1` AS `e`
//WHERE (((CHARINDEX('oo', `e`.`NullableStringA`) - 1) <> (CHARINDEX('ar', `e`.`NullableStringA`) - 1)) OR `e`.`NullableStringA` IS NULL) AND `e`.`NullableStringA` IS NOT NULL");
        }

        public override async Task Null_semantics_applied_when_comparing_two_functions_with_multiple_nullable_arguments(bool async)
        {
            await base.Null_semantics_applied_when_comparing_two_functions_with_multiple_nullable_arguments(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (REPLACE(`e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`) = `e`.`NullableStringA`) OR (REPLACE(`e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`) IS NULL AND `e`.`NullableStringA` IS NULL)",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((REPLACE(`e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`) <> `e`.`NullableStringA`) OR (REPLACE(`e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`) IS NULL OR `e`.`NullableStringA` IS NULL)) AND (REPLACE(`e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`) IS NOT NULL OR `e`.`NullableStringA` IS NOT NULL)");
        }

        public override async Task Null_semantics_coalesce(bool async)
        {
            await base.Null_semantics_coalesce(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`NullableBoolA` = IIf(`e`.`NullableBoolB` IS NULL, NULL, `e`.`NullableBoolB`)",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableBoolA` = IIf(`e`.`NullableBoolB` IS NULL, NULL, `e`.`NullableBoolB`)) OR (`e`.`NullableBoolA` IS NULL AND (`e`.`NullableBoolB` IS NULL AND `e`.`NullableBoolC` IS NULL))",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE (IIf(`e`.`NullableBoolB` IS NULL, NULL, `e`.`NullableBoolB`) <> `e`.`NullableBoolA`) OR `e`.`NullableBoolA` IS NULL",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((IIf(`e`.`NullableBoolB` IS NULL, NULL, `e`.`NullableBoolB`) <> `e`.`NullableBoolA`) OR ((`e`.`NullableBoolB` IS NULL AND `e`.`NullableBoolC` IS NULL) OR `e`.`NullableBoolA` IS NULL)) AND ((`e`.`NullableBoolB` IS NOT NULL OR `e`.`NullableBoolC` IS NOT NULL) OR `e`.`NullableBoolA` IS NOT NULL)");
        }

        public override async Task Null_semantics_conditional(bool async)
        {
            await base.Null_semantics_conditional(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE `e`.`BoolA` = CASE
    WHEN `e`.`BoolB` = True THEN `e`.`NullableBoolB`
    ELSE `e`.`NullableBoolC`
END",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE CASE
    WHEN ((`e`.`NullableBoolA` <> `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL OR `e`.`NullableBoolB` IS NULL)) AND (`e`.`NullableBoolA` IS NOT NULL OR `e`.`NullableBoolB` IS NOT NULL) THEN `e`.`BoolB`
    ELSE `e`.`BoolC`
END = `e`.`BoolA`",
                //
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE CASE
    WHEN CASE
        WHEN `e`.`BoolA` = True THEN IIF(((`e`.`NullableBoolA` <> `e`.`NullableBoolB`) OR (`e`.`NullableBoolA` IS NULL OR `e`.`NullableBoolB` IS NULL)) AND (`e`.`NullableBoolA` IS NOT NULL OR `e`.`NullableBoolB` IS NOT NULL), 1, 0)
        ELSE `e`.`BoolC`
    END <> `e`.`BoolB` THEN `e`.`BoolA`
    ELSE IIF(((`e`.`NullableBoolB` = `e`.`NullableBoolC`) AND (`e`.`NullableBoolB` IS NOT NULL AND `e`.`NullableBoolC` IS NOT NULL)) OR (`e`.`NullableBoolB` IS NULL AND `e`.`NullableBoolC` IS NULL), 1, 0)
END = True");
        }

        public override async Task Null_semantics_function(bool async)
        {
            await base.Null_semantics_function(async);

            AssertSql(
                $@"SELECT `e`.`Id`
FROM `Entities1` AS `e`
WHERE ((SUBSTRING(`e`.`NullableStringA`, 0 + 1, `e`.`IntA`) <> `e`.`NullableStringB`) OR (SUBSTRING(`e`.`NullableStringA`, 0 + 1, `e`.`IntA`) IS NULL OR `e`.`NullableStringB` IS NULL)) AND (SUBSTRING(`e`.`NullableStringA`, 0 + 1, `e`.`IntA`) IS NOT NULL OR `e`.`NullableStringB` IS NOT NULL)");
        }

        public override async Task Null_semantics_join_with_composite_key(bool async)
        {
            await base.Null_semantics_join_with_composite_key(async);

            // issue #15994
            //AssertSql(
            //    $@"");
        }

        public override async Task Null_semantics_contains(bool async)
        {
            await base.Null_semantics_contains(async);

            AssertSql(
                $@"");
        }

        public override async Task Null_semantics_with_null_check_simple(bool async)
        {
            await base.Null_semantics_with_null_check_simple(async);

            AssertSql(
                $@"SELECT `e`.`Id`, `e`.`BoolA`, `e`.`BoolB`, `e`.`BoolC`, `e`.`IntA`, `e`.`IntB`, `e`.`IntC`, `e`.`NullableBoolA`, `e`.`NullableBoolB`, `e`.`NullableBoolC`, `e`.`NullableIntA`, `e`.`NullableIntB`, `e`.`NullableIntC`, `e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`, `e`.`StringA`, `e`.`StringB`, `e`.`StringC`
FROM `Entities1` AS `e`
WHERE `e`.`NullableIntA` IS NOT NULL AND (`e`.`NullableIntA` = `e`.`NullableIntB`)",
                //
                $@"SELECT `e`.`Id`, `e`.`BoolA`, `e`.`BoolB`, `e`.`BoolC`, `e`.`IntA`, `e`.`IntB`, `e`.`IntC`, `e`.`NullableBoolA`, `e`.`NullableBoolB`, `e`.`NullableBoolC`, `e`.`NullableIntA`, `e`.`NullableIntB`, `e`.`NullableIntC`, `e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`, `e`.`StringA`, `e`.`StringB`, `e`.`StringC`
FROM `Entities1` AS `e`
WHERE `e`.`NullableIntA` IS NOT NULL AND ((`e`.`NullableIntA` <> `e`.`NullableIntB`) OR `e`.`NullableIntB` IS NULL)",
                //
                $@"SELECT `e`.`Id`, `e`.`BoolA`, `e`.`BoolB`, `e`.`BoolC`, `e`.`IntA`, `e`.`IntB`, `e`.`IntC`, `e`.`NullableBoolA`, `e`.`NullableBoolB`, `e`.`NullableBoolC`, `e`.`NullableIntA`, `e`.`NullableIntB`, `e`.`NullableIntC`, `e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`, `e`.`StringA`, `e`.`StringB`, `e`.`StringC`
FROM `Entities1` AS `e`
WHERE `e`.`NullableIntA` IS NOT NULL AND (`e`.`NullableIntA` = `e`.`IntC`)",
                //
                $@"SELECT `e`.`Id`, `e`.`BoolA`, `e`.`BoolB`, `e`.`BoolC`, `e`.`IntA`, `e`.`IntB`, `e`.`IntC`, `e`.`NullableBoolA`, `e`.`NullableBoolB`, `e`.`NullableBoolC`, `e`.`NullableIntA`, `e`.`NullableIntB`, `e`.`NullableIntC`, `e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`, `e`.`StringA`, `e`.`StringB`, `e`.`StringC`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableIntA` IS NOT NULL AND `e`.`NullableIntB` IS NOT NULL) AND (`e`.`NullableIntA` = `e`.`NullableIntB`)",
                //
                $@"SELECT `e`.`Id`, `e`.`BoolA`, `e`.`BoolB`, `e`.`BoolC`, `e`.`IntA`, `e`.`IntB`, `e`.`IntC`, `e`.`NullableBoolA`, `e`.`NullableBoolB`, `e`.`NullableBoolC`, `e`.`NullableIntA`, `e`.`NullableIntB`, `e`.`NullableIntC`, `e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`, `e`.`StringA`, `e`.`StringB`, `e`.`StringC`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableIntA` IS NOT NULL AND `e`.`NullableIntB` IS NOT NULL) AND (`e`.`NullableIntA` <> `e`.`NullableIntB`)");
        }

        public override async Task Null_semantics_with_null_check_complex(bool async)
        {
            await base.Null_semantics_with_null_check_complex(async);

            AssertSql(
                $@"SELECT `e`.`Id`, `e`.`BoolA`, `e`.`BoolB`, `e`.`BoolC`, `e`.`IntA`, `e`.`IntB`, `e`.`IntC`, `e`.`NullableBoolA`, `e`.`NullableBoolB`, `e`.`NullableBoolC`, `e`.`NullableIntA`, `e`.`NullableIntB`, `e`.`NullableIntC`, `e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`, `e`.`StringA`, `e`.`StringB`, `e`.`StringC`
FROM `Entities1` AS `e`
WHERE `e`.`NullableIntA` IS NOT NULL AND (((`e`.`NullableIntC` <> `e`.`NullableIntA`) OR `e`.`NullableIntC` IS NULL) OR (`e`.`NullableIntB` IS NOT NULL AND (`e`.`NullableIntA` <> `e`.`NullableIntB`)))",
                //
                $@"SELECT `e`.`Id`, `e`.`BoolA`, `e`.`BoolB`, `e`.`BoolC`, `e`.`IntA`, `e`.`IntB`, `e`.`IntC`, `e`.`NullableBoolA`, `e`.`NullableBoolB`, `e`.`NullableBoolC`, `e`.`NullableIntA`, `e`.`NullableIntB`, `e`.`NullableIntC`, `e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`, `e`.`StringA`, `e`.`StringB`, `e`.`StringC`
FROM `Entities1` AS `e`
WHERE `e`.`NullableIntA` IS NOT NULL AND (((`e`.`NullableIntC` <> `e`.`NullableIntA`) OR `e`.`NullableIntC` IS NULL) OR ((`e`.`NullableIntA` <> `e`.`NullableIntB`) OR `e`.`NullableIntB` IS NULL))",
                //
                $@"SELECT `e`.`Id`, `e`.`BoolA`, `e`.`BoolB`, `e`.`BoolC`, `e`.`IntA`, `e`.`IntB`, `e`.`IntC`, `e`.`NullableBoolA`, `e`.`NullableBoolB`, `e`.`NullableBoolC`, `e`.`NullableIntA`, `e`.`NullableIntB`, `e`.`NullableIntC`, `e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`, `e`.`StringA`, `e`.`StringB`, `e`.`StringC`
FROM `Entities1` AS `e`
WHERE (`e`.`NullableIntA` IS NOT NULL OR `e`.`NullableIntB` IS NOT NULL) AND ((`e`.`NullableIntA` = `e`.`NullableIntC`) OR (`e`.`NullableIntA` IS NULL AND `e`.`NullableIntC` IS NULL))");
        }

        public override async Task IsNull_on_complex_expression(bool async)
        {
            await base.IsNull_on_complex_expression(async);

            AssertSql(
                $@"SELECT `e`.`Id`, `e`.`BoolA`, `e`.`BoolB`, `e`.`BoolC`, `e`.`IntA`, `e`.`IntB`, `e`.`IntC`, `e`.`NullableBoolA`, `e`.`NullableBoolB`, `e`.`NullableBoolC`, `e`.`NullableIntA`, `e`.`NullableIntB`, `e`.`NullableIntC`, `e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`, `e`.`StringA`, `e`.`StringB`, `e`.`StringC`
FROM `Entities1` AS `e`
WHERE `e`.`NullableIntA` IS NOT NULL",
                //
                $@"SELECT `e`.`Id`, `e`.`BoolA`, `e`.`BoolB`, `e`.`BoolC`, `e`.`IntA`, `e`.`IntB`, `e`.`IntC`, `e`.`NullableBoolA`, `e`.`NullableBoolB`, `e`.`NullableBoolC`, `e`.`NullableIntA`, `e`.`NullableIntB`, `e`.`NullableIntC`, `e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`, `e`.`StringA`, `e`.`StringB`, `e`.`StringC`
FROM `Entities1` AS `e`
WHERE `e`.`NullableIntA` IS NULL OR `e`.`NullableIntB` IS NULL",
                //
                $@"SELECT `e`.`Id`, `e`.`BoolA`, `e`.`BoolB`, `e`.`BoolC`, `e`.`IntA`, `e`.`IntB`, `e`.`IntC`, `e`.`NullableBoolA`, `e`.`NullableBoolB`, `e`.`NullableBoolC`, `e`.`NullableIntA`, `e`.`NullableIntB`, `e`.`NullableIntC`, `e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`, `e`.`StringA`, `e`.`StringB`, `e`.`StringC`
FROM `Entities1` AS `e`
WHERE `e`.`NullableIntA` IS NULL AND `e`.`NullableIntB` IS NULL",
                //
                $@"SELECT `e`.`Id`, `e`.`BoolA`, `e`.`BoolB`, `e`.`BoolC`, `e`.`IntA`, `e`.`IntB`, `e`.`IntC`, `e`.`NullableBoolA`, `e`.`NullableBoolB`, `e`.`NullableBoolC`, `e`.`NullableIntA`, `e`.`NullableIntB`, `e`.`NullableIntC`, `e`.`NullableStringA`, `e`.`NullableStringB`, `e`.`NullableStringC`, `e`.`StringA`, `e`.`StringB`, `e`.`StringC`
FROM `Entities1` AS `e`
WHERE `e`.`NullableIntA` IS NOT NULL OR `e`.`NullableIntB` IS NOT NULL");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        protected override void ClearLog()
            => Fixture.TestSqlLoggerFactory.Clear();

        protected override NullSemanticsContext CreateContext(bool useRelationalNulls = false)
        {
            var options = new DbContextOptionsBuilder(Fixture.CreateOptions());
            if (useRelationalNulls)
            {
                new JetDbContextOptionsBuilder(options).UseRelationalNulls();
            }

            var context = new NullSemanticsContext(options.Options);

            context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

            return context;
        }
    }
}
