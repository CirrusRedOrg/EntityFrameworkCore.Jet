using System;
using System.Data.Common;
using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.IntegrationTests.Model01;

namespace EntityFrameworkCore.Jet.IntegrationTests
{
    class Program
    {
        static void Main(string[] args)
        {

            //Console.SetWindowSize(210, 80);

            // This is the only reason why we need to include the provider
            JetConfiguration.ShowSqlStatements = true;

            DbConnection connection = Helpers.GetJetConnection();
            Context context = new Context(TestBase<Context>.GetContextOptions(connection));



            Console.WriteLine("DB First ======================================================================");

            /*
            //EntityConnection ec = GetJetEntityConnection();
            EntityConnection ec = JetEntityFrameworkProvider.Test.DbFirst.SetUpDbFirst.GetJetEntityConnection();
            //EntityConnection ec = GetOleDbEntityConnection();

            // Use the Entity SQL to implement the Between operation
            IEnumerable<Course> courses = GetCoursesByEntitySQL(ec);
            ShowCourses("Get the Courses by Entity SQL", courses);
            Console.WriteLine();

            // Use the Entity SQL to implement the Between operation
            courses = GetCoursesByEntityEscapedLikeSQL(ec);
            ShowCourses("Get the Courses by Entity Escaped Like % SQL", courses);
            Console.WriteLine();

            // Use the extension method to implement the Between operation
            courses = GetCoursesByExtension(ec);
            ShowCourses("Get the Courses by extension method", courses);
            Console.WriteLine();

            //School school = new School(ec);
            //Course course = school.Courses.First(c => (c.DepartmentID BAND 3) == 3);


            Console.WriteLine("Code First ======================================================================");

            Student student;
            student = new Student() { StudentName = "New Student 1" };
            context.Students.Add(student);
            student = new Student() { StudentName = "New Student 2" };
            context.Students.Add(student);
            context.SaveChanges();

            // Add a student to update
            student = new Student() { StudentName = "Student to update" };
            context.Students.Add(student);
            context.SaveChanges();
            int studentId = student.StudentID;

            // Retrieve the student
            student = context.Students.Where(s => s.StudentID == studentId).First();

            // Update the student
            student.StudentName = "Student updated";
            context.SaveChanges();

            // Retrieve the student and check that is the right student
            student = context.Students.Where(s => s.StudentName == "Student updated").First();
            if (student.StudentID != studentId)
                Console.WriteLine("Student save or retrieve error");

            // Delete the student
            context.Students.Remove(student);
            context.SaveChanges();

            // Try to retrieve the student
            student = context.Students.Where(s => s.StudentName == "Student updated" || s.StudentID == studentId).FirstOrDefault();
            if (student != null)
                Console.WriteLine("Student not deleted");
            */

            Console.WriteLine("Function test ======================================================================");


            /*           
            // Retrieve some oledb schema infos
            jetConnection.Open();

            //Console.SetOut(new StreamWriter("C:\\Temp\\Tables.txt"));

            DataTable schemaTable = ((OleDbConnection)jetConnection).GetOleDbSchemaTable(
              System.Data.OleDb.OleDbSchemaGuid.Tables,
              new object[] { null, null, null, null });
            JetProviderFactory.JetStoreSchemaDefinitionRetrieveTest.ShowDataTableContent(schemaTable);


            //Console.SetOut(new StreamWriter("C:\\Temp\\Columns.txt"));
            
            schemaTable = ((OleDbConnection)jetConnection).GetOleDbSchemaTable(
              System.Data.OleDb.OleDbSchemaGuid.Columns,
              new object[] { null, null, null, null });
            JetProviderFactory.JetStoreSchemaDefinitionRetrieveTest.ShowDataTableContent(schemaTable);
            */


            Console.WriteLine("Boolean materialization ===========================================================");




            context.Dispose();


            Console.WriteLine("Press any key to exit.....");
            Console.ReadKey();
        }




    }
}
