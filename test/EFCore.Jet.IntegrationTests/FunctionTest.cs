/*
Priority low test
*/
using System;
using System.Linq;
using EFCore.Jet.Integration.Test.Model02;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test
{
    [TestClass]
    class FunctionTest
    {
        [TestMethod]
        public void Run()
        {
            using (var context = new Context(SetUpCodeFirst.Connection))
            {
                TableWithSeveralFieldsType table = new TableWithSeveralFieldsType()
                {
                    MyInt = 10,
                    MyString = " My current string with leading and trailing spaces ",
                    MyDateTime = new DateTime(1969, 09, 15, 20, 03, 19)
                };

                context.TableWithSeveralFieldsTypes.Add(table);
                context.SaveChanges();



                Console.WriteLine(context.TableWithSeveralFieldsTypes.Select(c => new { c.MyDateTime.Day }).First().Day);
#pragma warning disable 618
                Console.WriteLine(context.TableWithSeveralFieldsTypes.Select(c => new { Date = EntityFunctions.AddDays(c.MyDateTime, 4) }).First());
                Console.WriteLine(context.TableWithSeveralFieldsTypes.Select(c => new { ElapsedDays = EntityFunctions.DiffDays(c.MyDateTime, c.MyDateTime) }).First().ElapsedDays.Value);
#pragma warning restore 618

                // ReSharper disable StringIndexOfIsCultureSpecific.1
                Console.WriteLine(context.TableWithSeveralFieldsTypes.Select(c => c.MyString.IndexOf(CanonicalFunctionsTest1.MyStringValue.Substring(5, 4))).First());
                // ReSharper restore StringIndexOfIsCultureSpecific.1


                Console.WriteLine(context.TableWithSeveralFieldsTypes.Where(c => c.MyString.Contains(CanonicalFunctionsTest1.MyStringValue.Substring(3, 5))).First());
                Console.WriteLine(context.TableWithSeveralFieldsTypes.Where(c => c.MyString.StartsWith(CanonicalFunctionsTest1.MyStringValue.Substring(3, 5))).FirstOrDefault());
                Console.WriteLine(context.TableWithSeveralFieldsTypes.Where(c => c.MyString.StartsWith(CanonicalFunctionsTest1.MyStringValue.Substring(0, 5))).First());
                string stringEnd = CanonicalFunctionsTest1.MyStringValue.Substring(CanonicalFunctionsTest1.MyStringValue.Length - 5, 5);
                //Console.WriteLine(context.TableWithSeveralFieldsTypes.Where(c => c.MyString.EndsWith(CanonicalFunctionsTest.MYSTRINGVALUE.Substring(CanonicalFunctionsTest.MYSTRINGVALUE.Length - 5, 5))).First());
                Console.WriteLine(context.TableWithSeveralFieldsTypes.Where(c => c.MyString.EndsWith(stringEnd)).First());

            }
        }
    }
}
