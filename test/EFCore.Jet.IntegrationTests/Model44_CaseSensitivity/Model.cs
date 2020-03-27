using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model44_CaseSensitivity
{
    [Table("Entities44")]
    public class Entity
    {
        [Key]
        public string Name { get; set; }

        [MaxLength(50)]
        public string Description { get; set; }
    }

}
