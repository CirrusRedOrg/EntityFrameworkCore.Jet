using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace EFCore.Jet.Integration.Test.Model49_Inheritance_EagerlyLoad
{
    public class A
    {
        public string Id { get; set; }
        public IList<Base> Bases { get; set; }
    }

    public abstract class Base
    {
        [Key]
        public int BaseId { get; set; }
        public string Name { get; set; }
    }

    public abstract class Base1 : Base
    {
        public string SomeClass { get; set; }
    }

    public class Base2 : Base1
    {

    }

    public class Base3 : Base1
    {
        public string SomeOtherClass { get; set; }
    }


}
