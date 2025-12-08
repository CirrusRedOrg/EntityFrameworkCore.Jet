using System;
using System.Data.Common;
using System.Linq;
using EntityFrameworkCore.Jet.IntegrationTests.Model01;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests
{
    [TestClass]
    public class SqlJoinTest : TestBase<Context>
    {
        const string THESTANDARD = "SqlJoinTest Standard";

        public override void Seed()
        {
            Standard standard = new() { StandardName = THESTANDARD };
            Student student;
            Context.Standards.Add(standard);
            Context.SaveChanges();
            student = new Student() { StudentName = "Student 1 related to standard", Standard = standard };
            Context.Students.Add(student);
            student = new Student() { StudentName = "Student 2 related to standard", Standard = standard };
            Context.Students.Add(student);
            Context.SaveChanges();

            int standardId = standard.StandardId;

            standard = Context.Standards.Where(s => s.StandardId == standardId).First();

            Assert.HasCount(2, standard.Students);

            foreach (Student student2 in standard.Students)
                Console.WriteLine(student2);
            
        }

        [TestMethod]
        public void JoinTest()
        {
            Assert.HasCount(2, Context.Students.Where(s => s.Standard.StandardName == THESTANDARD).ToList());
            Assert.HasCount(2, Context.Students.Where(s => s.Standard.StandardName == THESTANDARD).Select(s => new { MyStudentName = s.StudentName, MyStudentStandardName = s.Standard.StandardName }).ToList());

        }


        protected override DbConnection GetConnection()
            => Helpers.GetJetConnection();
    }
}
