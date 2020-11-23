using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model13_TableSplit_1_1rel
{
    [Table("Addresses13")]
    public class Address
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public string Description { get; set; }

        public virtual Visit Visit { get; set; }
    }

    [Table("Visits13")]
    public class Visit
    {
        public Visit()
        {
            Address = new Address();
        }

        [Key]
        [ForeignKey("Address")]
        public int Id { get; set; }

        public string Description { get; set; }

        public virtual Address Address { get; set; }
    }
}
