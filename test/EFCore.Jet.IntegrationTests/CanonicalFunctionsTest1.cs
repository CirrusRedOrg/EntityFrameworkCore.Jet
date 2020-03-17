/*
Priority low test
Custom functions implementation
*/


using System;
using System.Linq;
using EFCore.Jet.Integration.Test.Model02;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test
{
    [TestClass]
    public class CanonicalFunctionsTest1
    {

        internal const string MyStringValue = " My current string with leading and trailing spaces ";
        internal const double MyDoubleValue = -123.456789;
        int _insertedRecordId;

        [TestInitialize]
        public void Initialize()
        {

            Context context = new Context(SetUpCodeFirst.Connection);

            TableWithSeveralFieldsType table = new TableWithSeveralFieldsType()
            {
                MyInt = 10,
                MyDouble = MyDoubleValue,
                MyString = MyStringValue,
                MyDateTime = new DateTime(1969, 09, 15, 20, 03, 19)
            };

            context.TableWithSeveralFieldsTypes.Add(table);
            context.SaveChanges();
            context.Dispose();

            _insertedRecordId = table.Id;

        }

        [TestMethod]
        public void DateTimeFunction()
        {

            Context context = new Context(SetUpCodeFirst.Connection);
            IQueryable<TableWithSeveralFieldsType> insertedRecord = GetInsertedRecordQueryable(context);

            Assert.AreEqual(insertedRecord.Select(c => new { c.MyDateTime.Year }).First().Year, 1969);
            Assert.AreEqual(insertedRecord.Select(c => new { c.MyDateTime.Month }).First().Month, 09);
            Assert.AreEqual(insertedRecord.Select(c => new { c.MyDateTime.Day }).First().Day, 15);

            Assert.AreEqual(insertedRecord.Select(c => new { c.MyDateTime.Hour }).First().Hour, 20);
            Assert.AreEqual(insertedRecord.Select(c => new { c.MyDateTime.Minute }).First().Minute, 3);
            Assert.AreEqual(insertedRecord.Select(c => new { c.MyDateTime.Second }).First().Second, 19);

            Assert.AreEqual(insertedRecord.Select(c => new { Date = DbFunctions.AddDays(c.MyDateTime, 4) }).First().Date.Value.Day, 19);
            Assert.AreEqual(insertedRecord.Select(c => new { ElapsedDays = DbFunctions.DiffDays(c.MyDateTime, c.MyDateTime) }).First().ElapsedDays.Value, 0);

            context.Dispose();

        }

        private IQueryable<TableWithSeveralFieldsType> GetInsertedRecordQueryable(Context context)
        {
            return context.TableWithSeveralFieldsTypes.Where(c => c.Id == _insertedRecordId);
        }

        [TestMethod]
        public void StringFunction()
        {
            Context context = new Context(SetUpCodeFirst.Connection);
            
            IQueryable<TableWithSeveralFieldsType> insertedRecord = GetInsertedRecordQueryable(context);

            Assert.AreEqual(insertedRecord.Select(c => c.MyString.ToLower()).First(), MyStringValue.ToLower());
            Assert.AreEqual(insertedRecord.Select(c => c.MyString.ToUpper()).First(), MyStringValue.ToUpper());
            Assert.AreEqual(insertedRecord.Select(c => c.MyString.Trim()).First(), MyStringValue.Trim());
            Assert.AreEqual(insertedRecord.Select(c => c.MyString.TrimEnd()).First(), MyStringValue.TrimEnd());
            Assert.AreEqual(insertedRecord.Select(c => c.MyString.TrimStart()).First(), MyStringValue.TrimStart());
            // ReSharper disable StringIndexOfIsCultureSpecific.1
            Assert.AreEqual(insertedRecord.Select(c => c.MyString.IndexOf(MyStringValue.Substring(5, 4))).First(), MyStringValue.IndexOf(MyStringValue.Substring(5, 4)));
            // ReSharper restore StringIndexOfIsCultureSpecific.1
            
            context.Dispose();
        }

        [TestMethod]
        public void LikeFunction()
        {
            Context context = new Context(SetUpCodeFirst.Connection);

            Assert.IsNotNull(context.TableWithSeveralFieldsTypes.Where(c => c.MyString.Contains(MyStringValue.Substring(3,5))).First());
            Assert.IsNull(context.TableWithSeveralFieldsTypes.Where(c => c.MyString.StartsWith(MyStringValue.Substring(3, 5))).FirstOrDefault());
            Assert.IsNotNull(context.TableWithSeveralFieldsTypes.Where(c => c.MyString.StartsWith(MyStringValue.Substring(0, 5))).First());
            string stringEnd = MyStringValue.Substring(MyStringValue.Length - 5, 5);
            Assert.IsNotNull(context.TableWithSeveralFieldsTypes.Where(c => c.MyString.EndsWith(stringEnd)).First());

            context.Dispose();
        }

        [TestMethod]
        public void ConcatFunction()
        {
            Context context = new Context(SetUpCodeFirst.Connection);

            IQueryable<TableWithSeveralFieldsType> insertedRecord = GetInsertedRecordQueryable(context);

            Assert.AreEqual(insertedRecord.Select(c => string.Concat(c.MyString, "abc")).First(), string.Concat(MyStringValue, "abc"));

            context.Dispose();
        }

        [TestMethod]
        public void FloatingPointFunctions()
        {
            Context context = new Context(SetUpCodeFirst.Connection);

            IQueryable<TableWithSeveralFieldsType> insertedRecord = GetInsertedRecordQueryable(context);

            Assert.AreEqual(insertedRecord.Select(c => Math.Abs(c.MyDouble)).First(), Math.Abs(MyDoubleValue));
            Assert.AreEqual(insertedRecord.Select(c => Math.Round(c.MyDouble, 3)).First(), Math.Round(MyDoubleValue, 3));
            Assert.AreEqual(insertedRecord.Select(c => Math.Truncate(c.MyDouble)).First(), Math.Truncate(MyDoubleValue));
            Assert.AreEqual(insertedRecord.Select(c => Math.Truncate(c.MyDouble)).First(), Math.Truncate(MyDoubleValue));
            context.Dispose();
        }

    }
}
