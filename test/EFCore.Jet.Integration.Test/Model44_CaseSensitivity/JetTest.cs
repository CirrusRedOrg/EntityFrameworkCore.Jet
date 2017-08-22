using System;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model44_CaseSensitivity
{
    [TestClass]
    public class Model44_CaseSensitivityJetTest : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.GetJetConnection();
        }

        [TestMethod]
        [ExpectedException(typeof(DbUpdateException))]
        public void Model44_CaseSensitivityJetTestRun()
        {
            base.Run();
        }
    }
}
