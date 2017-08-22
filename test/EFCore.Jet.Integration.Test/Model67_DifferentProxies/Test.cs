using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model67_DifferentProxies
{
    public abstract class Test : TestBase<Context>
    {

        [TestMethod]
        public void Run()
        {
            {
                var joe = new Person() {Name = "Joe"};
                var joesDad = new Person() {Name = "Joe's Dad"};
                joesDad.Children.Add(joe);
                joe.Info = new Info() { Description = "The Joe's Dad Info"};

                Context.People.Add(joesDad);
                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();

            {
                var joes1 = Context.People.Single(p => p.Name == "Joe");
                var joes2 = Context.People.Single(p => p.Name == "Joe's Dad").Children.Single(p => p.Name == "Joe");

                Assert.IsTrue(object.ReferenceEquals(joes1, joes2));
                Assert.IsTrue(object.ReferenceEquals(joes1.Info.GetType(), joes2.Info.GetType()));
                Assert.IsTrue(object.ReferenceEquals(joes1.Info, joes2.Info));
            }


            List<Person> allPeople;

            base.DisposeContext();
            base.CreateContext();
            {
                allPeople = Context.People
                    .Include(_ => _.Info)
                    .Include(_ => _.Children)
                    .ToList();
            }

            // This is an in memory query because to the previous ToList
            // Take care of == because is an in memory case sensitive query!
            Assert.IsNotNull(allPeople.Single(p => p.Name == "Joe").Info);
            Assert.IsNotNull(allPeople.Single(p => p.Name == "Joe's Dad").Children.Single(p => p.Name == "Joe").Info);
            Assert.IsTrue(object.ReferenceEquals(allPeople.Single(p => p.Name == "Joe").Info, allPeople.Single(p => p.Name == "Joe's Dad").Children.Single(p => p.Name == "Joe").Info));




            base.DisposeContext();
            base.CreateContext();
            {
                allPeople = Context.People
                    .Include(_ => _.Info)
                    .Include(_ => _.Children)
                    .AsNoTracking()
                    .ToList();
            }


            Exception exception = null;
            try
            {
                // The entities are not in the Context so this shoud not work
                Assert.IsNotNull(allPeople.Single(p => p.Name == "Joe's Dad").Children.Single(p => p.Name == "Joe").Info);
            }
            catch (Exception e)
            {
                exception = e;
            }

            Assert.IsNotNull(exception);
            Console.WriteLine(exception.Message);




        }
    }
}