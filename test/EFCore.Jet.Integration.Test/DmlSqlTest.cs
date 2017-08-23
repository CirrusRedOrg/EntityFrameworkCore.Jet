using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test
{
    //[TestClass]
    public class DmlSqlTest : DmlBaseTest
    {
        protected override DbConnection GetConnection()
            => Helpers.GetSqlServerConnection();
    }
}
