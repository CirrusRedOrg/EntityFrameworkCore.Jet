using System;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;

namespace EntityFrameworkCore.Jet.Data.JetStoreSchemaDefinition
{
    internal static class JetInformationSchema
    {
        private static readonly Regex _regExParseInformationSchemaCommand;

        static JetInformationSchema()
        {
            _regExParseInformationSchemaCommand = new Regex(
                @"^\s*SELECT\s+\*\s+FROM\s+`?INFORMATION_SCHEMA\.(?<object>\w*)`?(\s*WHERE\s+(?<conditions>.+?))?(\s*ORDER\s+BY\s+(?<orderColumns>.+?))?\s*;?$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        public static bool TryGetDataReaderFromInformationSchemaCommand(JetCommand command, out DbDataReader? dataReader)
        {
            dataReader = GetDbDataReaderFromSimpleStatement(command);
            return dataReader != null;
        }
        
        private static DbDataReader? GetDbDataReaderFromSimpleStatement(JetCommand command)
        {
            // Command text format is
            // SELECT * FROM `INFORMATION_SCHEMA.<what>` [WHERE <condition>] [ORDER BY <order>]
            //    <what> :=
            //          tables
            //          columns
            //          indexes
            //          index_columns
            //          relations
            //          relation_columns
            //          check_constraints

            var jetConnection = (JetConnection) command.Connection;
            var innerCommand = command.InnerCommand;
            var commandText = innerCommand.CommandText;
            var innerConnection = command.InnerCommand.Connection;
            var innerConnectionState = innerConnection.State;

            var match = _regExParseInformationSchemaCommand.Match(commandText);
            if (!match.Success)
            {
                return null;
            }

            var dbObject = match.Groups["object"].Value;
            var conditions = match.Groups["conditions"].Value;
            var orderColumns = match.Groups["orderColumns"].Value;

            Func<JetConnection, DataTable>? schemaMethod = dbObject.ToLower() switch
            {
                "tables" => GetTables,
                "columns" => GetColumns,
                "indexes" => GetIndexes,
                "index_columns" => GetIndexColumns,
                "relations" => GetRelations,
                "relation_columns" => GetRelationColumns,
                "check_constraints" => GetCheckConstraints,
                _ => null
            };

            if (schemaMethod == null)
            {
                return null;
            }

            if (innerConnectionState != ConnectionState.Open)
            {
                innerConnection.Open();
            }

            DataTable dataTable;

            try
            {
                dataTable = schemaMethod(jetConnection);
            }
            finally
            {
                if (innerConnectionState != ConnectionState.Open)
                {
                    innerConnection.Close();
                }
            }

            if (string.IsNullOrWhiteSpace(conditions) &&
                string.IsNullOrWhiteSpace(orderColumns))
            {
                return dataTable.CreateDataReader();
            }

            var selectedRows = dataTable.Select(
                string.IsNullOrWhiteSpace(conditions)
                    ? null
                    : conditions,
                string.IsNullOrWhiteSpace(orderColumns)
                    ? null
                    : orderColumns);

            var selectedDataTable = dataTable.Clone();

            foreach (var row in selectedRows)
                selectedDataTable.ImportRow(row);
            
            return selectedDataTable.CreateDataReader();
        }

        private static DataTable GetTables(JetConnection connection)
        {
            using var schemaProvider = SchemaProvider.CreateInstance(connection.SchemaProviderType, connection);
            return schemaProvider.GetTables();
        }

        private static DataTable GetColumns(JetConnection connection)
        {
            using var schemaProvider = SchemaProvider.CreateInstance(connection.SchemaProviderType, connection);
            return schemaProvider.GetColumns();
        }

        private static DataTable GetIndexes(JetConnection connection)
        {
            using var schemaProvider = SchemaProvider.CreateInstance(connection.SchemaProviderType, connection);
            return schemaProvider.GetIndexes();
        }

        private static DataTable GetIndexColumns(JetConnection connection)
        {
            using var schemaProvider = SchemaProvider.CreateInstance(connection.SchemaProviderType, connection);
            return schemaProvider.GetIndexColumns();
        }

        private static DataTable GetRelations(JetConnection connection)
        {
            using var schemaProvider = SchemaProvider.CreateInstance(connection.SchemaProviderType, connection);
            return schemaProvider.GetRelations();
        }

        private static DataTable GetRelationColumns(JetConnection connection)
        {
            using var schemaProvider = SchemaProvider.CreateInstance(connection.SchemaProviderType, connection);
            return schemaProvider.GetRelationColumns();
        }
 
        private static DataTable GetCheckConstraints(JetConnection connection)
        {
            using var schemaProvider = SchemaProvider.CreateInstance(connection.SchemaProviderType, connection);
            return schemaProvider.GetCheckConstraints();
        }
    }
}