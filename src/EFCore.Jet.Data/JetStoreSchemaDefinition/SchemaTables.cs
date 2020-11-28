namespace System.Data.Jet.JetStoreSchemaDefinition
{
    internal static class SchemaTables
    {
        public static DataTable GetTablesDataTable()
        {
            var dataTable = new DataTable("INFORMATION_SCHEMA.TABLES");
            
            dataTable.Columns.AddRange(
                new[]
                {
                    new DataColumn("TABLE_NAME", typeof(string)),
                    new DataColumn("TABLE_TYPE", typeof(string)),
                    new DataColumn("VALIDATION_RULE", typeof(string)),
                    new DataColumn("VALIDATION_TEXT", typeof(string)),
                });
            
            return dataTable;
        }

        public static DataTable GetColumnsDataTable()
        {
            var dataTable = new DataTable("INFORMATION_SCHEMA.COLUMNS");

            dataTable.Columns.AddRange(
                new[]
                {
                    new DataColumn("TABLE_NAME", typeof(string)),
                    new DataColumn("COLUMN_NAME", typeof(string)),
                    new DataColumn("ORDINAL_POSITION", typeof(int)),
                    new DataColumn("DATA_TYPE", typeof(string)),
                    new DataColumn("IS_NULLABLE", typeof(bool)),
                    new DataColumn("CHARACTER_MAXIMUM_LENGTH", typeof(int)),
                    new DataColumn("NUMERIC_PRECISION", typeof(int)),
                    new DataColumn("NUMERIC_SCALE", typeof(int)),
                    new DataColumn("COLUMN_DEFAULT", typeof(string)),
                    new DataColumn("VALIDATION_RULE", typeof(string)),
                    new DataColumn("VALIDATION_TEXT", typeof(string)),
                    new DataColumn("IDENTITY_SEED", typeof(int)),
                    new DataColumn("IDENTITY_INCREMENT", typeof(int)),
                    // TODO: Add ALLOW_ZERO_LENGTH_STRING
                });
            
            return dataTable;
        }

        public static DataTable GetIndexesDataTable()
        {
            var dataTable = new DataTable("INFORMATION_SCHEMA.INDEXES");

            dataTable.Columns.AddRange(
                new[]
                {
                    new DataColumn("TABLE_NAME", typeof(string)),
                    new DataColumn("INDEX_NAME", typeof(string)),
                    new DataColumn("INDEX_TYPE", typeof(string)),
                    new DataColumn("IS_NULLABLE", typeof(bool)),
                    new DataColumn("IGNORES_NULLS", typeof(bool)),
                });
            
            return dataTable;
        }

        public static DataTable GetIndexColumnsDataTable()
        {
            var dataTable = new DataTable("INFORMATION_SCHEMA.INDEX_COLUMNS");

            dataTable.Columns.AddRange(
                new[]
                {
                    new DataColumn("TABLE_NAME", typeof(string)),
                    new DataColumn("INDEX_NAME", typeof(string)),
                    new DataColumn("ORDINAL_POSITION", typeof(int)),
                    new DataColumn("COLUMN_NAME", typeof(string)),
                    new DataColumn("IS_DESCENDING", typeof(bool)),
                });
            
            return dataTable;
        }

        public static DataTable GetRelationsDataTable()
        {
            var dataTable = new DataTable("INFORMATION_SCHEMA.RELATIONS");

            dataTable.Columns.AddRange(
                new[]
                {
                    new DataColumn("RELATION_NAME", typeof(string)),
                    new DataColumn("REFERENCING_TABLE_NAME", typeof(string)),
                    new DataColumn("PRINCIPAL_TABLE_NAME", typeof(string)),
                    new DataColumn("RELATION_TYPE", typeof(string)),
                    new DataColumn("ON_DELETE", typeof(string)),
                    new DataColumn("ON_UPDATE", typeof(string)),
                    new DataColumn("IS_ENFORCED", typeof(bool)),
                    new DataColumn("IS_INHERITED", typeof(bool)),
                });
            
            return dataTable;
        }
        
        public static DataTable GetRelationColumnsDataTable()
        {
            var dataTable = new DataTable("INFORMATION_SCHEMA.RELATION_COLUMNS");

            dataTable.Columns.AddRange(
                new[]
                {
                    new DataColumn("RELATION_NAME", typeof(string)),
                    new DataColumn("REFERENCING_COLUMN_NAME", typeof(string)),
                    new DataColumn("PRINCIPAL_COLUMN_NAME", typeof(string)),
                });
            
            return dataTable;
        }
        
        public static DataTable GetCheckConstraintsDataTable()
        {
            var dataTable = new DataTable("INFORMATION_SCHEMA.CHECK_CONSTRAINTS");

            dataTable.Columns.AddRange(
                new[]
                {
                    new DataColumn("TABLE_NAME", typeof(string)),
                    new DataColumn("CONSTRAINT_NAME", typeof(string)),
                    new DataColumn("CHECK_CLAUSE", typeof(string)),
                });
            
            return dataTable;
        }
    }
}