using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model19_1_1
{
    [Table("ClassAs19")]
    public class ClassA
    {
        public int Id { get; set; }
        public string Description { get; set; }
        public virtual ClassB ClassB { get; set; }
    }

    [Table("ClassBs19")]
    public class ClassB
    {
        public int Id { get; set; }
        public string Description { get; set; }
        public virtual ClassA ClassA { get; set; }
    }
}