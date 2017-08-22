using System;
using System.Collections.Generic;

namespace EFCore.Jet.Integration.Test.Model42
{
    public class Foo
    {
        public int FooId { get; set; }
        public virtual ICollection<Bar> Bars { get; set; }
    }

    public class Bar
    {
        public int BarId { get; set; }
        public virtual Foo DefaultFoo { get; set; }
        public virtual ICollection<Foo> Foos { get; set; }
    }
}
