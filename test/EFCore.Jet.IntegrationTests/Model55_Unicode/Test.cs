using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model55_Unicode
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Run()
        {

            string text1 = "òèù";
            string text2 = "﻿崩壊アンプリファー";

            {
                Context.Entities.Add(new Entity() { Description = text1 });
                Context.Entities.Add(new Entity() { Description = text2 });
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
                Assert.IsTrue(Context.Entities.SingleOrDefault(_ => _.Description == text1) != null);
                Assert.IsTrue(Context.Entities.SingleOrDefault(_ => _.Description == text2) != null);
            }
        }
    }
}
