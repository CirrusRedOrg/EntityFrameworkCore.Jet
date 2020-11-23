using System;
using System.Data.Common;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model37_2Contexts
{
    public abstract class Test
    {
        protected abstract DbConnection GetConnection();

        [TestMethod]
        public void Model37_2ContextsRun()
        {
            using (DbConnection connection = GetConnection())
            {
                using (var Context = new Context1(connection))
                {
                    Context.MyEntities.Count();
                }

                using (var Context = new Context2(connection))
                {
                    Context.MyEntities.Count();
                    Context.MyEntities.Where(_ => _.Description2.Contains("a"))
                        .Count();
                }

                using (var Context = new Context2(connection))
                {
                    Context.MyEntities.Where(_ => _.Description2.Contains("a"))
                        .Count();
                }
            }
        }

#pragma warning disable 649
        private Context1 Context;
#pragma warning restore 649

        public interface IMyEntity
        {
            int Id { get; set; }
        }

        public int SaveItem<T>(T item)
            where T : class, IMyEntity
        {
            // Non generic dbsets are not supported.
            // The new implementation moved the Attach to the Context so we don't need the set anymore
            /*
            DbSet dbSet = Context.Set(typeof(T));
            dbSet.Attach(item);
            Context.Entry(item).State = item.Id == 0 ? EntityState.Added : EntityState.Modified;
            Context.SaveChanges();
            return item.Id;
            */

            Context.Attach(item);
            Context.Entry(item)
                .State = item.Id == 0
                ? EntityState.Added
                : EntityState.Modified;
            Context.SaveChanges();
            return item.Id;
        }
    }
}