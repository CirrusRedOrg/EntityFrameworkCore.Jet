using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model12_ComplexType
{
    public abstract class Test : TestBase<TestContext>
    {
        [TestMethod]
        public void Model12_ComplexTypeRun()
        {
            var friend = new Friend
            {
                Name = "Bubi",
                Address =
                {
                    Cap = "40100",
                    Street = "The street"
                }
            };

            Context.Friends.Add(friend);

            Context.SaveChanges();
        }
    }
}
