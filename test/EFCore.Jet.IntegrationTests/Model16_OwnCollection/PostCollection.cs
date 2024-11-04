using System.Collections.ObjectModel;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model16_OwnCollection
{
    public class PostCollection : ObservableCollection<Post>
    {
        public Post Add(string title, string content)
        {
            Post post = new()
            {
                Title = title,
                Content = content
            };
            Add(post);
            return post;
        }
    }
}
