using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model18_CompositeKeys
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
