using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model24_MultiTenantApp
{
    [Table("MyEntities24")]
    public class MyEntity
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Description { get; set; }

        [MaxLength(50)]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public string TenantId { get; set; }

    }
}
