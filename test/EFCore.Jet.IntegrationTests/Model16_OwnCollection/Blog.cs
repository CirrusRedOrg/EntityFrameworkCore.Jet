using System;
using System.ComponentModel.DataAnnotations;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model16_OwnCollection
{
    public class Blog
    {
        public Blog()
        {
            Posts = new PostCollection();
        }

        public int BlogId { get; set; }
        [MaxLength(200)]
        [ConcurrencyCheck]
        public string Name { get; set; }

        public virtual PostCollection Posts { get; set; }
    }
}
