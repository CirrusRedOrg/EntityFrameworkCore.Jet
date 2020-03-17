using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

// ReSharper disable VirtualMemberCallInConstructor

namespace EFCore.Jet.Integration.Test.Model71_MasterDetail
{
    [Table("Master71")]
    public class Master
    {
        public Master()
        {
            Details = new DetailCollection();
        }


        public int Id { get; set; }
        [MaxLength(50)]
        public string Description { get; set; }

        public virtual DetailCollection Details { get; set; }
    }

    public class DetailCollection : List<Detail>
    {
        public void Add(string description)
        {
            Add(new Detail() { Description = description });
        }
    }


    [Table("Detail71")]
    public class Detail
    {
        public int Id { get; set; }

        [Required]
        [ForeignKey("Master")]
        public int Master_Id { get; set; }

        [ForeignKey("Master_Id")]
        public virtual Master Master { get; set; }

        [MaxLength(50)]
        public string Description { get; set; }
    }


}
