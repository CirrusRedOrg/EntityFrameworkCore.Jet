namespace EntityFrameworkCore.Jet.IntegrationTests.Model08
{
    public class File
    {
        public int Id { get; private set; } // PK
        // Other properties
        public string Description { get; set; }
    }

    public class GalleryImage : File
    {
        public string A { get; set; }
    }

    public class PageImage : File
    {
        public string B { get; set; }
    }
}