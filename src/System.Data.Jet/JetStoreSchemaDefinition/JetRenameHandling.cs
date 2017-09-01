using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace System.Data.Jet.JetStoreSchemaDefinition
{
    class JetRenameHandling
    {
        private static Regex _renameTableRegex = new Regex(
            $@"^\s*rename\s+table\s+{GetQuotedOrUnquotedNamePattern("tableName")}\s+to\s+{GetQuotedOrUnquotedNamePattern("newTableName")}\s*$",
            RegexOptions.IgnoreCase);

        private static Regex _renameTableColumnRegex = new Regex(
            $@"^\s*rename\s+column\s+{GetQuotedOrUnquotedNamePattern("tableName")}\.{GetQuotedOrUnquotedNamePattern("columnName")}\s+to\s+{GetQuotedOrUnquotedNamePattern("newColumnName")}\s*$",
            RegexOptions.IgnoreCase);

        private static Regex _renameTableIndexRegex = new Regex(
            $@"^\s*rename\s+index\s+{GetQuotedOrUnquotedNamePattern("tableName")}\.{GetQuotedOrUnquotedNamePattern("indexName")}\s+to\s+{GetQuotedOrUnquotedNamePattern("newIndexName")}\s*$",
            RegexOptions.IgnoreCase);


        public static bool TryDatabaseOperation(string connectionString, string commandText)
        {
            Match match;


            match = _renameTableRegex.Match(commandText);
            if (match.Success)
            {
                string tableName = match.Groups["tableName"].Value;
                string newTableName = match.Groups["newTableName"].Value;
                AdoxWrapper.RenameTable(connectionString, RemoveBrackets(tableName), RemoveBrackets(newTableName));
                return true;
            }

            match = _renameTableColumnRegex.Match(commandText);
            if (match.Success)
            {
                string tableName = match.Groups["tableName"].Value;
                string columnName = match.Groups["columnName"].Value;
                string newColumnName = match.Groups["newColumnName"].Value;
                AdoxWrapper.RenameColumn(connectionString, RemoveBrackets(tableName), RemoveBrackets(columnName), RemoveBrackets(newColumnName));
                return true;
            }

            match = _renameTableIndexRegex.Match(commandText);
            if (match.Success)
            {
                string tableName = match.Groups["tableName"].Value;
                string indexName = match.Groups["indexName"].Value;
                string newIndexName = match.Groups["newIndexName"].Value;
                AdoxWrapper.RenameIndex(connectionString, RemoveBrackets(tableName), RemoveBrackets(indexName), RemoveBrackets(newIndexName));
                return true;
            }


            return false;
        }

        private static string RemoveBrackets(string name)
        {
            if (name.StartsWith("[") && name.EndsWith("]"))
                return name.Substring(1, name.Length - 2);
            else
                return name;
        }


        static string GetQuotedOrUnquotedNamePattern(string key)
        {
            return $@"((?<{key}>\S*)|\[(?<{key}>.*)\])";
        }
    }
}
