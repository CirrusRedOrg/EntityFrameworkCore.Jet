using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model77_Cast_Coalesce
{

    [Table("NTT77")]
    public class Entity
    {
        public int Id { get; set; }

        public int? Integer { get; set; }
        public string String { get; set; }

    }

}
