using System;

namespace EFCore.Jet.Integration.Test.Model06_Inherit
{
    public class Address : BaseEntity
    {
        //[JsonProperty("street")]
        public string Street { get; set; }

        //[JsonProperty("city")]
        public string City { get; set; }

        //[JsonProperty("zipcode")]
        public string ZipCode { get; set; }

        //[JsonProperty("country")]
        public string Country { get; set; }
    }
}
