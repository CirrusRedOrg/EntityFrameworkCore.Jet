namespace EntityFrameworkCore.Jet.IntegrationTests.Model09
{
    public class Three
    {
        public int Id { get; set; }
        public int TwoId { get; set; }
        public int OneId { get; set; }
        public virtual Two Two { get; set; }
    }
}