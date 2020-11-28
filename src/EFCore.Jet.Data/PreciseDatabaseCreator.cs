namespace EntityFrameworkCore.Jet.Data
{
    public class PreciseDatabaseCreator
        : JetDatabaseCreator
    {
        public override void CreateDatabase(
            string fileNameOrConnectionString,
            DatabaseVersion version = DatabaseVersion.Newest,
            CollatingOrder collatingOrder = CollatingOrder.General,
            string databasePassword = null)
            => new DaoDatabaseCreator().CreateDatabase(fileNameOrConnectionString, version, collatingOrder, databasePassword);

        public override void CreateDualTable(string fileNameOrConnectionString, string databasePassword = null)
            => new AdoxDatabaseCreator().CreateDualTable(fileNameOrConnectionString, databasePassword);
    }
}