namespace System.Data.Jet
{
    public abstract class JetDatabaseCreator : IJetDatabaseCreator
    {
        public static JetDatabaseCreator CreateInstance(SchemaProviderType schemaProviderType)
            => schemaProviderType switch
            {
                SchemaProviderType.Precise => new PreciseDatabaseCreator(),
                SchemaProviderType.Dao => new DaoDatabaseCreator(),
                SchemaProviderType.Adox => new AdoxDatabaseCreator(),
                _ => throw new ArgumentOutOfRangeException(nameof(schemaProviderType))
            };

        public abstract void CreateDatabase(string fileNameOrConnectionString, DatabaseVersion version = DatabaseVersion.Newest, CollatingOrder collatingOrder = CollatingOrder.General, string databasePassword = null);
        public abstract void CreateDualTable(string fileNameOrConnectionString, string databasePassword = null);
    }
}