using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model48_Cast
{
    public abstract class Test : TestBase<Context>
    {
        // Actually no canonical function to parse an integer (cast cant compile)

        [TestMethod]
        //[ExpectedException(typeof(System.NotSupportedException))]
        public void Model48_CastRun()
        {
            {
                Context.Entities.Add(new Entity() { Id = (new Random()).Next(100000).ToString() });
                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();

            {
                Context.Entities.Where(_ => _.Number.ToString() == "A").ToList();
                //Context.Entities.Where(_ => Context.Rnd() == 10).ToList();
                Context.Entities.Where(_ => Double.Parse(_.Id) > 5).ToList();
            }
        }
    }
}
