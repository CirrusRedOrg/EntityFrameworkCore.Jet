using System;
using System.Data.Common;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model77_DateTimeOffset
{
    public abstract class Test
    {
        protected abstract DbConnection GetConnection();

        [TestMethod]
        public void Model77_DateTimeOffset()
        {
            using (DbConnection connection = GetConnection())
            {
                using (var context = new Context(connection))
                {
                    context.MyEntities.Add(new MyEntity() {DateTimeOffset = DateTime.Now});
                    context.SaveChanges();
                }

                using (var context = new Context(connection))
                {
                    Console.WriteLine(
                        context.MyEntities.FirstOrDefault()
                            .DateTimeOffset);
                }
            }
        }
    }
}