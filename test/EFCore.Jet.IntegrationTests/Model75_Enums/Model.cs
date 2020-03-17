using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model75_Enums
{

    [Table("NTT75")]
    public class Entity
    {
        public int Id { get; set; }

        public EnumDataType EnumDataType { get; set; }
    }

    public enum EnumDataType
    {
        a,
        b,
        c
    }
}
