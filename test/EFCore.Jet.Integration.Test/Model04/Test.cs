using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model04
{
    public abstract class Test : TestBase<CarsContext>
    {
        [TestMethod]
        public void SaveTest()
        {
            Context.Cars.Add(new Car { Name = "Maserati" });
            Context.Cars.Add(new Car { Name = "Ferrari" });
            Context.Cars.Add(new Car { Name = "Lamborghini" });

            Context.SaveChanges();
        }

        [TestMethod]
        public void SkipTakeTest()
        {
            SeedHelper.SeedPersons(Context);
            List<Person> persons = Context.Persons.OrderBy(p => p.Name).Skip(3).Take(5).ToList();
            Assert.AreEqual(5, persons.Count);
            foreach (Person person in persons)
                Console.WriteLine(person.Name);
            Console.WriteLine("=====================");
            foreach (Person person in Context.Persons.OrderBy(p => p.Name).ToList())
                Console.WriteLine(person.Name);
        }

    }
}
