/*
SQL CE Test disabled
*/
using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model56_SkipTake
{
    [TestClass]
    public class Model56_SkipTake_SqlCeTest : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.GetSqlCeConnection();
        }
    }
}
