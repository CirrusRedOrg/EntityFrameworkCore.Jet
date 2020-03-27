using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model53_TableSplitting
{
    public class Person
    {
        public int PersonId { get; set; }
        public string Name { get; set; }
        public virtual Address Address { get; set; }
    }
    public class Address
    {
        public Int32 PersonId { get; set; }
        public string Province { get; set; }
        public virtual Person Person { get; set; }
        public virtual City City { get; set; }

    }

    public class City
    {
        public Int32 CityID { get; set; }
        public string Name { get; set; }
    }


}
