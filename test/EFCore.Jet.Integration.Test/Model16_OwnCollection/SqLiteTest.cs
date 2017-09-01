using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model16_OwnCollection
{
    [TestClass]
    public class Model16_OwnCollectionSqliteTest : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.GetSqliteConnection();
        }
    }
}
