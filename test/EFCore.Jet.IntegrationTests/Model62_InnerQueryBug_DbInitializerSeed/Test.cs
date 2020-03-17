using System;
using System.Data.Common;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model62_InnerQueryBug_DbInitializerSeed
{
    public abstract class Test
    {
        protected abstract DbConnection GetConnection();

        [TestMethod]
        public void Run()
        {
            Database.SetInitializer(new DbInitializer());

            using (var context = new Context(GetConnection()))
            {
                var frequentItems = context.Items.Where(x => !x.IsService)
                    .OrderByDescending(y => y.SaleDetails.Count).Take(2).ToList();

                foreach (var item in frequentItems)
                {
                    Console.WriteLine(item.Name);
                }
            }
        }
    }
}
