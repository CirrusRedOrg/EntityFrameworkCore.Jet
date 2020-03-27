using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model66_StackOverflow_TooManyColumns
{
    public class Context : DbContext
    {
        public Context()
        { }


        public Context(DbContextOptions options) : base (options)
        { }

        public DbSet<MyFlashCard> MyFlashCards { get; set; }
        public DbSet<MyFlashCardPic> MyFlashCardPics { get; set; }
        public DbSet<FasleManJdl> FasleManJdls { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MyFlashCard>()
                .HasMany(e => e.MyFlashCardPics)
                .WithOne(e => e.MyFlashCard)
                .HasForeignKey(e => e.MyFlashCardId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

}
