using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model43_PKasFK
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
                    Children =
                    [
                        new Child { ChildName = "TestChild" },
                        new Child { ChildName = "NewChild" }
                    ]
                });

        }
    }
}
