using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFCore.Jet.Integration.Test.Model69_PrivateBackingField
{

    public class Info
    {
        public int Id { get; set; }
        [MaxLength(50)]
        public string Description { get; set; }

        public sbyte SByte
        {
            get
            {
                return (sbyte) SByteBackingField;
            }
            set
            {
                SByteBackingField = value;
            }
        }

        private int SByteBackingField { get; set; }


        public class InfoMap : IEntityTypeConfiguration<Info>
        {

            public void Configure(EntityTypeBuilder<Info> builder)
            {
                builder.ToTable("Infoes69");
                builder.Property(_ => _.SByteBackingField).HasColumnName("SByte");
                builder.Ignore(_ => _.SByte);

            }
        }

    }
}
