using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EntityFramework.Jet.FunctionalTests.Migrations
{
    [DbContext(typeof(Issue305Test.TiffFilesContext))]
    [Migration("20160215092329_Migration02")]
    partial class Migration02
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("ProductVersion", "1.0.0");

            modelBuilder.Entity("ConsoleApplication3.FileInfo", b =>
                {
                    b.ToTable("FileInfo");

                    b.Property<int>("FileInfoId")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("BlindedName");

                    b.Property<bool>("ContainsSynapse");

                    b.Property<string>("Path")
                        .IsRequired();

                    b.Property<int>("Quality");

                    b.HasKey("FileInfoId");
                });
        }
    }
}
