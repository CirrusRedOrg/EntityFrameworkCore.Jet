using System;
using System.Data;

namespace EntityFrameworkCore.Jet.Data
{
    public enum SchemaProviderType
    {
        Precise,
        Dao,
        Adox,
    }

    public abstract class SchemaProvider : ISchemaProvider
    {
        public static SchemaProvider CreateInstance(SchemaProviderType type, JetConnection connection)
            => type switch
            {
                SchemaProviderType.Precise => new PreciseSchema(connection),
                SchemaProviderType.Dao => new PreciseSchema(connection),
                SchemaProviderType.Adox => new PreciseSchema(connection),
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };

        public abstract void Dispose();
        public abstract void EnsureDualTable();
        
        public abstract DataTable GetTables();
        public abstract DataTable GetColumns();
        public abstract DataTable GetIndexes();
        public abstract DataTable GetIndexColumns();
        public abstract DataTable GetRelations();
        public abstract DataTable GetRelationColumns();
        public abstract DataTable GetCheckConstraints();

        internal static bool IsSystemTableByName(string tableName)
            => tableName.StartsWith("msys", StringComparison.OrdinalIgnoreCase);

        internal static bool IsInternalTableByName(string tableName)
            => tableName.StartsWith("#", StringComparison.OrdinalIgnoreCase);
    }
}