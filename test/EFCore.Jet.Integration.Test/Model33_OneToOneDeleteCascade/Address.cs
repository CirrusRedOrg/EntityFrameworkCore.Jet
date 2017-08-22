using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model33_OneToOneDeleteCascade
{
    [Table("Address33")]
    public class Address
    {
        public int Id { get; set; }

        public string AddressLines1 { get; set; }

        public string AddressLines2 { get; set; }

        public string AddressLines3 { get; set; }

        public string PostCode { get; set; }

        public virtual Person Person { get; set; } // needed for one to one?
    }
}
