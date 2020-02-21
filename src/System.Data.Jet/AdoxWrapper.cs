using System;

namespace System.Data.Jet
{
    public static class AdoxWrapper
    {

        public static void RenameTable(string connectionString, string tableName, string newTableName)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(newTableName)) throw new ArgumentNullException(nameof(newTableName));


            ADOX.Catalog catalog = GetCatalogInstanceAndOpen("Cannot rename column", connectionString);

            try
            {
                catalog.Tables[tableName].Name = newTableName;
            }
            catch (Exception e)
            {
                throw new Exception("Cannot rename table", e);
            }
            finally
            {
                ((ADODB.Connection)catalog.ActiveConnection).Close();
            }
        }


        public static void RenameColumn(string connectionString, string tableName, string columnName, string newColumnName)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(columnName)) throw new ArgumentNullException(nameof(columnName));
            if (string.IsNullOrWhiteSpace(newColumnName)) throw new ArgumentNullException(nameof(newColumnName));


            ADOX.Catalog catalog = GetCatalogInstanceAndOpen("Cannot rename column", connectionString);

            try
            {
                catalog.Tables[tableName].Columns[columnName].Name = newColumnName;
            }
            catch (Exception e)
            {
                throw new Exception("Cannot rename column", e);
            }
            finally
            {
                ((ADODB.Connection)catalog.ActiveConnection).Close();
            }
        }


        public static void RenameIndex(string connectionString, string tableName, string indexName, string newIndexName)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentNullException(nameof(indexName));
            if (string.IsNullOrWhiteSpace(newIndexName)) throw new ArgumentNullException(nameof(newIndexName));


            ADOX.Catalog catalog = GetCatalogInstanceAndOpen("Cannot rename index", connectionString);

            ADOX.Table table;
            ADOX.Index index;

            try
            {
                table = catalog.Tables[tableName];
            }
            catch (Exception e)
            {
                ((ADODB.Connection)catalog.ActiveConnection).Close();
                throw new Exception("Cannot rename index. Cannot retrieve the table '" + tableName + "'", e);
            }


            try
            {
                index = table.Indexes[indexName];
            }
            catch (Exception e)
            {
                ((ADODB.Connection)catalog.ActiveConnection).Close();
                throw new Exception("Cannot rename index. Cannot retrieve the old index '" + indexName + "'", e);
            }



            try
            {
                index.Name = newIndexName;
            }
            catch (Exception e)
            {
                throw new Exception("Cannot rename index.", e);
            }
            finally
            {
                ((ADODB.Connection)catalog.ActiveConnection).Close();
            }


        }



        public static void CreateEmptyDatabase(string connectionString)
        {
            ADOX.Catalog catalog = GetCatalogInstance("Cannot create database");

            try
            {
                catalog.Create(connectionString);
            }
            catch (Exception e)
            {
                throw new Exception("Cannot create database using the specified connection string.", e);
            }


            try
            {
                using (var connection = (new JetConnection(connectionString)))
                {
                    connection.Open();
                    string sql = @"
CREATE TABLE [MSysAccessStorage] (
    [DateCreate] DATETIME NULL,
    [DateUpdate] DATETIME NULL,
    [Id] INT NOT NULL IDENTITY,
    [Lv] IMAGE,
    [Name] VARCHAR(128) NULL,
    [ParentId] INT NULL,
    [Type] INT NULL,
    CONSTRAINT [Id] PRIMARY KEY ([Id])
);
CREATE UNIQUE INDEX [ParentIdId] ON [MSysAccessStorage] ([ParentId], [Id]);
CREATE UNIQUE INDEX [ParentIdName] ON [MSysAccessStorage] ([ParentId], [Name]);";

                    connection.CreateCommand(sql).ExecuteNonQuery();
                }
            }
            catch (Exception e)
            {
                throw new Exception("Cannot create database using the specified connection string.", e);
            }

            try
            {
                var x = ((ADODB.Connection)catalog.ActiveConnection);
                x.Close();
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Cannot close active connection after create statement.\r\nThe exception is: {0}", e.Message);
            }

        }




        private static ADOX.Catalog GetCatalogInstance(string errorPrefix)
        {
            return new ADOX.Catalog();
        }

        private static ADOX.Catalog GetCatalogInstanceAndOpen(string errorPrefix, string connectionString)
        {
            ADOX.Catalog catalog = GetCatalogInstance(errorPrefix);

            try
            {
                ADODB.Connection cnn = new ADODB.Connection();
                cnn.Open(ConnectionString: connectionString);
                catalog.ActiveConnection = cnn;
            }
            catch (Exception e)
            {
                throw new Exception(errorPrefix + ". Cannot open database using the specified connection string.", e);
            }
            return catalog;
        }


    }
}