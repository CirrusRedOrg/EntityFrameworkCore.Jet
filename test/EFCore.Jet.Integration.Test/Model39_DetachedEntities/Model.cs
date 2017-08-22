using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace EFCore.Jet.Integration.Test.Model39_DetachedEntities
{
    public class Grade
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [Required]
        public float Quantity { get; set; }

        public ICollection<GradeWidth> GradeWidths { get; set; }
    }

    public class GradeWidth
    {
        public int Id { get; set; }
        public int Width { get; set; }
    }
}
