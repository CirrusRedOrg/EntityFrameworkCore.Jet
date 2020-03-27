using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model24_MultiTenantApp
{
    public abstract class Test
    {
        protected abstract DbConnection GetConnection();

        [TestMethod]
        public void Run()
        {

            using (DbConnection connection = GetConnection())
            using (Context context = new Context(connection))
            {

                MyEntity myNewEntity;

                context.MyEntities.Add(myNewEntity = new MyEntity()
                {
                    Description = "My first message"
                });
                context.SaveChanges();

                Console.WriteLine("{0} {1}", myNewEntity.Id, myNewEntity.TenantId);

            }



        }
    }
}
