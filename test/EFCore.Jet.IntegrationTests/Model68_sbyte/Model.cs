using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model68_sbyte
{

    [Table("Infoes68")]
    public class Info
    {
        public int Id { get; set; }
        [MaxLength(50)]
        public string Description { get; set; }

        public sbyte Sbyte { get; set; }
    }
}
