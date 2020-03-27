using System.ComponentModel.DataAnnotations;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model16_OwnCollection
{
    public class Post
    {
        public int PostId { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }

        public int BlogId { get; set; }
        public virtual Blog Blog { get; set; }

        [Timestamp]
        public byte[] Timestamp { get; set; }
    }
}
