using System;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;

namespace EntityFrameworkCore.Jet.Data
{
    public static class DbConnectionStringBuilderExtensions
    {
        public static string? GetProvider(this DbConnectionStringBuilder builder, DataAccessProviderType? providerType = null)
        {
            if (providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                return builder.TryGetValue("Provider", out var value)
                    ? (string)value
                    : null;
            }

            if (providerType == DataAccessProviderType.Odbc ||
                IsOdbc(builder))
            {
                return builder.TryGetValue("DRIVER", out var value)
                    ? ((string)value).TrimStart('{').TrimEnd('}')
                    : null;
            }

            return builder.TryGetValue("Provider", out var oleDbValue)
                ? (string)oleDbValue
                : builder.TryGetValue("DRIVER", out var odbcValue)
                    ? ((string)odbcValue).TrimStart('{').TrimEnd('}')
                    : null;
        }

        public static void SetProvider(this DbConnectionStringBuilder builder, string value, DataAccessProviderType? providerType = null)
        {
            if (providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                builder["Provider"] = value;
            }
            else if (providerType == DataAccessProviderType.Odbc ||
                     IsOdbc(builder))
            {
                builder["DRIVER"] = Regex.Replace(value.Trim(), @"^(?<!\{)(.*)(?!\})$", @"{$1}", RegexOptions.IgnoreCase);
            }
            else
                throw new InvalidOperationException("This extension method only supports OdbcConnectionStringBuilder and OleDbConnectionStringBuilder.");
        }
        
        public static string? GetDataSource(this DbConnectionStringBuilder builder, DataAccessProviderType? providerType = null)
        {
            if (providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                return builder.TryGetValue("Data Source", out var value)
                    ? (string)value
                    : null;
            }

            if (providerType == DataAccessProviderType.Odbc ||
                IsOdbc(builder))
            {
                return builder.TryGetValue("DBQ", out var value)
                    ? (string)value
                    : null;
            }

            return builder.TryGetValue("Data Source", out var oleDbValue)
                ? (string)oleDbValue
                : builder.TryGetValue("DBQ", out var odbcValue)
                    ? (string)odbcValue
                    : null;
        }

        public static void SetDataSource(this DbConnectionStringBuilder builder, string value, DataAccessProviderType? providerType = null)
        {
            if (providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                builder["Data Source"] = value;
            }
            else if (providerType == DataAccessProviderType.Odbc ||
                     IsOdbc(builder))
            {
                builder["DBQ"] = value;
            }
            else
                throw new InvalidOperationException("This extension method only supports OdbcConnectionStringBuilder and OleDbConnectionStringBuilder.");
        }
        
        public static string? GetUserId(this DbConnectionStringBuilder builder, DataAccessProviderType? providerType = null)
        {
            if (IsOleDb(builder))
            {
                return builder.TryGetValue("User ID", out var value)
                    ? (string)value
                    : null;
            }

            if (providerType == DataAccessProviderType.Odbc ||
                IsOdbc(builder))
            {
                return builder.TryGetValue("UID", out var value)
                    ? (string)value
                    : null;
            }

            return builder.TryGetValue("User ID", out var oleDbValue)
                ? (string)oleDbValue
                : builder.TryGetValue("UID", out var odbcValue)
                    ? (string)odbcValue
                    : null;
        }

        public static void SetUserId(this DbConnectionStringBuilder builder, string value, DataAccessProviderType? providerType = null)
        {
            if (providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                builder["User ID"] = value;
            }
            else if (providerType == DataAccessProviderType.Odbc ||
                     IsOdbc(builder))
            {
                builder["UID"] = value;
            }
            else
                throw new InvalidOperationException("This extension method only supports OdbcConnectionStringBuilder and OleDbConnectionStringBuilder.");
        }
        
