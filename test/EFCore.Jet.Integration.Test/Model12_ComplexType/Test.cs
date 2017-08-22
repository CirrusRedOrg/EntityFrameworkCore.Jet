using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model12_ComplexType
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
