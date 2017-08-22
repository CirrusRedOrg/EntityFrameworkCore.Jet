namespace EFCore.Jet.Integration.Test.MigrationUpDownTest
{
    public class MigrationWithNumericDefaultValueSql : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "MigrationWithNumericDftValueSql",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    Description = c.String(false, 50),
                    Age = c.Int(nullable: false, defaultValueSql: "50"),
                    AgeNotNullable = c.Int(nullable: true, defaultValueSql: "50")
                })
                .PrimaryKey(t => t.Id);
        }
    }
}