using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model32_Include_NotInclude
{
    [Table("Address32")]
    public class Address
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public string Description { get; set; }

        public virtual Visit Visit { get; set; }
    }

    [Table("Visit32")]
    public class Visit
    {
        public Visit()
        {
        }

        [Key]
        [ForeignKey("Address")]
        public int Id { get; set; }

        public string Description { get; set; }

        public virtual Address Address { get; set; }
    }
}
