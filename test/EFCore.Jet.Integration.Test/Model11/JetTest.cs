using System;
using System.Data.Common;

namespace EFCore.Jet.Integration.Test.Model11
{
#warning This test does not work with access because access does not support longs
    //[TestClass]
    public class Model11JetTest : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.GetJetConnection();
        }
    }
}
