using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model21_CommandInterception
{
    public abstract class Test
    {
        protected abstract DbConnection GetConnection();

        [TestMethod]
        public void Run()
        {
            using (DbConnection connection = GetConnection())
            using (CarsContext context = new Model21_CommandInterception.CarsContext(connection))
            {
                context.Cars.Add(new Car() { Name = "Ferrari" });
                context.SaveChanges();
            }
        }

    }
}
