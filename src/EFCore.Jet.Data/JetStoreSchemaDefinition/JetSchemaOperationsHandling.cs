using System.Text.RegularExpressions;

namespace EntityFrameworkCore.Jet.Data.JetStoreSchemaDefinition
{
    internal static class JetSchemaOperationsHandling
    {
        private static readonly Regex _renameTableRegex = new Regex(
            $@"^\s*rename\s+table\s+{GetIdentifierPattern("OldTableName")}\s+to\s+{GetIdentifierPattern("NewTableName")}\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex _renameTableColumnRegex = new Regex(
            $@"^\s*rename\s+column\s+{GetIdentifierPattern("TableName")}\s*\.\s*{GetIdentifierPattern("OldColumnName")}\s+to\s+{GetIdentifierPattern("NewColumnName")}\s*$",
            RegexOptions.IgnoreCase);

        public static bool TryDatabaseOperation(JetConnection connection, string commandText)
        {
            var match = _renameTableRegex.Match(commandText);
            if (match.Success)
            {
                var oldTableName = match.Groups["OldTableName"].Value;
                var newTableName = match.Groups["NewTableName"].Value;
                
                RenameTable(connection, oldTableName, newTableName);

                return true;
            }

            match = _renameTableColumnRegex.Match(commandText);
            if (match.Success)
            {
                var tableName = match.Groups["TableName"].Value;
                var oldColumnName = match.Groups["OldColumnName"].Value;
                var newColumnName = match.Groups["NewColumnName"].Value;
                
                RenameColumn(connection, tableName, oldColumnName, newColumnName);
                
                return true;
            }

            return false;
        }

        private static string GetIdentifierPattern(string key)
            => $@"(?:`(?<{key}>.*?)`|\[(?<{key}>.*?)\]|(?<{key}>\S*))";
        
        private static void RenameTable(JetConnection connection, string oldTableName, string newTableName)
        {
            using var schemaProvider = SchemaProvider.CreateInstance(connection.SchemaProviderType, connection, false);
            schemaProvider.RenameTable(oldTableName, newTableName);
        }

        private static void RenameColumn(JetConnection connection, string tableName, string oldColumnName, string newColumnName)
        {
            using var schemaProvider = SchemaProvider.CreateInstance(connection.SchemaProviderType, connection, false);
            schemaProvider.RenameColumn(tableName, oldColumnName, newColumnName);
        }
    }
}
