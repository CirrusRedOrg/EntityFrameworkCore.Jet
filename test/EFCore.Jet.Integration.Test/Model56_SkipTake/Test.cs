using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model56_SkipTake
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void SkipTakeDate()
        {
            {
                for (int i = 0; i < 30; i++)
                    Context.Entities.Add(new Entity() { Description = i.ToString(), Date = new DateTime(1969, 09, 15).AddDays(i % 2 == 0 ? i * 30 : - i * 30)});

                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();

            {
                foreach (Entity entity in Context.Entities.ToList())
                    Console.WriteLine(entity.Description);
            }

            base.DisposeContext();
            base.CreateContext();

            {
                var entities = Context.Entities.OrderBy(_ => _.Date).Skip(10).Take(5).ToList();
                Assert.AreEqual(5, entities.Count);
                for (int i = 0; i < entities.Count - 1; i++)
                {
                    Entity entity = entities[i];
                    Assert.IsTrue(entity.Date < entities[i + 1].Date);
                }
            }

            RemoveAllEntities();
        }

        private void RemoveAllEntities()
        {
            {
                Context.Entities.RemoveRange(Context.Entities.ToList());
                Context.SaveChanges();
            }
        }

        [TestMethod]
        public virtual void SkipTakeDuplicatedDate()
        {
            {
                for (int i = 0; i < 30; i++)
                    Context.Entities.Add(new Entity() { Description = i.ToString(), Date = new DateTime(1969, 09, 15)});

                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();

            {
                var entities = Context.Entities.OrderBy(_ => _.Date).Skip(10).Take(5).ToList();
                Assert.AreEqual(5, entities.Count);
            }

            RemoveAllEntities();
        }

        [TestMethod]
        public void SkipTakeString()
        {
            {
                for (int i = 0; i < 30; i++)
                    Context.Entities.Add(new Entity() { Description = i % 2 == 0 ? i.ToString(): (-i).ToString() });

                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();

            {
                foreach (Entity entity in Context.Entities.ToList())
                    Console.WriteLine(entity.Description);
            }

            base.DisposeContext();
            base.CreateContext();

            {
                var entities = Context.Entities.OrderBy(_ => _.Description).Skip(10).Take(5).ToList();
                foreach (Entity entity in entities)
                    Console.WriteLine(entity.Description);
                Assert.AreEqual(5, entities.Count);
                for (int i = 0; i < entities.Count - 1; i++)
                {
                    Entity entity = entities[i];
                    Assert.AreEqual(-1, String.Compare(entity.Description , entities[i + 1].Description));
                }
            }

            RemoveAllEntities();
        }



        [TestMethod]
        public void SkipTakeDuplicatedString()
        {
            {
                for (int i = 0; i < 30; i++)
                    Context.Entities.Add(new Entity() { Description = "This is the same old song" });

                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();

            {
                var entities = Context.Entities.OrderBy(_ => _.Description).Skip(10).Take(5).ToList();
                Assert.AreEqual(5, entities.Count);
                foreach (Entity entity in Context.Entities.ToList())
                    Assert.AreEqual("This is the same old song", entity.Description);
            }

            RemoveAllEntities();
        }


        [TestMethod]
        public void SkipTakeDouble()
        {
            {
                for (int i = 0; i < 30; i++)
                    Context.Entities.Add(new Entity() { Value = i % 2 == 0 ? i : -i });

                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();

            {
                foreach (Entity entity in Context.Entities.ToList())
                    Console.WriteLine(entity.Value);
            }

            base.DisposeContext();
            base.CreateContext();

            {
                var entities = Context.Entities.OrderBy(_ => _.Value).Skip(10).Take(5).ToList();
                Assert.AreEqual(5, entities.Count);
                for (int i = 0; i < entities.Count - 1; i++)
                {
                    Entity entity = entities[i];
                    Assert.IsTrue(entity.Value < entities[i + 1].Value);
                }
            }

            RemoveAllEntities();
        }
    }
}
