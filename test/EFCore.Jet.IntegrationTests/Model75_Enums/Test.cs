using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model75_Enums
{
    public abstract class Test : TestBase<TestContext>
    {
        [TestMethod]
        public void Model75_EnumsRun()
        {
            Context.Entities.AddRange(
                new Entity() {EnumDataType = EnumDataType.a}, 
                new Entity() {EnumDataType = EnumDataType.b}, 
                new Entity() {EnumDataType = EnumDataType.c});

            Context.SaveChanges();

            Context.Entities.Select(_ => _.EnumDataType).ToList();
            Context.Entities.Single(_ => _.EnumDataType == EnumDataType.c);

        }
    }
}
