using System;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Diagnostics;

await using var context = new MyContext();
await context.Database.MigrateAsync();

public class MyContext : DbContext
{
    public DbSet<Entity> Entities { get; set; }
    //Microsoft.EntityFrameworkCore.Metadata.Internal.EntityType
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder
            .LogTo(Console.WriteLine)
            .UseJetOleDb("Jet.accdb").ConfigureWarnings(builder => builder.Log(RelationalEventId.PendingModelChangesWarning));
}

[Index(nameof(EntityId), IsUnique = true)]
public class Entity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Key { get; set; }

    [Required]
    [MaxLength(50)]
    public required string EntityId { get; set; }
}