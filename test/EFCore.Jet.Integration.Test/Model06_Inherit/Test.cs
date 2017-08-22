using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model06_Inherit
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Model06_InheritRun()
        {

            int userId;

            {
                User user = new User
                {
                    Firstname = "Bubi",
                    Address = new Address
                    {
                        City = "Modena"
                    }
                };

                Context.Users.Add(user);
                Context.SaveChanges();

                userId = user.Id;
            }

            base.DisposeContext();
            base.CreateContext();

            {
                User user = Context.Users.First(u => u.Id == userId);
                Console.WriteLine("{0} {1}", user.Firstname, user.Address.City);

                user.Address.City = "Bologna";
                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();

            {
                User user = Context.Users.First(u => u.Id == userId);
                Console.WriteLine("{0} {1}", user.Firstname, user.Address.City);
            }

        }
    }


}

