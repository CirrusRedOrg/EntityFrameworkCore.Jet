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

    public abstract class SchemaProvider : ISchemaProvider, ISchemaOperationsProvider
    {
        public static SchemaProvider CreateInstance(SchemaProviderType type, JetConnection connection, bool readOnly = true)
            => type switch
            {
                SchemaProviderType.Precise => new PreciseSchema(connection, readOnly),
                SchemaProviderType.Dao => new PreciseSchema(connection, readOnly),
                SchemaProviderType.Adox => new PreciseSchema(connection, readOnly),
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };

        public abstract void Dispose();
        
        public abstract DataTable GetTables();
        public abstract DataTable GetColumns();
        public abstract DataTable GetIndexes();
        public abstract DataTable GetIndexColumns();
        public abstract DataTable GetRelations();
        public abstract DataTable GetRelationColumns();
        public abstract DataTable GetCheckConstraints();

        public abstract void EnsureDualTable();
        public abstract void RenameTable(string oldTableName, string newTableName);
        public abstract void RenameColumn(string tableName, string oldColumnName, string newColumnName);

        internal static bool IsSystemTableByName(string tableName)
            => tableName.StartsWith("msys", StringComparison.OrdinalIgnoreCase);

        internal static bool IsInternalTableByName(string tableName)
            => tableName.StartsWith("#", StringComparison.OrdinalIgnoreCase);
    }
}