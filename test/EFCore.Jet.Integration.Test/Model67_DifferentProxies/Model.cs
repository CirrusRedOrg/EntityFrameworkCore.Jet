using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model67_DifferentProxies
{
    [Table("People67")]
    public class Person
    {
        public Person()
        {
            Children = new List<Person>();
        }

        public int Id { get; set; }
        [MaxLength(50)]
        public string Name { get; set; }

        public virtual Info Info { get; set; }

        [ForeignKey("Children")]
        public int? Person_Id { get; set; }

        public virtual ICollection<Person> Children { get; set; }
    }

    [Table("Infoes67")]
    public class Info
    {
        public int Id { get; set; }
        [MaxLength(50)]
        public string Description { get; set; }
    }
}
