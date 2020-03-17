namespace EFCore.Jet.Integration.Test.MigrationUpDownTest
{
    public class SimpleMigration : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "SimpleMigrationTest",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    Description = c.String(false, 50)
                })
                .PrimaryKey(t => t.Id);
        }
    }
}