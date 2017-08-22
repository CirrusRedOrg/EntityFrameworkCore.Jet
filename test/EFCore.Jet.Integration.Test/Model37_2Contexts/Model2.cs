using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model37_2Contexts_2
{
    [Table("Module2MyEntity")]
    public class MyEntity
    {
        public int Id { get; set; }
        [MaxLength(50)]
        public string Description { get; set; }
        [MaxLength(50)]
        public string Description2 { get; set; }
    }
}
