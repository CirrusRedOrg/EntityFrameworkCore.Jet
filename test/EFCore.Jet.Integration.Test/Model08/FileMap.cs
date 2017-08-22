using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFCore.Jet.Integration.Test.Model08
{
    public class FileMap : IEntityTypeConfiguration<File>
    {
        public FileMap()
        {
        }

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
}