using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model04
{
    [TestClass]
    public class Model04SqlCeTest : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.GetSqlCeConnection();
        }
    }
}
