using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model63_Time
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Run()
        {
            Item item1;
            Item item2;

            var timeSpan = new TimeSpan(15, 12, 6);

            {
                Context.Items.AddRange(
                    new[]
                    {
                        item1 = new Item() {TimeSpan = null, DateTime = new DateTime(1969, 09, 15)},
                        item2 = new Item() {TimeSpan = timeSpan}
                    });
                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();

            {
                Assert.AreNotEqual(0, Context.Items.Count(_ => _.TimeSpan == timeSpan));
            }

            base.DisposeContext();
            base.CreateContext();

            {
                Assert.IsNull(Context.Items.Find(item1.Id).TimeSpan);
                var item = Context.Items.Find(item2.Id);
                Assert.AreEqual(timeSpan, item.TimeSpan);
            }

        }
    }
}
