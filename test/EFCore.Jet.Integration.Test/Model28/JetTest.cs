using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model28
{
    [TestClass]
    public class Model28_SimpleTestJetTest : Test
    {
        protected override DbConnection GetConnection()
        {
            return Integration.Test.Helpers.GetJetConnection();
        }
    }
}
