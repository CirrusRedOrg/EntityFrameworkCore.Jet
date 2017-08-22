using System;
using System.Data.Common;
using System.Linq;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model36_DetachedScenario
{
    public abstract class Test : TestBase<Context>
    {

        [TestMethod]
        // Validation now is done using validation Context
        /*
        [ExpectedException(typeof(DbEntityValidationException))]
        */
        public void Model36_DetachedScenarioRun()
        {
            using (DbConnection connection = GetConnection())
            {
                Seed(connection);

                ShowHoldersId(connection);

                ShowHolders(connection);

                base.DisposeContext();
                base.CreateContext();

                {
                    Holder holder = new Holder()
                    {
                        Id = 1,
                        Some = "Holder updated",
                        Thing = new Thing() { Id = 2 }
                    };

                    Repository.Update(Context, holder);
                }

                ShowHolders(connection);

                Console.WriteLine("========== ATTACHED UPDATE =============");

                base.DisposeContext();
                base.CreateContext();

                {
                    Holder holder = Context.Holders.First();
                    holder.Thing = new Thing() { Id = 4 };
                    Context.SaveChanges();
                }

                ShowHolders(connection);
            }
        }

        private static void ShowHolders(DbConnection connection)
        {
            using (var Context = new Context(new DbContextOptionsBuilder<Context>().UseJet(connection).Options))
            {
                foreach (var holder in Context.Holders.AsQueryable().Include(_ => _.Thing).ToList())
                    Console.WriteLine(holder);
            }
        }

        private static void ShowHoldersId(DbConnection connection)
        {
            using (var Context = new Context(new DbContextOptionsBuilder<Context>().UseJet(connection).Options))
            {
                foreach (var holder in Context.Holders.Select(_ => _.Id).ToList())
                    Console.WriteLine(holder);
            }
        }


        public static void Seed(DbConnection connection)
        {
            using (var Context = new Context(new DbContextOptionsBuilder<Context>().UseJet(connection).Options))
            {
                Holder holder = new Holder()
                {
                    Some = "Holder 1"
                };

                Context.Holders.Add(holder);

                for (int i = 0; i < 3; i++)
                {
                    Thing thing = new Thing()
                    {
                        Name = string.Format("Thing {0}", i + 1)
                    };
                    Context.Things.Add(thing);
                }

                Context.SaveChanges();

            }
        }

    }
}
