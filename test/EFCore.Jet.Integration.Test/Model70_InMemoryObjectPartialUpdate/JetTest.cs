using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model70_InMemoryObjectPartialUpdate
{
    [TestClass]
    public class Model70_InMemoryObjectPartialUpdate_Jet : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.GetJetConnection();
        }
    }
}
