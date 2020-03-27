using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model21_CommandInterception
{
    [TestClass]
    public class Model21_CommandInterceptionJetTest : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.GetJetConnection();
        }
    }
}
