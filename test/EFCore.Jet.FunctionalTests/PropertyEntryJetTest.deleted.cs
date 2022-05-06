//// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

//using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
//using Microsoft.EntityFrameworkCore;
//using Xunit.Abstractions;

//namespace EntityFrameworkCore.Jet.FunctionalTests
//{
//    public class PropertyEntryJetTest : PropertyEntryTestBase<F1JetFixture>
//    {
//        public PropertyEntryJetTest(F1JetFixture fixture, ITestOutputHelper testOutputHelper)
//            : base(fixture)
//        {
//            Fixture.TestSqlLoggerFactory.Clear();
//        }

//        public override void Property_entry_original_value_is_set()
//        {
//            base.Property_entry_original_value_is_set();

//            AssertSql(
//                $@"SELECT TOP 1 `e`.`Id`, `e`.`EngineSupplierId`, `e`.`Name`, `t`.`Id`, `t`.`StorageLocation_Latitude`, `t`.`StorageLocation_Longitude`
//FROM `Engines` AS `e`
//LEFT JOIN (
//    SELECT `e0`.`Id`, `e0`.`StorageLocation_Latitude`, `e0`.`StorageLocation_Longitude`, `e1`.`Id` AS `Id0`
//    FROM `Engines` AS `e0`
//    INNER JOIN `Engines` AS `e1` ON `e0`.`Id` = `e1`.`Id`
//    WHERE `e0`.`StorageLocation_Longitude` IS NOT NULL AND `e0`.`StorageLocation_Latitude` IS NOT NULL
//) AS `t` ON `e`.`Id` = `t`.`Id`
//ORDER BY `e`.`Id`",
//                //
//                $@"{AssertSqlHelper.Declaration("@p1='1'")}

//{AssertSqlHelper.Declaration("@p2='1'")}

//{AssertSqlHelper.Declaration("@p0='FO 108X' (Size = 4000)")}

//{AssertSqlHelper.Declaration("@p3='ChangedEngine' (Size = 4000)")}

//{AssertSqlHelper.Declaration("@p4='47.64491'")}

//{AssertSqlHelper.Declaration("@p5='-122.128101'")}

//SET NOCOUNT ON;
//UPDATE `Engines` SET `Name` = {AssertSqlHelper.Parameter("@p0")}
//WHERE `Id` = {AssertSqlHelper.Parameter("@p1")} AND `EngineSupplierId` = {AssertSqlHelper.Parameter("@p2")} AND `Name` = {AssertSqlHelper.Parameter("@p3")} AND `StorageLocation_Latitude` = {AssertSqlHelper.Parameter("@p4")} AND `StorageLocation_Longitude` = {AssertSqlHelper.Parameter("@p5")};
//SELECT @@ROWCOUNT;");
//        }

//        private void AssertSql(params string[] expected)
//            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
//    }
//}
