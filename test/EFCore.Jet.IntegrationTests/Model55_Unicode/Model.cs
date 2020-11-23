using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model55_Unicode
{
    [Table("Model55Entity")]
    public class Entity
    {
        public int Id { get; set; }
        [MaxLength(50)]
        public string Description { get; set; }
    }

}
