using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model08
{
    public class FileMap : IEntityTypeConfiguration<File>
    {
        public void Configure(EntityTypeBuilder<File> builder)
        {
            // Primary Key
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Id).ValueGeneratedOnAdd();
            builder.ToTable("Files");
            builder.Property(t => t.Id).HasColumnName("Id");
            // Other Properties go here
        }
    }

    public class PageImageMap : IEntityTypeConfiguration<PageImage>
    {
        public void Configure(EntityTypeBuilder<PageImage> builder)
        {

            // Table & Column Mappings
            builder.ToTable("PageImages");

            // In EF Core this must be configured on the base type (see below for the error message)
            // Primary Key
            //builder.HasKey(t => t.Id);
            // builder.Property(t => t.Id).HasColumnName("Id");

            // Other properties go here
        }

    }

    public class GalleryImageMap : IEntityTypeConfiguration<GalleryImage>
    {
        public void Configure(EntityTypeBuilder<GalleryImage> builder)
        {
            builder.ToTable("GalleryImages");

            // In EF core this must be configured on the base type (see below for the error message)
            // Primary Key
            //builder.HasKey(t => t.Id);
            // builder.Property(t => t.Id).HasColumnName("Id");

            // Other properties go here
        }

    }
}


// Error message in case of wrong configuration
/*
 * Initialization method EFCore.Jet.IntegrationTests.Model08.Model08.Initialize threw exception.System.InvalidOperationException: 
 * System.InvalidOperationException: A key cannot be configured on 'GalleryImage' because it is a derived type.The key must be 
 * configured on the root type 'File'. If you did not intend for 'File' to be included in the model, ensure that it is not 
 * included in a DbSet property on your context, referenced in a configuration call to ModelBuilder, or referenced from a 
 * navigation property on a type that is included in the model..
 */

