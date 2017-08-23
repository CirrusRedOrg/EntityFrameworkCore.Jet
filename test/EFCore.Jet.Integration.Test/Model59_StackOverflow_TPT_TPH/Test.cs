using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model59_StackOverflow_TPT_TPH
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Run()
        {
            {
                Context.DataCaptureActivities.Add(new DataCaptureActivity() {Description = "Description", ActivityType = ActivityType.A});
                Context.DataCaptureActivities.Add(new DataCaptureActivity() { Description = "Description", ActivityType = ActivityType.B });
                Context.SaveChanges();
            }
        }
    }
}
