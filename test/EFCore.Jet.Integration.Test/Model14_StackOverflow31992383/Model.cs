using System;
using System.ComponentModel.DataAnnotations;

namespace EFCore.Jet.Integration.Test.Model14_StackOverflow31992383
{
    public class Class1
    {
        [Key]
        public int Id { get; set; }

        public virtual string MyProperty { get; set; }
        // other properties 
    }

    public class Class3 : Class1
    {
        [Required]
        public override string MyProperty { get; set; }

        public string Class3Prop { get; set; }
    }
}
