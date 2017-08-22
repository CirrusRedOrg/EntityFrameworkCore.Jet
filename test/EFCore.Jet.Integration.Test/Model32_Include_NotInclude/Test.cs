using System;
using System.Data.Common;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model32_Include_NotInclude
{
    public abstract class Test : TestBase<TestContext>
    {
        [TestMethod]
        public void Model32_Include_NotIncludeRun()
        {
            Visit visit;

            {
                visit = new Visit
                {
                    Description = "Visit",
                    Address = new Address() { Description = "AddressDescription" }
                };

                Context.Visits.Add(visit);

                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();
            {
                visit = Context.Visits.ToList()[0];
                Console.WriteLine(visit.Address);
            }

            base.DisposeContext();
            base.CreateContext();
            {
                // ReSharper disable once RedundantAssignment
                visit = Context.Visits
                    .Include(v => v.Address)
                    .ToList()[0];
            }
        }

    }
}

