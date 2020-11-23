using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model18_CompositeKeys
{
    public abstract class Test : TestBase<TestContext>
    {

        [TestMethod]
        public void Model18_CompositeKeysRun()
        {
            Context.Products.Add(
                new Product()
                {
                    ArticleNumber = "ABCD"
                });
            Context.SaveChanges();
        }
    }
}
