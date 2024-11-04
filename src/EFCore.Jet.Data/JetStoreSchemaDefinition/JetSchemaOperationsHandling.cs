using System.Text.RegularExpressions;

namespace EntityFrameworkCore.Jet.Data.JetStoreSchemaDefinition
{
    public static class JetSchemaOperationsHandling
    {
        private static readonly Regex _renameTableRegex = new(
            $@"^\s*alter\s+table\s+{GetIdentifierPattern("OldTableName")}\s+rename\s+to\s+{GetIdentifierPattern("NewTableName")}\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex _renameTableColumnRegex = new(
            $@"^\s*alter\s+table\s+{GetIdentifierPattern("TableName")}\s+rename\s+column\s+{GetIdentifierPattern("OldColumnName")}\s+to\s+{GetIdentifierPattern("NewColumnName")}\s*$",
            RegexOptions.IgnoreCase);

        public static bool IsDatabaseOperation(string commandText)
            => _renameTableRegex.IsMatch(commandText) || _renameTableColumnRegex.IsMatch(commandText);

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
