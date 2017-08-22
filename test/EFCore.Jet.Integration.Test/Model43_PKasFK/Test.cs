using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model43_PKasFK
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Model43_PKasFKRun()
        {
            Context.AddOrUpdate("Test",
                new Parent
                {
                    Name = "Test",
                    Children = new List<Child>
                    {
                            new Child {ChildName = "TestChild"},
                            new Child {ChildName = "NewChild"}
                    }
                });

        }
    }
}
