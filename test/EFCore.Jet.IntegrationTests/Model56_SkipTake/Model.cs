using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model56_SkipTake
{
    [Table("Model56Entity")]
    public class Entity
    {
        public int Id { get; set; }
        [MaxLength(50)]
        public string Description { get; set; }
        public DateTime? Date { get; set; }
        public double? Value { get; set; }
    }

}
