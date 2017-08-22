using System;
using System.Data;
using System.Data.Common;
using System.Data.OleDb;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model16_OwnCollection
{
    public abstract class Test : TestBase<BloggingContext>
    {
        [TestMethod]
        public void Run()
        {
            int blogId;

            {
                Blog blog = new Blog { Name = "MyNewBlog" };
                blog.Posts.Add("My1stPost", "Not really interesting post");
                blog.Posts.Add("My2ndPost", "Not really interesting post");
                blog.Posts.Add("My3rdPost", "Not really interesting post");

                Context.Blogs.Add(blog);
                Context.SaveChanges();
                blogId = blog.BlogId;
            }

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
