using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model76_FullCreate
{
    [Table("NTT76")]
    public class MyEntity
    {
        public int Id { get; set; }
        [MaxLength(50)]
        public string Description { get; set; }
    }
}
