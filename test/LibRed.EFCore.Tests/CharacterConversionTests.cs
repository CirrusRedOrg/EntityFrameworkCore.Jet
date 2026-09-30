using EntityFrameworkCore.LibRed.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LibRed.EFCore.Tests;

public class CharacterConversionTests
{
    [Theory]
    [InlineData(LibRedSqlMode.Compatible)]
    [InlineData(LibRedSqlMode.Extended)]
    public void Character_to_uint_preserves_unsigned_utf16_values(LibRedSqlMode mode)
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-char-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        try
        {
            var options = new DbContextOptionsBuilder<CharacterContext>().UseLibRed(path, mode).Options;
            using var context = new CharacterContext(options);
            context.Database.ExecuteSqlRaw("CREATE TABLE CharRows (Id LONG PRIMARY KEY, Value TEXT(1))");
            char[] values = ['1', '\u7FFF', '\u8000', '\uFFFF'];
            for (int i = 0; i < values.Length; i++)
                context.Database.ExecuteSqlRaw("INSERT INTO CharRows (Id, Value) VALUES ({0}, {1})", i, values[i].ToString());

            Assert.Equal(values.Select(c => (uint)c),
                context.Rows.OrderBy(r => r.Id).Select(r => (uint)r.Value).ToArray());
        }
        finally { File.Delete(path); }
    }

    private sealed class CharacterRow
    {
        public int Id { get; set; }
        public char Value { get; set; }
    }

    private sealed class CharacterContext(DbContextOptions<CharacterContext> options) : DbContext(options)
    {
        public DbSet<CharacterRow> Rows => Set<CharacterRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<CharacterRow>().ToTable("CharRows");
    }
}
