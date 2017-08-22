namespace EFCore.Jet.Integration.Test.MigrationUpDownTest
{
    public class MigrationWithDefaultValue : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "MigrationWithDefaultValue",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    Description = c.String(false, 50, null, null, "DftValue")
                })
                .PrimaryKey(t => t.Id);
        }
    }
}