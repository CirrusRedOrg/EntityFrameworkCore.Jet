using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model15_SortInheritedColumns
{
    public abstract class BaseEntity
    {
        [Key, Column(Order = 1)]
        public int id { get; set; }

        [Column(Order = 2)]
        public int siteId { get; set; }

        [Column(Order = 5)]
        public bool online { get; set; }

        [Column(Order = 6)]
        public bool deleted { get; set; }

        [Column(Order = 7)]
        public DateTime addDate { get; set; }

        [Column(Order = 8)]
        public DateTime updateDate { get; set; }

        [Column(Order = 9)]
        public DateTime deletedDate { get; set; }
    }

    public class Brand : BaseEntity
    {
        [Column(Order = 3)]
        public string brandName { get; set; }

        [Column(Order = 4)]
        public string brandLogo { get; set; }
    }
}
