using System;
using System.ComponentModel.DataAnnotations;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model05_WithIndex
{
    public class Foo
    {
        [Key]
        public int FooID {get; set;}
    }
}
