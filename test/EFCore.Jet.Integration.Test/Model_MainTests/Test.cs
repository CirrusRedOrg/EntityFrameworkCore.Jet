using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
// ReSharper disable ReturnValueOfPureMethodIsNotUsed

namespace EFCore.Jet.Integration.Test.Model_MainTests
{
    public abstract class Test : TestBase<TestContext>
    {
        public override void Seed()
        {
            Context.Entities.AddRange(
                new Entity() { Integer = 20, String = "aaa" },
                new Entity() { Integer = 30, String = "aaa" },
                new Entity() { Integer = 40, String = "ccc" });

            Context.SaveChanges();
        }

        [TestMethod]
        public void Cast_Coalesce()
        {

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

        [TestMethod]
        public void Any()
        {
            Assert.IsTrue(Context.Entities.Any(c => c.String.StartsWith("A")));
        }


        [TestMethod]
        public void BigCount()
        {
            Assert.IsNotNull(Context.Entities.LongCount());
        }


        [TestMethod]
        public void SelectMany_cartesian_product_with_ordering()
        {
            var result = from c in Context.Entities
                from e in Context.Entities
                where c.String == e.String
                orderby e.Integer, c.String descending
                select new {c, e.Integer};
            result.ToList();
        }

        //[TestMethod]
        public void Take_with_single()
        {
            // In this case the generated query is shown below
            // Executing this query, JET returns 2 records
            /*
            SELECT TOP 2 [t].*
                FROM(
                    SELECT TOP 1 [c].[Id], [c].[Date], [c].[Integer], [c].[String]
                    FROM [EntityMainTest] AS[c]
                    ORDER BY [c].[String]
                ) AS [t]
            ORDER BY [t].[String]
            */
            Context.Entities.OrderBy(c => c.String).Take(1).Single();
        }

        [TestMethod]
        public void Join_same_collection_multiple()
        {
            var cs1 = Context.Entities;
            var cs2 = Context.Entities;
            var cs3 = Context.Entities;

            cs1.Join(cs2, o => o.String, i => i.String, (c1, c2) => new {c1, c2}).Join(cs3, o => o.c1.String, i => i.String, (c12, c3) => c3).ToList();
        }

        [TestMethod]
        public void OrderBy_Skip_Count()
        {
            Context.Entities.OrderBy(c => c.String).Skip(7).Count();
        }
        //OrderBy_LongSkipCount

        //[TestMethod]
        public void OrderBy_correlated_subquery_lol()
        {
            // After the issue IIf => true or false has been removed
            // The generated query is below.
            // The query does not work in Access
            //
            // Now the original behaviour has been restored because of other test that stop work
            /*
            SELECT [c].[Id], [c].[Date], [c].[Integer], [c].[String]
            FROM [EntityMainTest] AS [c]
            ORDER BY (
                SELECT EXISTS (
                    SELECT 1
                    FROM [EntityMainTest] AS [c2]
                    WHERE ([c2].[String] = [c].[String]) OR ([c2].[String] IS NULL AND [c].[String] IS NULL))
                FROM (SELECT COUNT(*) FROM MSysAccessStorage)
            )
            */
            (from c in Context.Entities
                orderby Context.Entities.Any(c2 => c2.String == c.String)
                select c).ToList();
        }



        [TestMethod]
        public void Select_nested_collection()
        {
            var cs = Context.Entities;
            var os = Context.Entities;
            (from c in cs
                    where c.String == "London"
                    orderby c.Integer
                    select os
                        .Where(
                            o => o.Integer == c.Integer
                                 && o.Date.Value.Year == 1997)
                        .Select(o => o.Integer)
                        .OrderBy(o => o))
                .ToList();
        }

        [TestMethod]
        public void Select_nested_deep()
        {
            var cs = Context.Entities;
            var os = Context.Entities;
            (from c in cs
                where c.String == "London"
                orderby c.Integer
                select (from o1 in os
                    where o1.Integer == c.Integer
                          && o1.Date.Value.Year == 1997
                    orderby o1.Integer
                        select (from o2 in os
                        where o1.Integer == c.Integer
                            orderby o2.Integer
                            select o1.Integer))
                )
                .ToList();
        }

        [TestMethod]
        public void Select_nested_collection_in_anonymous_type()
        {
            (from c in Context.Entities
                where c.String == "ALFKI"
                select new
                {
                    CustomerId = c.Integer,
                    OrderIds
                    = Context.Entities.Where(
                            o => o.Integer == c.Integer
                                 && o.Date.Value.Year == 1997)
                        .Select(o => o.Integer)
                        .OrderBy(o => o),
                    Customer = c

                }).ToList();
        }



        [TestMethod]
        public void Skip()
        {
            var cs = Context.Entities;
            cs.OrderBy(c => c.Integer).Skip(5).ToList();
            cs.OrderBy(c => c.String).Skip(5).ToList();
        }

        [TestMethod]
        public void Skip_Distinct()
        {
            var cs = Context.Entities;
            cs.OrderBy(c => c.Integer).Skip(5).Distinct().ToList();
        }

        [TestMethod]
        public void Skip_Take_Distinct()
        {
            var cs = Context.Entities;
            cs.OrderBy(c => c.Integer).Skip(5).Take(10).Distinct().ToList();
        }


        [TestMethod]
        public void Take_Skip_Distinct()
        {
            var cs = Context.Entities;
            cs.OrderBy(c => c.Integer).Take(10).Skip(5).Distinct().ToList();
        }

        [TestMethod]
        public void Take_Skip()
        {
            var cs = Context.Entities;
            cs.OrderBy(c => c.Integer).Take(10).Skip(5).ToList();
        }


        [TestMethod]
        public void Where_chain()
        {
            Context.Entities
                .Where(o => o.String == "QUICK")
                .Where(o => o.Date > new DateTime(1998, 1, 1)).ToList();
        }
    }
}
