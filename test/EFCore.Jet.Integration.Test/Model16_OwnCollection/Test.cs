using System;
using System.Data;
using System.Data.Common;
using System.Data.OleDb;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model16_OwnCollection
{
    public abstract class Test : TestBase<BloggingContext>
    {
        public override void Seed()
        {
            for (int i = 0; i < 100; i++)
            {
                Blog blog = new Blog { Name = "MyNewBlog " + i };
                blog.Posts.Add("My1stPost", "Not really interesting post " + i * 10 + 1);
                blog.Posts.Add("My2ndPost", "Not really interesting post " + i * 10 + 1);
                blog.Posts.Add("My3rdPost", "Not really interesting post " + i * 10 + 1);

                Context.Blogs.Add(blog);
                Context.SaveChanges();
            }

        }

        [TestMethod]
        public void Run()
        {
            int blogId = Context.Blogs.First().BlogId;

            base.DisposeContext();
            base.CreateContext();
            {
                //var query = Context.Blogs.Where(b => b.BlogId == 25);
                //DataTable dataTable = ToDataTable(connection, query);

                Blog blog = Context.Blogs.Find(blogId);
                foreach (Post post in blog.Posts)
                    Console.WriteLine("{0} - {1}", post.Title, post.Content);
            }
        }


        [TestMethod]
        [ExpectedException(typeof(DbUpdateConcurrencyException))]
        public void UpdateOnUpdatedConcurrencyTest()
        {
            var firstPost = Context.Posts.First();
            

            var firstBlog = Context.Blogs.First();
            Context.Database.ExecuteSqlCommand(
                "UPDATE Blogs SET Name = 'Another Name' WHERE BlogId = " + firstBlog.BlogId);
            firstBlog.Name = "Changed";
            Context.SaveChanges();
        }

        [TestMethod]
        [ExpectedException(typeof(DbUpdateConcurrencyException))]
        public void UpdateOnDeletedConcurrencyTest()
        {
            var firstBlog = Context.Blogs.First();
            Context.Database.ExecuteSqlCommand(
                "DELETE FROM Blogs WHERE BlogId = " + firstBlog.BlogId);
            firstBlog.Name = "Changed";
            Context.SaveChanges();
        }

        [TestMethod]
        [ExpectedException(typeof(DbUpdateConcurrencyException))]
        public void DeleteOnUpdatedConcurrencyTest()
        {
            var firstBlog = Context.Blogs.First();
            Context.Database.ExecuteSqlCommand(
                "UPDATE Blogs SET Name = 'Another Name2' WHERE BlogId = " + firstBlog.BlogId);
            Context.Blogs.Remove(firstBlog);
            Context.SaveChanges();
        }


        [TestMethod]
        [ExpectedException(typeof(DbUpdateConcurrencyException))]
        public void DeleteOnDeletedConcurrencyTest()
        {
            var firstBlog = Context.Blogs.First();
            Context.Database.ExecuteSqlCommand(
                "DELETE FROM Blogs WHERE BlogId = " + firstBlog.BlogId);
            Context.Blogs.Remove(firstBlog);
            Context.SaveChanges();
        }



        private static DataTable ToDataTable(DbConnection connection, IQueryable query)
        {

            if (query == null)
                throw new ArgumentNullException("query");
            if (connection == null)
                throw new ArgumentNullException();

            string sqlQuery = query.ToString();

            DbCommand command = connection.CreateCommand();
            command.CommandText = sqlQuery;
            // Get the right one or get it from a factory
            DbDataAdapter dataAdapter = new OleDbDataAdapter();
            dataAdapter.SelectCommand = (OleDbCommand)command;

            DataTable dataTable = new DataTable("sd");

            try
            {
                command.Connection.Open();
                dataAdapter.Fill(dataTable);
            }
            finally
            {
                command.Connection.Close();
            }

            return dataTable;
        }

    }
}
