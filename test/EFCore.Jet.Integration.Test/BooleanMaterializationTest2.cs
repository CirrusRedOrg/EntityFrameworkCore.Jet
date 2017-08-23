using System;
using System.Data.Common;
using System.Linq;
using EFCore.Jet.Integration.Test.Model02;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test
{
    [TestClass]
    public class BooleanMaterializationTest2 : TestBase<Context>
    {
        [TestMethod]
        public void BooleanMaterializationTest2Run()
        {
            // ReSharper disable once RedundantCast
            Console.WriteLine(Context.TableWithSeveralFieldsTypes.Select(c => new {MyNewProperty = (bool) true}).ToList().Count);
        }

        protected override DbConnection GetConnection()
        {
            return AssemblyInitialization.Connection;
        }

        public override void CleanUp()
        {
            Context.Dispose();
        }
    }
}
