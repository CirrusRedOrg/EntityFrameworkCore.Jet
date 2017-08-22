using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model37_2Contexts
{
    [TestClass]
    public class Model37_2ContextsSqlCeTest : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.GetSqlCeConnection();
        }
    }
}