        public static string? GetPassword(this DbConnectionStringBuilder builder, DataAccessProviderType? providerType = null)
        {
            if (providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                return builder.TryGetValue("Password", out var value)
                    ? (string)value
                    : null;
            }

            // MAJOR ISSUE: PWD seems to be used for the datbase password AND the workgroup user password.
            //              See https://stackoverflow.com/questions/65025810/how-to-specify-mdw-username-and-password-user-level-security-and-the-database
            //              As a workaround, we will assume the PWD field to contain the database password, if no SYSTEMDB has been specified.
            //              Otherwise, we will handle the field as the workgroup user password.

            if (providerType == DataAccessProviderType.Odbc ||
                IsOdbc(builder))
            {
                return !string.IsNullOrEmpty(builder.GetSystemDatabase(providerType))
                    ? builder.TryGetValue("PWD", out var value)
                        ? (string) value
                        : null
                    : null;
            }

            return builder.TryGetValue("Password", out var oleDbValue)
                ? (string) oleDbValue
                : !string.IsNullOrEmpty(builder.GetSystemDatabase(providerType))
                    ? builder.TryGetValue("PWD", out var odbcValue)
                        ? (string) odbcValue
                        : null
                    : null;
        }

        public static void SetPassword(this DbConnectionStringBuilder builder, string value, DataAccessProviderType? providerType = null)
        {
            if (providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                builder["Password"] = value;
            }
            else if (providerType == DataAccessProviderType.Odbc ||
                     IsOdbc(builder))
            {
                builder["PWD"] = value;
            }
            else
                throw new InvalidOperationException("This extension method only supports OdbcConnectionStringBuilder and OleDbConnectionStringBuilder.");
        }
        
        public static string? GetSystemDatabase(this DbConnectionStringBuilder builder, DataAccessProviderType? providerType = null)
        {
            if (providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                return builder.TryGetValue("Jet OLEDB:System Database", out var value)
                    ? (string)value
                    : null;
            }

            if (providerType == DataAccessProviderType.Odbc ||
                IsOdbc(builder))
            {
                return builder.TryGetValue("SYSTEMDB", out var value)
                    ? (string)value
                    : null;
            }

            return builder.TryGetValue("Jet OLEDB:System Database", out var oleDbValue)
                ? (string)oleDbValue
                : builder.TryGetValue("SYSTEMDB", out var odbcValue)
                    ? (string)odbcValue
                    : null;
        }

        public static void SetSystemDatabase(this DbConnectionStringBuilder builder, string value, DataAccessProviderType? providerType = null)
        {
            if (providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                builder["Jet OLEDB:System Database"] = value;
            }
            else if (providerType == DataAccessProviderType.Odbc ||
                     IsOdbc(builder))
            {
                builder["SYSTEMDB"] = value;
            }
            else
                throw new InvalidOperationException("This extension method only supports OdbcConnectionStringBuilder and OleDbConnectionStringBuilder.");
        }

        public static string? GetDatabasePassword(this DbConnectionStringBuilder builder, DataAccessProviderType? providerType = null)
        {
            if (providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                return builder.TryGetValue("Jet OLEDB:Database Password", out var value)
                    ? (string)value
                    : null;
            }

            // MAJOR ISSUE: PWD seems to be used for the datbase password AND the workgroup user password.
            //              See https://stackoverflow.com/questions/65025810/how-to-specify-mdw-username-and-password-user-level-security-and-the-database
            //              As a workaround, we will assume the PWD field to contain the database password, if no SYSTEMDB has been specified.
            //              Otherwise, we will handle the field as the workgroup user password.

            if (providerType == DataAccessProviderType.Odbc ||
                IsOdbc(builder))
            {
                return string.IsNullOrEmpty(builder.GetSystemDatabase(providerType))
                    ? builder.TryGetValue("PWD", out var value)
                        ? (string) value
                        : null
                    : null;
            }

            return builder.TryGetValue("Jet OLEDB:Database Password", out var oleDbValue)
                ? (string) oleDbValue
                : string.IsNullOrEmpty(builder.GetSystemDatabase(providerType))
                    ? builder.TryGetValue("PWD", out var odbcValue)
                        ? (string) odbcValue
                        : null
                    : null;
        }

        public static void SetDatabasePassword(this DbConnectionStringBuilder builder, string value, DataAccessProviderType? providerType = null)
        {
            if (providerType == DataAccessProviderType.OleDb ||
                IsOleDb(builder))
            {
                builder["Jet OLEDB:Database Password"] = value;
            }
            else if (providerType == DataAccessProviderType.Odbc ||
                     IsOdbc(builder))
            {
                builder["PWD"] = value;
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