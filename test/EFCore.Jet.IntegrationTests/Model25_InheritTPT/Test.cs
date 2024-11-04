using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model25_InheritTPT
{
    public abstract class Test : TestBase<Context>
    {


        [TestMethod]
        public void Model25_InheritTPTRun()
        {
            var companies = new List<Company>
            {
                new() {Name = "X", CreatedOn = DateTime.Now, IsActive = true, UpdatedOn = DateTime.Now},
                new() {Name = "XX", CreatedOn = DateTime.Now, IsActive = true, UpdatedOn = DateTime.Now},
                new() {Name = "XXX", CreatedOn = DateTime.Now, IsActive = true, UpdatedOn = DateTime.Now},
                new() {Name = "XXXX", CreatedOn = DateTime.Now, IsActive = true, UpdatedOn = DateTime.Now},
            };

            foreach (var item in companies)
            {
                Context.Companies.Add(item);
            }

            var suppliers = new List<Supplier>
            {
                new() {CreatedOn = DateTime.Now, Company = companies[0], IsActive = true, UpdatedOn = DateTime.Now},
                new() {CreatedOn = DateTime.Now, Company = companies[1], IsActive = true, UpdatedOn = DateTime.Now},
                new() {CreatedOn = DateTime.Now, Company = companies[2], IsActive = true, UpdatedOn = DateTime.Now},
                new() {CreatedOn = DateTime.Now, Company = companies[3], IsActive = true, UpdatedOn = DateTime.Now}
            };

            foreach (var item in suppliers)
            {
                Context.Suppliers.Add(item);
            }

            Context.SaveChanges();
        }
    }
}
