using System.Data.Common;

namespace System.Data.Jet
{
    public static class AdoxWrapper
    {
        public static void RenameTable(string connectionString, string tableName, string newTableName)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentNullException(nameof(connectionString));
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(newTableName))
                throw new ArgumentNullException(nameof(newTableName));

            using var catalog = GetCatalogInstanceAndOpen("Cannot rename column", connectionString);

            try
            {
                using var tables = catalog.Tables;
                using var table = tables[tableName];
                table.Name = newTableName;
            }
            catch (Exception e)
            {
                throw new Exception("Cannot rename table", e);
            }
            finally
            {
                catalog.ActiveConnection.Close();
            }
        }

        public static void RenameColumn(string connectionString, string tableName, string columnName, string newColumnName)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentNullException(nameof(connectionString));
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(columnName))
                throw new ArgumentNullException(nameof(columnName));
            if (string.IsNullOrWhiteSpace(newColumnName))
                throw new ArgumentNullException(nameof(newColumnName));

            using var catalog = GetCatalogInstanceAndOpen("Cannot rename column", connectionString);

            try
            {
                using var tables = catalog.Tables;
                using var columns = tables[tableName].Columns;
                using var column = columns[columnName];
                column.Name = newColumnName;
            }
            catch (Exception e)
            {
                throw new Exception("Cannot rename column", e);
            }
            finally
            {
                catalog.ActiveConnection.Close();
            }
        }
        
        public static string CreateEmptyDatabase(string fileName, DbProviderFactory dataAccessProviderFactory)
        {
            string connectionString = null;
            using var catalog = GetCatalogInstance();
            
            try
            {
                connectionString = JetConnection.GetConnectionString(fileName, DataAccessType.OleDb);
                
                catalog.Create(connectionString)
                    .Dispose(); // Dispose the returned Connection object, because we don't use it here.
            }
            catch (Exception e)
            {
                throw new Exception("Cannot create database using the specified connection string: " + connectionString, e);
            }

            try
            {
                connectionString = JetConnection.GetConnectionString(fileName, dataAccessProviderFactory);

                using var connection = (JetConnection)JetFactory.Instance.CreateConnection();
                connection.ConnectionString = connectionString;
                connection.DataAccessProviderFactory = dataAccessProviderFactory;
                connection.Open();
                
                string sql = @"
CREATE TABLE [MSysAccessStorage] (
    [DateCreate] DATETIME NULL,
    [DateUpdate] DATETIME NULL,
    [Id] COUNTER NOT NULL,
    [Lv] IMAGE,
    [Name] VARCHAR(128) NULL,
    [ParentId] INT NULL,
    [Type] INT NULL,
    CONSTRAINT [Id] PRIMARY KEY ([Id])
);
CREATE UNIQUE INDEX [ParentIdId] ON [MSysAccessStorage] ([ParentId], [Id]);
CREATE UNIQUE INDEX [ParentIdName] ON [MSysAccessStorage] ([ParentId], [Name]);";

                using var command = connection.CreateCommand(sql);
                command.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                throw new Exception("Cannot create database using the specified connection string.", e);
            }

            try
            {
                using var connection = catalog.ActiveConnection;
                connection.Close();
            }
            catch (Exception e)
            {
                Diagnostics.Debug.WriteLine("Cannot close active connection after create statement.\r\nThe exception is: {0}", e.Message);
            }

            return connectionString;
        }

        private static dynamic GetCatalogInstance()
            => new ComObject("ADOX.Catalog");

        private static dynamic GetCatalogInstanceAndOpen(string errorPrefix, string connectionString)
        {
            var catalog = GetCatalogInstance();

            try
            {
                using dynamic cnn = new ComObject("ADODB.Connection");
                cnn.Open(connectionString);
                catalog.ActiveConnection = cnn;
            }
            catch (Exception e)
            {
                catalog.Dispose();
                throw new Exception(errorPrefix + ". Cannot open database using the specified connection string.", e);
            }

            return catalog;
        }
    }
}