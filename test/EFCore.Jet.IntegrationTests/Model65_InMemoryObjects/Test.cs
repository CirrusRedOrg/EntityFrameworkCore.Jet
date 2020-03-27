//
// Proxy test
//
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model65_InMemoryObjects
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Run()
        {

            Item item;


            // Seed =============================
            {
                Context.Items.AddRange(
                    new[]
                    {
                        item = new Item() {Description = "Description1"},
                        new Item() {Description = "Description2"},
                        new Item() {Description = "Description3"},
                        new Item() {Description = "Description4"},
                    });
                Context.SaveChanges();
            }

            // Upsert (object without a proxy) =============================
            item.Description = "DescriptionChanged";
            Upsert(item);

            base.DisposeContext();
            base.CreateContext();

            // Upsert (new) =============================
            Upsert(new Item() {Description = "Item from upsert"});

            base.DisposeContext();
            base.CreateContext();
            {
                item = Context.Items.First(_ => _.Description == "Description2");
            }

            // Upsert (object with a proxy) =============================
            Assert.AreNotEqual(item.GetType(), typeof(Item));
            item.Description = "Description changed to object with proxy";
            Upsert(item);
            base.DisposeContext();

            base.CreateContext();
            {
                Assert.IsNull(Context.Items.FirstOrDefault(_ => _.Description == "Description2"));
            }

        }

        public void Upsert(Item item)
        {
            if (item.Id == 0)
            {
                Context.Items.Add(item);
            }
            else
            {
                Context.Entry(item).State = EntityState.Modified;
            }

            Context.SaveChanges();
        }
    }
}
