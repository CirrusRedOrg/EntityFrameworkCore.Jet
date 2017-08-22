using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFCore.Jet.Integration.Test.Model08
{
    public class GalleryImageMap : IEntityTypeConfiguration<GalleryImage>
    {
        public void Configure(EntityTypeBuilder<GalleryImage> builder)
        {
            // Primary Key
            builder.HasKey(t => t.Id);
            builder.ToTable("GalleryImages");
            builder.Property(t => t.Id).HasColumnName("Id");
            // Other properties go here
        }

    }
}