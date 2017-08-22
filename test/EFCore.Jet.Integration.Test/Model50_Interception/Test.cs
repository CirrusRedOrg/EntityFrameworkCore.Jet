using System;
using System.Data.Common;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model50_Interception
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
                // With literals parameters are not generated
                context.Notes.Where(_ => _.Id == 12).Count(); // No parameters
                context.Notes.Where(_ => _.Id == 12 + 1).Count(); // No parameters
                int a = 12;
                context.Notes.Where(_ => _.Id == a).Count(); // Yes parameters
            }
        }
    }
}
