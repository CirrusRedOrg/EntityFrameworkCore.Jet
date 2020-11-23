using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model70_InMemoryObjectPartialUpdate
{

    [Table("Item70")]
    public class Item
    {
        public int Id { get; set; }
        [MaxLength(50)]
        public string ColumnA { get; set; }
        [MaxLength(50)]
        public string ColumnB { get; set; }
    }
}
