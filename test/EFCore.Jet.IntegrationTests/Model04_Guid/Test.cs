using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model04_Guid
{
    public abstract class Test : TestBase<CarsContext>
    {
        [TestMethod]
        public void Model04_GuidRun()
        {
            {
                Context.Cars.Add(new Car {Name = "Maserati"});
                Context.Cars.Add(new Car {Name = "Ferrari"});
                Context.Cars.Add(new Car {Name = "Lamborghini"});

                Context.SaveChanges();

                var myQuery = Context.Persons.Where(p => p.OwnedCar.Id == Guid.Empty);
                var result = myQuery.ToList();

            }

        }


    }
}
