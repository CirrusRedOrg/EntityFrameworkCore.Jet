namespace EFCore.Jet.Integration.Test.MigrationUpDownTest
{
    public class MigrationWithNumericDefaultValue : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "MigrationWithNumericDefaultValue",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    Description = c.String(false, 50),
                    Age = c.Int(nullable: false, defaultValue:50),
                    AgeNotNullable = c.Int(nullable: true, defaultValue: 50)
                })
                .PrimaryKey(t => t.Id);
        }
    }
}