using System;
using System.Data.Common;
using System.Data.Jet;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model05_WithIndex
{
    [TestClass]
    public class Model05JetTest : Test
    {

        protected override DbConnection GetConnection()
        {
            return new JetConnection(JetConnection.GetConnectionString("SystemTables.accdb"));
        }
    }
}
