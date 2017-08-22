using System;

namespace EFCore.Jet.Integration.Test.Model38_OneEntity2Tables
{
    public class Student
    {
        public int Id { get; set; }
        public string StudentName { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public byte[] Photo { get; set; }
        public decimal? Height { get; set; }
        public float? Weight { get; set; }
    }

}
