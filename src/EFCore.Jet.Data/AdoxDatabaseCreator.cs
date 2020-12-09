using System;
using EntityFrameworkCore.Jet.Data.JetStoreSchemaDefinition;

namespace EntityFrameworkCore.Jet.Data
{
    public class AdoxDatabaseCreator
        : JetDatabaseCreator
    {
        public override void CreateDatabase(
            string fileNameOrConnectionString,
            DatabaseVersion version = DatabaseVersion.NewestSupported,
            CollatingOrder collatingOrder = CollatingOrder.General,
            string databasePassword = null)
        {
            if (databasePassword != null &&
                databasePassword.Length > 20)
            {
                throw new ArgumentOutOfRangeException(nameof(databasePassword));
            }
            
            var filePath = JetStoreDatabaseHandling.ExpandFileName(JetStoreDatabaseHandling.ExtractFileNameFromConnectionString(fileNameOrConnectionString));
            
            if (version == DatabaseVersion.NewestSupported &&
                string.Equals(System.IO.Path.GetExtension(filePath), ".mdb"))
            {
                version = DatabaseVersion.Version40;
            }

            try
            {
                using dynamic catalog = new ComObject("ADOX.Catalog");
                
                // ADOX is an ADO eXtension and ADO is build on top of OLE DB.
                var connectionString = GetConnectionString(filePath, version, collatingOrder, databasePassword);
                using var connection = catalog.Create(connectionString);
            }
            catch (Exception e)
            {
                throw new Exception($"Cannot create database \"{filePath}\" using DAO.", e);
            }
        }

        public override void CreateDualTable(
            string fileNameOrConnectionString,
            string databasePassword = null)
        {
            if (databasePassword != null &&
                databasePassword.Length > 20)
            {
                throw new ArgumentOutOfRangeException(nameof(databasePassword));
            }
            
            using dynamic connection = new ComObject("ADODB.Connection");
            
            // ADOX is an ADO eXtension and ADO is build on top of OLE DB.
            var filePath = JetStoreDatabaseHandling.ExpandFileName(JetStoreDatabaseHandling.ExtractFileNameFromConnectionString(fileNameOrConnectionString));
            var connectionString = GetConnectionString(filePath, default, default, databasePassword);

            connection.ConnectionString = connectionString;
            connection.Open();
            
            try
            {
                connection.Execute("CREATE TABLE `#Dual` (`ID` INTEGER NOT NULL CONSTRAINT `PrimaryKey` PRIMARY KEY)", out int _, (int)CommandTypeEnum.adCmdText | (int)ExecuteOptionEnum.adExecuteNoRecords);
                connection.Execute("INSERT INTO `#Dual` (`ID`) VALUES (1)", out int _, (int)CommandTypeEnum.adCmdText | (int)ExecuteOptionEnum.adExecuteNoRecords);
                connection.Execute("ALTER TABLE `#Dual` ADD CONSTRAINT `SingleRecord` CHECK ((SELECT COUNT(*) FROM [#Dual]) = 1)", out int _, (int)CommandTypeEnum.adCmdText | (int)ExecuteOptionEnum.adExecuteNoRecords);
                
                using dynamic catalog = new ComObject("ADOX.Catalog");
                catalog.ActiveConnection = connection;
                
                using var tables = catalog.Tables;
                using var table = tables["#Dual"];
                using var properties = table.Properties;
                using var property = properties["Jet OLEDB:Table Hidden In Access"];
                property.Value = true;
            }
            catch (Exception e)
            {
                throw new Exception($"Cannot create dual table \"#Dual\" using ADOX.", e);
            }
        }
        
        private static string GetConnectionString(string filePath, DatabaseVersion version, CollatingOrder collatingOrder, string databasePassword)
        {
            var connectionString = JetConnection.GetConnectionString(filePath, DataAccessProviderType.OleDb);

            var databaseType = version switch
            {
                DatabaseVersion.Version10 => 1,
                DatabaseVersion.Version11 => 2,
                DatabaseVersion.Version20 => 3,
                DatabaseVersion.Version30 => 5,
                DatabaseVersion.Version40 => 5,
                DatabaseVersion.Version120 => 6,
                _ => 0
            };

            if (collatingOrder > 0)
            {
                //connectionString += $";Locale Identifier={collatingOrder}";
            }

            if (databaseType > 0)
            {
                connectionString += $";Jet OLEDB:Engine Type={databaseType}";
            }

            if (!string.IsNullOrEmpty(databasePassword))
            {
                connectionString += $";Jet OLEDB:Database Password={databasePassword}";
            }

            return connectionString;
        }
        
        [Flags]
        protected enum CommandTypeEnum
        {
            adCmdUnspecified = -1,
            adCmdText = 0x00000001,
            adCmdTable = 0x00000002,
            adCmdStoredProc = 0x00000004,
            adCmdUnknown = 0x00000008,
            adCmdFile = 0x00000100,
            adCmdTableDirect = 0x00000200,
        }
        
        [Flags]
        protected enum ExecuteOptionEnum
        {
            adOptionUnspecified = -1,
            adAsyncExecute = 0x00000010,
            adAsyncFetch = 0x00000020,
            adAsyncFetchNonBlocking = 0x00000040,
            adExecuteNoRecords = 0x00000080,
            adExecuteStream = 0x00000400,
            //adExecuteRecord = ???,
        }
    }
}