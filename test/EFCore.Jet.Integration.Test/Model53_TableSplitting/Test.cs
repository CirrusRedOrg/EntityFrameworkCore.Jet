using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model53_TableSplitting
{
    public abstract class Test : TestBase<Context>
    {
        
        [TestMethod]
        public void Model53_TableSplittingRun()
        {
            Context.Persons.Add(
                new Person() {PersonId = 1, Name = "Bubi", Address = new Address() {Province = "MO", City = new City() {Name = "Maranello"}}}
            );
            Context.SaveChanges();

            var person = Context.Persons.FirstOrDefault();
            var cityName = person.Address.City.Name;
            Console.WriteLine(cityName);

            var address = Context.Addresses.FirstOrDefault();
            var personName = address.Person.Name;
            Console.WriteLine(personName);
        }
    }
}
