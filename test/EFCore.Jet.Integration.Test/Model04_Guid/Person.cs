using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model04_Guid
{
    [Table("PersonWGuid")]
    public class Person
    {
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public Guid Id { get; set; }
        public string Name { get; set; }
        public virtual Car OwnedCar { get; set; }
    }
}
