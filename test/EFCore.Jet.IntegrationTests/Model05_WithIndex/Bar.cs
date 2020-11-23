using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model05_WithIndex
{
    public class Bar
    {
        [Key]    
        public int barID {get;set;}

        // Index are supported only with fluent API
        /*[Index("IX_UniqueWithinFoo", 0, IsUnique = true)]*/
        public int parent_FooId { get; set; }

        [ForeignKey("parent_FooId")]
        public virtual Foo parent { get; set; }
        
        /*[Index("IX_UniqueWithinFoo", 1, IsUnique = true)]*/
        public int order { get; set; }
    }
}
