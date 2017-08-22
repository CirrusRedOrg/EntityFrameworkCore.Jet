using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model33_OneToOneDeleteCascade
{
    [Table("Person33")]
    public class Person
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public int AddressId { get; set; } // needed for nav?

        public virtual Address Address { get; set; } // nav property
    }
}