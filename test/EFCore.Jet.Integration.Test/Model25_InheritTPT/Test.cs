using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model25_InheritTPT
{
    public abstract class Test : TestBase<Context>
    {


        [TestMethod]
        public void Run()
        {
            var companies = new List<Company>
            {
                new Company {Id = 1, Name = "X", CreatedOn = DateTime.Now, IsActive = true, UpdatedOn = DateTime.Now},
                new Company {Id = 2, Name = "XX", CreatedOn = DateTime.Now, IsActive = true, UpdatedOn = DateTime.Now},
                new Company {Id = 3, Name = "XXX", CreatedOn = DateTime.Now, IsActive = true, UpdatedOn = DateTime.Now},
                new Company {Id = 4, Name = "XXXX", CreatedOn = DateTime.Now, IsActive = true, UpdatedOn = DateTime.Now},
            };

            foreach (var item in companies)
            {
                Context.Companies.Add(item);
            }

            var suppliers = new List<Supplier>
            {
                new Supplier {Id = 1, CreatedOn = DateTime.Now, Company = companies[0], IsActive = true, UpdatedOn = DateTime.Now},
                new Supplier {Id = 2, CreatedOn = DateTime.Now, Company = companies[1], IsActive = true, UpdatedOn = DateTime.Now},
                new Supplier {Id = 3, CreatedOn = DateTime.Now, Company = companies[2], IsActive = true, UpdatedOn = DateTime.Now},
                new Supplier {Id = 4, CreatedOn = DateTime.Now, Company = companies[3], IsActive = true, UpdatedOn = DateTime.Now}
            };

            foreach (var item in suppliers)
            {
                Context.Suppliers.Add(item);
            }

            Context.SaveChanges();
        }
    }
}
