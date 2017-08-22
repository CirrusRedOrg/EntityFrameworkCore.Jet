using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model64_Schema
{
    [Table("Jet.TableWithSchema")]
    public class Item
    {
        public int Id { get; set; }
        [MaxLength(50)]
        public string Description { get; set; }
    }
}
