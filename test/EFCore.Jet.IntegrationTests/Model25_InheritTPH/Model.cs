using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model25_InheritTPH
{
    //Not mapped to table, has all fields apart from foreign key
    public abstract class FirstBaseModel
    {
        public int Id { get; set; }
    }

    //Mapped to a table, has foreign key (eg. customerId)
    [Table("MyTable")]
    public class Derived1Model : FirstBaseModel
    {
        public string D1 { get; set; }
    }

    //Mapped to a different table, has foreign key (eg. companyId)
    [Table("MyDifferentTable")]
    public class Derived2Model : FirstBaseModel
    {
        public string D2 { get; set; }
    }

    //Mapped to the same table as Derived2Model
    [Table("MyDifferentTable")]
    public class Derived3Model : Derived2Model
    {
        public string D3 { get; set; }
    }
}
