using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model74_ComplexTypeContained_Github9536
{

    [Table("Friend74")]
    public class Friend
    {
        public Friend()
        {
            Address = new FullAddress();
        }

        public int Id { get; set; }
        public string Name { get; set; }

        public FullAddress Address { get; set; }
    }

    [Table("LessThanFriend74")]
    public class LessThanFriend
    {
        public LessThanFriend()
        {
            Address = new CityAddress();
        }

        public int Id { get; set; }
        public string Name { get; set; }

        public CityAddress Address { get; set; }
    }


    public class CityAddress
    {
        public string Cap { get; set; }
        public string City { get; set; }
    }


    public class FullAddress
    {
        public FullAddress()
        {
            CityAddress1 = new CityAddress();
            CityAddress2 = new CityAddress();
        }

        public string Street { get; set; }
        public CityAddress CityAddress1 { get; set; }
        public CityAddress CityAddress2 { get; set; }
    }
}
