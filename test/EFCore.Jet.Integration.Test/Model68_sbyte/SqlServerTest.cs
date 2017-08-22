using System;
using System.Data.Common;

namespace EFCore.Jet.Integration.Test.Model68_sbyte
{
    //[TestClass]
    public class Model68_SByte_SqlServer : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.GetSqlServerConnection();
        }
    }
}
