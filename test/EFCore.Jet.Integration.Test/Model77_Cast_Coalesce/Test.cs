using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model77_Cast_Coalesce
{
    public abstract class Test : TestBase<TestContext>
    {
        [TestMethod]
        public void Model77_Cast_Coalesce()
        {
            Context.Entities.AddRange(
                new Entity() {Integer = 20, String = "aaa"},
                new Entity() { Integer = 30, String = "aaa" },
                new Entity() { Integer = 40, String = "ccc" });

            Context.SaveChanges();

            Context.Entities.Select(_ => (double)_.Integer).ToList();
            Context.Entities.Select(_ => _.Integer??5).ToList();
            Context.Entities.GroupBy(g => g.String).Select(_ => new { k = _.Key, avg = _.Average(q => q.Integer)}).ToList();
            foreach (var result in Context.Entities.GroupBy(g => g.String).Select(_ => new { k = _.Key, avg = _.Average(q => (double)(q.Integer ?? 5)) }).ToList())
            {
                Console.WriteLine("{0} {1}", result.k, result.avg);
            }

            Console.WriteLine(Context.Entities.Average(_ => (float) (_.Integer ?? 5)));
            Console.WriteLine(Context.Entities.Average(_ => (double)(_.Integer ?? 5)));
            Console.WriteLine(Context.Entities.Average(_ => (decimal)(_.Integer ?? 5)));


        }
    }
}
