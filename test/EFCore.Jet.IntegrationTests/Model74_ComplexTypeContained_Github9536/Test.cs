using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model74_ComplexTypeContained_Github9536
{
    public abstract class Test : TestBase<TestContext>
    {
        [TestMethod]
        public void Model74_ComplexType_Github9536()
        {
            var friend = new Friend
            {
                Name = "Bubi",
                Address =
                {
                    CityAddress1 = { Cap = "40100" },
                    CityAddress2 = { Cap = "40101" },
                    Street = "The street"
                }
            };

            Context.Friends.Add(friend);

            Context.SaveChanges();

            DisposeContext();
            CreateContext();

            var readFriend = Context.Friends.First();
            Assert.AreEqual("40100", readFriend.Address.CityAddress1.Cap);
            Assert.AreEqual("40101", readFriend.Address.CityAddress2.Cap);

        }
    }
}
