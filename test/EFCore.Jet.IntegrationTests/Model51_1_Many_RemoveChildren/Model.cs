using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model51_1_Many_RemoveChildren
{
    [Table("Blogs51")]
    public class Blog
    {
        public Blog()
        {
            Posts = new List<Post>();
        }

        public int Id { get; set; }
        public string Name { get; set; }
        public virtual ICollection<Post> Posts { get; set; }
    }

    [Table("Posts51")]
    public class Post
    {
        public int Id { get; set; }

        // foreign key to Blog:
        public int BlogId { get; set; }
        public virtual Blog Blog { get; set; }

        public string Title { get; set; }
        public string Text { get; set; }
    }
}
