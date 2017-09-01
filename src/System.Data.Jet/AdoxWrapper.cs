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


            dynamic catalog = GetCatalogInstanceAndOpen("Cannot rename column", connectionString);

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
                catalog.ActiveConnection.Close();
            }
        }


        public static void RenameColumn(string connectionString, string tableName, string columnName, string newColumnName)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(columnName)) throw new ArgumentNullException(nameof(columnName));
            if (string.IsNullOrWhiteSpace(newColumnName)) throw new ArgumentNullException(nameof(newColumnName));


            dynamic catalog = GetCatalogInstanceAndOpen("Cannot rename column", connectionString);

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
                catalog.ActiveConnection.Close();
            }
        }


        public static void RenameIndex(string connectionString, string tableName, string indexName, string newIndexName)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentNullException(nameof(indexName));
            if (string.IsNullOrWhiteSpace(newIndexName)) throw new ArgumentNullException(nameof(newIndexName));


            dynamic catalog = GetCatalogInstanceAndOpen("Cannot rename index", connectionString);

            try
            {
                catalog.Tables[tableName].Indexes[indexName].Name = newIndexName;
            }
            catch (Exception e)
            {
                throw new Exception("Cannot rename index", e);
            }
            finally
            {
                catalog.ActiveConnection.Close();
            }
        }



        public static void CreateEmptyDatabase(string connectionString)
        {
            dynamic catalog = GetCatalogInstance("Cannot create database");

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
                catalog.ActiveConnection.Close();
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Cannot close active connection after create statement.\r\nThe exception is: {0}", e.Message);
            }

        }




        private static dynamic GetCatalogInstance(string errorPrefix)
        {
            Type adoxCatalogType;
            dynamic catalog;

            try
            {
                adoxCatalogType = Type.GetTypeFromProgID("ADOX.Catalog", true);
            }
            catch (Exception e)
            {
                throw new Exception(errorPrefix + ". Cannot retrieve ADOX.Catalog type. Check ADOX installation.", e);
            }

            try
            {
                catalog = System.Activator.CreateInstance(adoxCatalogType);
            }
            catch (Exception e)
            {
                throw new Exception(errorPrefix + ". Cannot create an instance of ADOX.Catalog type.", e);
            }
            return catalog;
        }

        private static dynamic GetCatalogInstanceAndOpen(string errorPrefix, string connectionString)
        {
            dynamic catalog = GetCatalogInstance(errorPrefix);

            try
            {
                catalog.ActiveConnection = connectionString;
            }
            catch (Exception e)
            {
                throw new Exception(errorPrefix + ". Cannot open database using the specified connection string.", e);
            }
            return catalog;
        }


    }
}