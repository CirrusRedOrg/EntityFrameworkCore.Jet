using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model74_ComplexTypeContained_Github9536
{

    [Table("Friend74")]
    public class Friend
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public FullAddress Address { get; set; } = new();
    }

    [Table("LessThanFriend74")]
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
        public string Street { get; set; }
        public CityAddress CityAddress1 { get; set; } = new();
        public CityAddress CityAddress2 { get; set; } = new();
    }
}
