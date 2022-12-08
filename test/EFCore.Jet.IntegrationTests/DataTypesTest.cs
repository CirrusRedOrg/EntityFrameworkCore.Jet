using System;
using System.Data.Common;
using System.Linq;
using EntityFrameworkCore.Jet.IntegrationTests.Model02;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests
{
    [TestClass]
    public class DataTypesTest : TestBase<Context>
    {

        const int MYINT = 23456;

        public override void Seed()
        {

            TableWithSeveralFieldsType table = new TableWithSeveralFieldsType()
            {
                MyInt = MYINT,
                MyDouble = 12.34,
                MyString = "Another student",
                MyDateTime = new DateTime(1969, 09, 15, 20, 03, 19),
                MyBool = true,
                MyNullableBool = false

            };

            Context.TableWithSeveralFieldsTypes.Add(table);
            Context.SaveChanges();
        }

        [TestMethod]
        public void Booleans()
        {


            // ReSharper disable ReturnValueOfPureMethodIsNotUsed
            Context.TableWithSeveralFieldsTypes.Where(c => c.MyBool).Select(c => c.MyBool != false).First();
            Context.TableWithSeveralFieldsTypes.Where(c => c.MyBool).Select(c => c.MyBool).First();
            // ReSharper restore ReturnValueOfPureMethodIsNotUsed

        }

        public override void CleanUp()
        {
            Context.Dispose();
        }

        protected override DbConnection GetConnection()
        {
            return Helpers.GetJetConnection();
        }
    }
}
