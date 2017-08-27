using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model_MainTests
{

    [Table("EntityMainTest")]
    public class Entity
    {
        public int Id { get; set; }

        public int? Integer { get; set; }
        [MaxLength(255)]
        public string String { get; set; }
        public DateTime? Date { get; set; }
    }

}
