using System;
using System.ComponentModel.DataAnnotations;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model02
{
    public class TableWithSeveralFieldsType
    {
        [Key]
        public int Id { get; set; }
        public int MyInt { get; set; }
        public double MyDouble { get; set; }
        public string MyString { get; set; }
        public DateTime MyDateTime { get; set; }
        public bool MyBool { get; set; }
        public bool MyNullableBool { get; set; }
    }
}
