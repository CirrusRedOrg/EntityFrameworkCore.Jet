using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model_MainTests
{
    //[TestClass]
    public class MainSqlServerTest : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.GetSqlServerConnection();
        }
    }
}
