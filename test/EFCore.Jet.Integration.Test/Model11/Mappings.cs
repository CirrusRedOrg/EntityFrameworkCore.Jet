using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFCore.Jet.Integration.Test.Model11
{
    public class VersionMap : IEntityTypeConfiguration<Version>
    {

        public void Configure(EntityTypeBuilder<Version> builder)
        {
            // Relationships
            builder.HasMany(t => t.Models)
                .WithMany(t => t.Versions);

            builder.HasMany(t => t.DataReleaseLevels)
                .WithMany(t => t.Versions);
        }
    }
}
