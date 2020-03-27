/*
Priority low test
Custom functions implementation
*/

using System;
using System.Linq;
using EFCore.Jet.Integration.Test.Model02;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test
{
    [TestClass]
    public class BuiltInFunctionsTest
    {
        private const string MYSTRINGVALUE = " My current string with leading and trailing spaces ";
        private const double MYDOUBLEVALUE = -123.456789;

        [TestInitialize]
        public void Initialize()
        {

            Context context = new Context(SetUpCodeFirst.Connection);

            for (int i = 0; i < 30; i++)
            {
                TableWithSeveralFieldsType table = new TableWithSeveralFieldsType()
                {
                    MyInt = new Random().Next(300),
                    MyDouble = MYDOUBLEVALUE,
                    MyString = MYSTRINGVALUE,
                    MyDateTime = new DateTime(1969, 09, 15, 20, 03, 19)
                };
                context.TableWithSeveralFieldsTypes.Add(table);
                context.SaveChanges();
            }

            context.Dispose();
        }

        [TestMethod]
        public void GroupByFunctions()
        {

            Context context = new Context(SetUpCodeFirst.Connection);

            var myGroupBy1 = context.TableWithSeveralFieldsTypes
                .GroupBy(g => g.MyString, r => r.MyInt)
                .Select(g => new { Group = g.Key, MyFirstInt = g.JetFirst() }).First();
            Console.WriteLine("{0} {1}", myGroupBy1.Group, myGroupBy1.MyFirstInt);

            var myGroupBy2 = context.TableWithSeveralFieldsTypes
                .GroupBy(g => g.MyString, r => r.MyInt)
                .Select(g => new { Group = g.Key, MyLastInt = JetFunctions.JetLast(g) }).First();
            Console.WriteLine("{0} {1}", myGroupBy2.Group, myGroupBy2.MyLastInt);

            context.Dispose();

        }

        [TestMethod]
        public void StringFunctions()
        {

            Context context = new Context(SetUpCodeFirst.Connection);

            var myGroupBy2 = context.TableWithSeveralFieldsTypes
                .GroupBy(g => g.MyString, r => r.MyInt)
                .Select(g => new { Group = JetFunctions.LCase(g.Key), MyLastInt = JetFunctions.JetLast(g) }).First();
            Console.WriteLine("{0} {1}", myGroupBy2.Group, myGroupBy2.MyLastInt);
            Assert.AreEqual(myGroupBy2.Group, myGroupBy2.Group.ToLower());

            context.Dispose();

        }


    }
}
