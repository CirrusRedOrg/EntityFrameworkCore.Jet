using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model12_ComplexType
{



    [Table("Friend12")]
    public class Friend
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public FullAddress Address { get; set; } = new();
    }

    [Table("LessThanFriend12")]
    public class LessThanFriend
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public CityAddress Address { get; set; } = new();
    }


    public class CityAddress
    {
        public string Cap { get; set; }
        public string City { get; set; }
    }

    public class FullAddress
    {
        public string Cap { get; set; }
        public string City { get; set; }
        public string Street { get; set; }
    }


    /*
    Actually complex types cannot inherit from other types
    public class FullAddress : CityAddress
    {
        public string Street { get; set; }
    }
    */
}
