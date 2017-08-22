using System;
using System.ComponentModel.DataAnnotations;

namespace EFCore.Jet.Integration.Test.Model05_WithIndex
{
    public class Foo
    {
        [Key]
        public int FooID {get; set;}
    }
}
