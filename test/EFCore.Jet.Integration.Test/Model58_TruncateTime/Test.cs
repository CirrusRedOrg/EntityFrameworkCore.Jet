using System;
using System.Data.Common;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model58_TruncateTime
{
    public abstract class Test
    {
        protected abstract DbConnection GetConnection();

        [TestMethod]
        public void RunEntityFunctions()
        {
            using (var context = new Context(GetConnection()))
            {
                for (int i = 0; i < 30; i++)
                    context.Entities.Add(new Entity() {Date = DateTime.Now.AddHours(i)});

                context.SaveChanges();
            }


            using (var context = new Context(GetConnection()))
            {
#pragma warning disable 618
                var dates = context.Entities.Select(_ => EntityFunctions.TruncateTime(_.Date)).Distinct().ToList();
#pragma warning restore 618
                foreach (DateTime? date in dates)
                {
                    Assert.IsNotNull(date);
                    Assert.AreEqual(0, date.Value.Hour);
                    Assert.AreEqual(0, date.Value.Minute);
                    Assert.AreEqual(0, date.Value.Second);
                }
            }

        }

        [TestMethod]
        public void RunDbFunctions()
        {
            using (var context = new Context(GetConnection()))
            {
                for (int i = 0; i < 30; i++)
                    context.Entities.Add(new Entity() { Date = DateTime.Now.AddHours(i) });

                context.SaveChanges();
            }


            using (var context = new Context(GetConnection()))
            {
                var dates = context.Entities.Select(_ => DbFunctions.TruncateTime(_.Date)).Distinct().ToList();
                foreach (DateTime? date in dates)
                {
                    Assert.IsNotNull(date);
                    Assert.AreEqual(0, date.Value.Hour);
                    Assert.AreEqual(0, date.Value.Minute);
                    Assert.AreEqual(0, date.Value.Second);
                }
            }

        }


    }
}
