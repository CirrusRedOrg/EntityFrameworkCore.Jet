using System;
using System.Data.Common;
using System.Linq;
using EntityFrameworkCore.Jet.IntegrationTests.Model01;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests
{
    [TestClass]
    public class CanonicalFunctionsTest2 : TestBase<Context>
    {

        [TestMethod]
        public void CastToBool()
        {
            Standard standard = new() { StandardName = "Another Standard" };
            Context.Standards.Add(standard);
            Context.SaveChanges();

            Assert.IsTrue(Context.Standards.Select(c => new {MyNewProperty = true }).ToList().Count > 0);
            Context.Dispose();
        }

        [TestMethod]
        public void InClause()
        {
            Standard standard = new() { StandardName = "Standard used in student in clause" };
            Student student;
            Context.Standards.Add(standard);
            Context.SaveChanges();
            student = new Student() { StudentName = "Student 1 related to standard in clause", Standard = standard };
            Context.Students.Add(student);
            student = new Student() { StudentName = "Student 2 related to standard in clause", Standard = standard };
            Context.Students.Add(student);
            Context.SaveChanges();

            Assert.IsNotNull(Context.Students.Where(s => (new[] { 1, 2, 3, 4 }).Contains(s.StudentId)).First());

            // SELECT WHERE IN SELECT NOT IMPLEMENTED
            //Assert.IsNotNull(Context.Students.Where(s => Context.Standards.Contains(s.Standard)).First());

            Assert.IsNotNull(Context.Students.First(stu => Context.Standards.Any(std => std.StandardId == stu.StudentId)));


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
