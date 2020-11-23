using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model31_DoubleReference
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Run()
        {
            var persons = Context.People.Where(x => x.MainPhoneNumber.PersonPhoneId == 12).ToList();
        }
    }
}
