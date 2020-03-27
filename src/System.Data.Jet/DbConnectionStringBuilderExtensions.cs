using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;

namespace System.Data.Jet
{
    public static class DbConnectionStringBuilderExtensions
    {
        public static string GetProvider(this DbConnectionStringBuilder builder)
        {
            if (IsOleDb(builder))
            {
                return (string) builder["provider"];
            }

            if (IsOdbc(builder))
            {
                return (string) builder["driver"];
            }

            throw new InvalidOperationException("This extension method only supports OdbcConnectionStringBuilder and OleDbConnectionStringBuilder.");
        }

        public static void SetProvider(this DbConnectionStringBuilder builder, string value)
        {
            if (IsOleDb(builder))
            {
                builder["provider"] = value;
            }
            else if (IsOdbc(builder))
            {
                builder["driver"] = Regex.Replace(value.Trim(), @"^(?<!\{).*(?!\})$", @"{$1}", RegexOptions.IgnoreCase);
            }
            else
                throw new InvalidOperationException("This extension method only supports OdbcConnectionStringBuilder and OleDbConnectionStringBuilder.");
        }
        
        public static string GetDataSource(this DbConnectionStringBuilder builder)
        {
            if (IsOleDb(builder))
            {
                return (string) builder["data source"];
            }

            if (IsOdbc(builder))
            {
                return (string) builder["dbq"];
            }

            throw new InvalidOperationException("This extension method only supports OdbcConnectionStringBuilder and OleDbConnectionStringBuilder.");
        }

        public static void SetDataSource(this DbConnectionStringBuilder builder, string value)
        {
            if (IsOleDb(builder))
            {
                builder["data source"] = value;
            }
            else if (IsOdbc(builder))
            {
                builder["dbq"] = value;
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