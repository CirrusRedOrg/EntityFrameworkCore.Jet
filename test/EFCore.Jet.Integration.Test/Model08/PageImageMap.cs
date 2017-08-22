using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFCore.Jet.Integration.Test.Model08
{
    public class PageImageMap : IEntityTypeConfiguration<PageImage>
    {
        public void Configure(EntityTypeBuilder<PageImage> builder)
        {
            // Primary Key
            builder.HasKey(t => t.Id);

            // Table & Column Mappings
            builder.ToTable("PageImages");
            builder.Property(t => t.Id).HasColumnName("Id");
            // Other properties go here
        }

    }
}