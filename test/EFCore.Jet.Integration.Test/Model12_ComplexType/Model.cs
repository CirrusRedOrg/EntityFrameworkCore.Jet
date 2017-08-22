using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model12_ComplexType
{



    public class Friend
    {
        public Friend()
        {Address = new FullAddress();}

        public int Id { get; set; }
        public string Name { get; set; }

        public FullAddress Address { get; set; }
    }

    public class LessThanFriend
    {
        public LessThanFriend()
        {Address = new CityAddress();}

        public int Id { get; set; }
        public string Name { get; set; }

        public CityAddress Address { get; set; }
    }


    [ComplexType]
    public class CityAddress
    {
        public string Cap { get; set; }
        public string City { get; set; }
    }

    [ComplexType]
    public class FullAddress : CityAddress
    {
        public string Street { get; set; }
    }

}
