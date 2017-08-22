using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model49_Inheritance_EagerlyLoad
{
    public abstract class Test : TestBase<Context>
    {
        // Actually there are some bugs on ef related to inheritance and eagerly loading

        [TestMethod]
        [ExpectedException(typeof(ArgumentException), "The Include path expression")]
        public void Model49_Inheritance_EagerlyLoadRun()
        {
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            Context.A.ToList();

            string id = "25";

            Context.A
                .Where(x => x.Id.Equals(id))
                .Include(_ => _.Bases)
                .SelectMany(_ => _.Bases)
                .OfType<Base1>()
                .Select(y => y.SomeClass)
                .ToList();

            Context.A
                .Include(_ => _.Bases.OfType<Base1>()
                    .Select(y => y.SomeClass))
                .Where(x => x.Id.Equals(id))
                .ToList();
        }
    }
}
