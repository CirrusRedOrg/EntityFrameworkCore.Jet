using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model31_DoubleReference
{
    [Table("Person31")]
    public class Person //This maps to a view in the database
    {
        [Key]
        public int PersonId { get; set; }
        public string Name { get; set; }

        public virtual ICollection<PhoneNumber> PhoneNumbers { get; set; }
        public virtual PhoneNumber MainPhoneNumber { get; set; }
    }

    public class PhoneNumber //maps to a table
    {
        [Key]
        public int PersonPhoneId { get; set; }
        public string AreaCode { get; set; }
        public string PhoneNo { get; set; }

        public virtual Person Person { get; set; }
    }

}
