using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model46_InnerClasses
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Model46_InnerClassesRun()
        {
            {
                ClassA a = new ClassA();
                a.B.b = 10;


                a.x = 10;
                a.y = 20;

                Context.ClassAs.Add(a);
                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();

            {
                ClassA a = Context.ClassAs.First();
                Assert.AreEqual(10, a.B.b);
                Assert.IsNotNull(a.C);
                Assert.IsNull(a.C.c);
            }
        }
    }
}
