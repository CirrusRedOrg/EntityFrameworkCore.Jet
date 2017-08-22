using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model63_Time
{
    [Table("Items63")]
    public class Item
    {
        public int Id { get; set; }
        public TimeSpan? TimeSpan { get; set; }
        public DateTime? DateTime { get; set; }
    }
}
