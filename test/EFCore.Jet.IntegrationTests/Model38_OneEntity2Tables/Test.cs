using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model38_OneEntity2Tables
{
    public abstract class Test
    {
        protected abstract DbConnection GetConnection();

        [TestMethod]
        public void Run()
        {
            using (DbConnection connection = GetConnection())
            using (var context = new Context(connection))
            {
                Student student1 = new Student()
                {
                    StudentName = "Student1"
                };
                context.Students.Add(student1);

                Student student2 = new Student()
                {
                    StudentName = "Student2",
                    Weight = 60,
                    DateOfBirth = new DateTime(1969, 09, 15)
                };
                context.Students.Add(student2);

                context.SaveChanges();

            }
        }
    }
}
