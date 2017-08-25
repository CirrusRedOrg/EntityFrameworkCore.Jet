using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model43_PKasFK
{
    [Table("Parent43")]
    public class Parent
    {
        [Key]
        public string Name { get; set; }

        public virtual List<Child> Children { get; set; }
    }

    [Table("Child43")]
    public class Child
    {
        // Multiple keys can be defined only via FluentApi
        //[Key]
        [Column(Order = 1)]
        public string ParentName { get; set; }

        //[Key]
        [Column(Order = 2)]
        public string ChildName { get; set; }

        public virtual Parent Parent { get; set; }
    }

}
