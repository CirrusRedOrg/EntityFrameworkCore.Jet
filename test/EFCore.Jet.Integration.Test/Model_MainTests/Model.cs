using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model_MainTests
{

    [Table("EntityMainTest")]
    public class Entity
    {
        public int Id { get; set; }

        public int? Integer { get; set; }
        public string String { get; set; }
        public DateTime? Date { get; set; }
    }

}
