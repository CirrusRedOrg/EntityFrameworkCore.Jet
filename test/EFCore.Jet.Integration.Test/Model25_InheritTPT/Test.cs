using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model25_InheritTPT
{
    public abstract class Test : TestBase<Context>
    {


        [TestMethod]
        public void Model25_InheritTPTRun()
        {
            var companies = new List<Company>
            {
                new Company {Name = "X", CreatedOn = DateTime.Now, IsActive = true, UpdatedOn = DateTime.Now},
                new Company {Name = "XX", CreatedOn = DateTime.Now, IsActive = true, UpdatedOn = DateTime.Now},
                new Company {Name = "XXX", CreatedOn = DateTime.Now, IsActive = true, UpdatedOn = DateTime.Now},
                new Company {Name = "XXXX", CreatedOn = DateTime.Now, IsActive = true, UpdatedOn = DateTime.Now},
            };

            foreach (var item in companies)
            {
                Context.Companies.Add(item);
            }

            var suppliers = new List<Supplier>
            {
                new Supplier {CreatedOn = DateTime.Now, Company = companies[0], IsActive = true, UpdatedOn = DateTime.Now},
                new Supplier {CreatedOn = DateTime.Now, Company = companies[1], IsActive = true, UpdatedOn = DateTime.Now},
                new Supplier {CreatedOn = DateTime.Now, Company = companies[2], IsActive = true, UpdatedOn = DateTime.Now},
                new Supplier {CreatedOn = DateTime.Now, Company = companies[3], IsActive = true, UpdatedOn = DateTime.Now}
            };

            foreach (var item in suppliers)
            {
                Context.Suppliers.Add(item);
            }

            Context.SaveChanges();
        }
    }
}
