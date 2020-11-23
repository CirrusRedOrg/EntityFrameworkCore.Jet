using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model52_Requires_HasFalse_LogicalDelete
{
    [Table("PanelLookup")]
    public class PanelLookup
    {
        [Column("Id")]
        public int Id { get; set; }

        [Column("Name")]
        public string Name { get; set; }

        public virtual ICollection<PanelTexture> PanelTextures { get; set; }
    }
}
