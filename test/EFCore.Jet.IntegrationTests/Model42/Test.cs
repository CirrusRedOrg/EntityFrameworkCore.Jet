using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model42
{
    public abstract class Test
    {

        protected abstract DbConnection GetConnection();

        [TestMethod]
        public void Run()
        {
            using (DbConnection connection = GetConnection())
            using (var context = new Context(connection))
            {
                // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                context.Foos.Count();
                //context.Applicants.AsQueryable().Where("Ciao");

                IQueryable<Foo> query = context.Foos.AsQueryable();
                List<int> ids = new List<int>();
                ids.AddRange(new[] { 3, 2, 1 });
                bool first = true;
                foreach (int idCopy in ids)
                {
                    int id1 = idCopy;
                    if (first)
                    {
                        query = query.Where(_ => _.FooId == id1);
                        first = false;
                    }
                    else
                    {
                        query = query.Union(context.Foos.Where(_ => _.FooId == id1));
                    }
                }
                // ReSharper disable once UnusedVariable
                var a = query.ToList();
            }
        }
    }
}
