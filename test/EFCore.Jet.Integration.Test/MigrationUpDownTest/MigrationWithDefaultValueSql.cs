namespace EFCore.Jet.Integration.Test.MigrationUpDownTest
{
    public class MigrationWithDefaultValueSql : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "MigrationWithDefaultValueSql",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    Description = c.String(true, 50, null, null, null, "DftValue"),
                    DescriptionNotNullable = c.String(false, 50, null, null, null, "DftValue")
                })
                .PrimaryKey(t => t.Id);
        }
    }
}