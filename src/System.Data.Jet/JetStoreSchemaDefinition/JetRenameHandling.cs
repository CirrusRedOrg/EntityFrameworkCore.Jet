using System.Text.RegularExpressions;

namespace System.Data.Jet.JetStoreSchemaDefinition
{
    internal class JetRenameHandling
    {
        private static readonly Regex _renameTableRegex = new Regex(
            $@"^\s*rename\s+table\s+{GetIdentifierPattern("OldTableName")}\s+to\s+{GetIdentifierPattern("NewTableName")}\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex _renameTableColumnRegex = new Regex(
            $@"^\s*rename\s+column\s+{GetIdentifierPattern("TableName")}\s*\.\s*{GetIdentifierPattern("OldColumnName")}\s+to\s+{GetIdentifierPattern("NewColumnName")}\s*$",
            RegexOptions.IgnoreCase);

        public static bool TryDatabaseOperation(string connectionString, string commandText)
        {
            var match = _renameTableRegex.Match(commandText);
            if (match.Success)
            {
                var oldTableName = match.Groups["OldTableName"].Value;
                var newTableName = match.Groups["NewTableName"].Value;
                
                // TODO: Only use ADOX in an OLE DB context. Use DAO in an ODBC context.
                AdoxWrapper.RenameTable(connectionString, oldTableName, newTableName);
                
                return true;
            }

            match = _renameTableColumnRegex.Match(commandText);
            if (match.Success)
            {
                var tableName = match.Groups["TableName"].Value;
                var oldColumnName = match.Groups["OldColumnName"].Value;
                var newColumnName = match.Groups["NewColumnName"].Value;
                
                // TODO: Only use ADOX in an OLE DB context. Use DAO in an ODBC context.
                AdoxWrapper.RenameColumn(connectionString, tableName, oldColumnName, newColumnName);
                
                return true;
            }

            return false;
        }

        private static string GetIdentifierPattern(string key)
            => $@"(?:`(?<{key}>.*?)`|\[(?<{key}>.*?)\]|(?<{key}>\S*))";
    }
}
