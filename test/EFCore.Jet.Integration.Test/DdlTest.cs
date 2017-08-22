using System;
using System.Data.Jet;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test
{
    [TestClass]
    public class DdlTest
    {
        [TestMethod]
        public void CheckIfTablesExists()
        {
            bool exists = ((JetConnection)SetUpCodeFirst.Connection).TableExists("Students");
            Assert.IsTrue(exists);
        }
    }
}
