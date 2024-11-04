using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model70_InMemoryObjectPartialUpdate
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Run()
        {
            int itemId;


            {
                Item item = new() {ColumnA = "ColumnA", ColumnB = "ColumnB"};
                Context.Items.Add(item);
                Context.SaveChanges();
                itemId = item.Id;
            }

            base.DisposeContext();
            base.CreateContext();

            {
                Item item = new() { Id = itemId, ColumnA = "ColumnAUpdated"};
                Context.Entry(item).State = EntityState.Modified;
                Context.Entry(item).Property(_ => _.ColumnB).IsModified = false;
                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();

            {
                Item item = Context.Items.Find(itemId);
                Assert.IsNotNull(item);
                Assert.AreEqual("ColumnAUpdated", item.ColumnA);
                Assert.AreEqual("ColumnB", item.ColumnB);
            }



        }
    }
}