namespace EntityFrameworkCore.Jet.IntegrationTests.Model80_LongValuesOverOdbc
{
    public class Table
    {
        public int Id { get; set; }
        public string Text { get; set; }
        public byte[] Binary { get; set; }
        public Document Document { get; set; }
    }

    public class Document
    {
        public string Body { get; set; }
    }
}
