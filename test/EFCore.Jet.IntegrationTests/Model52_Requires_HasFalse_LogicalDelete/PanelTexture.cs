using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model52_Requires_HasFalse_LogicalDelete
{

    [Table("PanelTexture")]
    public class PanelTexture
    {
        [Column("PanelId")]
        public int PanelId { get; set; }

        public virtual PanelLookup PanelLookup { get; set; }

        [Column("TextureId")]
        public int TextureId { get; set; }

        public bool IsInterior { get; set; }

        public virtual Texture Texture { get; set; }
    }
}
