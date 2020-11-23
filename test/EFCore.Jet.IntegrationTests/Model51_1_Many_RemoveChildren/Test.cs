using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model51_1_Many_RemoveChildren
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Run()
        {
            {
                Blog blog = new Blog()
                {
                    Name = "MyBlog"
                };

                blog.Posts.Add(new Post() { Title = "Title1" });
                blog.Posts.Add(new Post() { Title = "Title2" });

                Context.Blogs.Add(blog);
                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();

            {
                Blog blog = Context.Blogs.First();
                Console.WriteLine(blog.Posts.Count);
                Context.Posts.RemoveRange(blog.Posts);
                Console.WriteLine(blog.Posts.Count);
            }
        }
    }
}
