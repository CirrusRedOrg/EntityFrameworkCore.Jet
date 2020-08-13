using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;

namespace System.Data.Jet
{
    public static class DbConnectionStringBuilderExtensions
    {
        public static string GetProvider(this DbConnectionStringBuilder builder, DataAccessProviderType? providerType = null)
        {
            if (providerType != null && providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                return builder.TryGetValue("provider", out var value)
                    ? (string)value
                    : null;
            }

            if (providerType != null && providerType == DataAccessProviderType.Odbc ||
                IsOdbc(builder))
            {
                return builder.TryGetValue("driver", out var value)
                    ? (string)value
                    : null;
            }

            return builder.TryGetValue("provider", out var oleDbValue)
                ? (string)oleDbValue
                : builder.TryGetValue("driver", out var odbcValue)
                    ? (string)odbcValue
                    : null;
        }

        public static void SetProvider(this DbConnectionStringBuilder builder, string value, DataAccessProviderType? providerType = null)
        {
            if (providerType != null && providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                builder["provider"] = value;
            }
            else if (providerType != null && providerType == DataAccessProviderType.Odbc ||
                     IsOdbc(builder))
            {
                builder["driver"] = Regex.Replace(value.Trim(), @"^(?<!\{)(.*)(?!\})$", @"{$1}", RegexOptions.IgnoreCase);
            }
            else
                throw new InvalidOperationException("This extension method only supports OdbcConnectionStringBuilder and OleDbConnectionStringBuilder.");
        }
        
        public static string GetDataSource(this DbConnectionStringBuilder builder, DataAccessProviderType? providerType = null)
        {
            if (providerType != null && providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                return builder.TryGetValue("data source", out var value)
                    ? (string)value
                    : null;
            }

            if (providerType != null && providerType == DataAccessProviderType.Odbc ||
                IsOdbc(builder))
            {
                return builder.TryGetValue("dbq", out var value)
                    ? (string)value
                    : null;
            }

            return builder.TryGetValue("data source", out var oleDbValue)
                ? (string)oleDbValue
                : builder.TryGetValue("dbq", out var odbcValue)
                    ? (string)odbcValue
                    : null;
        }

        public static void SetDataSource(this DbConnectionStringBuilder builder, string value, DataAccessProviderType? providerType = null)
        {
            if (providerType != null && providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                builder["data source"] = value;
            }
            else if (providerType != null && providerType == DataAccessProviderType.Odbc ||
                     IsOdbc(builder))
            {
                builder["dbq"] = value;
            }
            else
                throw new InvalidOperationException("This extension method only supports OdbcConnectionStringBuilder and OleDbConnectionStringBuilder.");
        }
        
        public static string GetUserId(this DbConnectionStringBuilder builder, DataAccessProviderType? providerType = null)
        {
            if (IsOleDb(builder))
            {
                return builder.TryGetValue("user id", out var value)
                    ? (string)value
                    : null;
            }

            if (providerType != null && providerType == DataAccessProviderType.Odbc ||
                IsOdbc(builder))
            {
                return builder.TryGetValue("uid", out var value)
                    ? (string)value
                    : null;
            }

            return builder.TryGetValue("user id", out var oleDbValue)
                ? (string)oleDbValue
                : builder.TryGetValue("uid", out var odbcValue)
                    ? (string)odbcValue
                    : null;
        }

        public static void SetUserId(this DbConnectionStringBuilder builder, string value, DataAccessProviderType? providerType = null)
        {
            if (providerType != null && providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                builder["user id"] = value;
            }
            else if (providerType != null && providerType == DataAccessProviderType.Odbc ||
                     IsOdbc(builder))
            {
                builder["uid"] = value;
            }
            else
                throw new InvalidOperationException("This extension method only supports OdbcConnectionStringBuilder and OleDbConnectionStringBuilder.");
        }
        
        public static string GetPassword(this DbConnectionStringBuilder builder, DataAccessProviderType? providerType = null)
        {
            if (providerType != null && providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                return builder.TryGetValue("password", out var value)
                    ? (string)value
                    : null;
            }

            if (providerType != null && providerType == DataAccessProviderType.Odbc ||
                IsOdbc(builder))
            {
                return builder.TryGetValue("pwd", out var value)
                    ? (string)value
                    : null;
            }

            return builder.TryGetValue("password", out var oleDbValue)
                ? (string)oleDbValue
                : builder.TryGetValue("pwd", out var odbcValue)
                    ? (string)odbcValue
                    : null;
        }

        public static void SetPassword(this DbConnectionStringBuilder builder, string value, DataAccessProviderType? providerType = null)
        {
            if (providerType != null && providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                builder["password"] = value;
            }
            else if (providerType != null && providerType == DataAccessProviderType.Odbc ||
                     IsOdbc(builder))
            {
                builder["pwd"] = value;
            }
            else
                throw new InvalidOperationException("This extension method only supports OdbcConnectionStringBuilder and OleDbConnectionStringBuilder.");
        }
        
        public static string GetSystemDatabase(this DbConnectionStringBuilder builder, DataAccessProviderType? providerType = null)
        {
            if (providerType != null && providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                return builder.TryGetValue("Jet OLEDB:System Database", out var value)
                    ? (string)value
                    : null;
            }

            if (providerType != null && providerType == DataAccessProviderType.Odbc ||
                IsOdbc(builder))
            {
                return builder.TryGetValue("SystemDB", out var value)
                    ? (string)value
                    : null;
            }

            return builder.TryGetValue("Jet OLEDB:System Database", out var oleDbValue)
                ? (string)oleDbValue
                : builder.TryGetValue("SystemDB", out var odbcValue)
                    ? (string)odbcValue
                    : null;
        }

        public static void SetSystemDatabase(this DbConnectionStringBuilder builder, string value, DataAccessProviderType? providerType = null)
        {
            if (providerType != null && providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                builder["Jet OLEDB:System Database"] = value;
            }
            else if (providerType != null && providerType == DataAccessProviderType.Odbc ||
                     IsOdbc(builder))
            {
                builder["SystemDB"] = value;
            }
            else
                throw new InvalidOperationException("This extension method only supports OdbcConnectionStringBuilder and OleDbConnectionStringBuilder.");
        }
        
        private static bool IsOdbc(DbConnectionStringBuilder builder)
        {
            return builder
                .GetType()
                .GetTypesInHierarchy()
                .Any(t => string.Equals(t.FullName, "System.Data.Odbc.OdbcConnectionStringBuilder"));
        }

        private static bool IsOleDb(DbConnectionStringBuilder builder)
        {
            return builder
                .GetType()
                .GetTypesInHierarchy()
                .Any(t => string.Equals(t.FullName, "System.Data.OleDb.OleDbConnectionStringBuilder"));
        }
    }
}