// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestModels.UpdatesModel;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class UpdatesJetTest : UpdatesRelationalTestBase<UpdatesJetFixture>
    {
        // ReSharper disable once UnusedParameter.Local
        public UpdatesJetTest(UpdatesJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            //Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
            Fixture.TestSqlLoggerFactory.Clear();
        }

        public override void Save_replaced_principal()
        {
            base.Save_replaced_principal();

            AssertSql(
                $@"SELECT TOP 2 `c`.`Id`, `c`.`Name`, `c`.`PrincipalId`
FROM `Categories` AS `c`",
                //
                $@"{AssertSqlHelper.Declaration("@__category_PrincipalId_0='778' (Nullable = true)")}

SELECT `p`.`Id`, `p`.`DependentId`, `p`.`Name`, `p`.`Price`
FROM `Products` AS `p`
WHERE `p`.`DependentId` = {AssertSqlHelper.Parameter("@__category_PrincipalId_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@p1='78'")}

{AssertSqlHelper.Declaration("@p0='New Category' (Size = 4000)")}

SET NOCOUNT ON;
UPDATE `Categories` SET `Name` = {AssertSqlHelper.Parameter("@p0")}
WHERE `Id` = {AssertSqlHelper.Parameter("@p1")};
SELECT @@ROWCOUNT;",
                //
                $@"SELECT TOP 2 `c`.`Id`, `c`.`Name`, `c`.`PrincipalId`
FROM `Categories` AS `c`",
                //
                $@"{AssertSqlHelper.Declaration("@__category_PrincipalId_0='778' (Nullable = true)")}

SELECT `p`.`Id`, `p`.`DependentId`, `p`.`Name`, `p`.`Price`
FROM `Products` AS `p`
WHERE `p`.`DependentId` = {AssertSqlHelper.Parameter("@__category_PrincipalId_0")}");
        }

        public override void Identifiers_are_generated_correctly()
        {
            using (var context = CreateContext())
            {
                var entityType = context.Model.FindEntityType(
                    typeof(
                        LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingCorrectly
                    ));
                Assert.Equal(
                    "LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorking~",
                    entityType.GetTableName());
                Assert.Equal(
                    "PK_LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWork~",
                    entityType.GetKeys().Single().GetName());
                Assert.Equal(
                    "FK_LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWork~",
                    entityType.GetForeignKeys().Single().GetConstraintName());
                Assert.Equal(
                    "IX_LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWork~",
                    entityType.GetIndexes().Single().GetDatabaseName());

                var entityType2 = context.Model.FindEntityType(
                    typeof(
                        LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingCorrectlyDetails
                    ));

                Assert.Equal(
                    "LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkin~1",
                    entityType2.GetTableName());
                Assert.Equal(
                    "PK_LoginDetails",
                    entityType2.GetKeys().Single().GetName());
                Assert.Equal(
                    "ExtraPropertyWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingCo~",
                    entityType2.GetProperties().ElementAt(1).GetColumnBaseName());
                Assert.Equal(
                    "ExtraPropertyWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingC~1",
                    entityType2.GetProperties().ElementAt(2).GetColumnBaseName());
                Assert.Equal(
                    "IX_LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWor~1",
                    entityType2.GetIndexes().Single().GetDatabaseName());
            }
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
    }
}
