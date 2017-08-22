using System;
using System.ComponentModel.DataAnnotations;

namespace EFCore.Jet.Integration.Test.Model47_200
{
    public class Dept
    {
        [Key]
        public int DeptId { get; set; }

        public Emp Manager { get; set; }

        public string DeptName { get; set; }
    }

    public class Emp
    {
        [Key]
        public int EmpId { get; set; }

        public Dept Department { get; set; }

        public string Name { get; set; }
    }
}
