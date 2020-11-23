using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model65_InMemoryObjects
{
    [Table("Items65")]
    public class Item
    {
        public int Id { get; set; }
        [MaxLength(50)]
        public string Description { get; set; }

        public virtual Item ReferencedItem { get; set; }
    }

}
