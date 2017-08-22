using System;
using System.Data.Common;
using System.Linq;
using EFCore.Jet.Integration.Test.Model01;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test
{
    [TestClass]
    public class CanonicalFunctionsTest2 : TestBase<Context>
    {

        [TestMethod]
        public void CastToBool()
        {
            Standard standard = new Standard() { StandardName = "Another Standard" };
            Context.Standards.Add(standard);
            Context.SaveChanges();

            Assert.IsTrue(Context.Standards.Select(c => new {MyNewProperty = true }).ToList().Count > 0);
            Context.Dispose();
        }

        [TestMethod]
        public void InClause()
        {
            Standard standard = new Standard() { StandardName = "Standard used in student in clause" };
            Student student;
            Context.Standards.Add(standard);
            Context.SaveChanges();
            student = new Student() { StudentName = "Student 1 related to standard in clause", Standard = standard };
            Context.Students.Add(student);
            student = new Student() { StudentName = "Student 2 related to standard in clause", Standard = standard };
            Context.Students.Add(student);
            Context.SaveChanges();

            Assert.IsNotNull(Context.Students.Where(s => Context.Standards.Contains(s.Standard)).First());
            Assert.IsNotNull(Context.Students.Where(s => (new[] {1,2,3,4}).Contains(s.StudentId)).First());
            Context.Dispose();
        }

        [TestMethod]
        public void NotInClause()
        {
            // ReSharper disable ReturnValueOfPureMethodIsNotUsed
            Context.Students.Where(s => !(new[] {1, 2, 3, 4}).Contains(s.StudentId)).FirstOrDefault();
            // ReSharper restore ReturnValueOfPureMethodIsNotUsed
        }

        protected override DbConnection GetConnection()
        {
            return Helpers.GetJetConnection();
        }
    }
}
