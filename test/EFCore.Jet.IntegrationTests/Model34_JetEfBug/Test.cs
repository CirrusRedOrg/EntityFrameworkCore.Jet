using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model34_JetEfBug
{
    public abstract class Test : TestBase<DataContext>
    {

        [TestMethod]
        public void Run()
        {
            var categoriesList = Context.Categories.Select(c => new {c.ID, c.Name, TotalItems = Context.Items.Count(i => i.Category.ID == c.ID)});
            Console.WriteLine(categoriesList);
        }
    }
}
