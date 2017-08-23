using System;
using System.Data.Common;
using System.Linq;
using EFCore.Jet.Integration.Test.Model01;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test
{
    public abstract class DmlBaseTest : TestBase<Context>
    {
        [TestMethod]
        public void Insert()
        {

            Student student;
            student = new Student() { StudentName = "New Student 1" };
            Context.Students.Add(student);
            student = new Student() { StudentName = "New Student 2" };
            Context.Students.Add(student);
            Context.SaveChanges();

            Context.Dispose();

        }

        [TestMethod]
        public void AddUpdateDelete()
        {
            Student student;

            // Add a student to update
            student = new Student() { StudentName = "Student to update" };
            Context.Students.Add(student);
            Context.SaveChanges();
            int studentId = student.StudentId;

            base.DisposeContext();
            base.CreateContext();


            // Retrieve the student
            student = Context.Students.Where(s => s.StudentId == studentId).First();

            /*
            base.Connection.Open();
            string sql = "UPDATE [Students] SET [StudentName] = 'Student updated' WHERE [StudentId] = " + student.StudentId;
            var command = base.Connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteReader();
            */


            
            // Update the student
            student.StudentName = "Student updated";
            Context.SaveChanges();
            

            base.DisposeContext();

            // Retrieve the student and check that is the right student
            base.CreateContext();
            student = Context.Students.Where(s => s.StudentName == "Student updated").First();
            Assert.AreEqual(student.StudentId, studentId);

            // Delete the student
            Context.Students.Remove(student);
            Context.SaveChanges();
            base.DisposeContext();

            // Try to retrieve the student
            base.CreateContext();
            student = Context.Students.Where(s => s.StudentName == "Student updated" || s.StudentId == studentId).FirstOrDefault();
            Assert.AreEqual(student, null);


        }

        [TestMethod]
        public void AddOnRelationAndList()
        {
            Standard standard = new Standard() { StandardName = "Standard used in student" };
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

            Assert.AreEqual(standard.Students.Count, 2);

            foreach (Student student2 in standard.Students)
                Console.WriteLine(student2);

        }


    }
}
