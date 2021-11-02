using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace EntityFrameworkCore.Jet.Data.JetStoreSchemaDefinition
{
    static class JetStoreDatabaseHandling
    {
        private static readonly Regex _regExIsCreateOrDropDatabaseCommand;
        private static readonly Regex _regExParseCreateDatabaseCommand;
        private static readonly Regex _regExParseDropDatabaseCommand;
        private static readonly Regex _regExIsConnectionString;
        private static readonly Regex _regExHasProvider;
        private static readonly Regex _regExExtractFilenameFromConnectionString;

        // TODO: Remove this obsolete block for .NET 5.
        private static readonly Regex _regExParseObsoleteCreateDatabaseCommand;
        private static readonly Regex _regExParseObsoleteDropDatabaseCommand;
        private static readonly Regex _regExParseObsoleteCreateDatabaseCommandFromConnection;
        private static readonly Regex _regExParseObsoleteDropDatabaseCommandFromConnection;

        static JetStoreDatabaseHandling()
        {
            _regExIsCreateOrDropDatabaseCommand = new Regex(
                @"^\s*(?:create|drop)\s+database\s",
                RegexOptions.IgnoreCase);

            // CREATE DATABASE 'Joe''s Database.accdb';
            // CREATE DATABASE 'Joe''s Database.accdb' PASSWORD 'Joe''s secret';
            _regExParseCreateDatabaseCommand = new Regex(
                @"^\s*create\s+database\s+'\s*(?<filename>(?:''|[^'])*)\s*'(\s+password\s+'(?<password>(?:''|[^'])*)')?\s*(?:;|$)",
                RegexOptions.IgnoreCase);

            // DROP DATABASE 'Joe''s Database.accdb';
            _regExParseDropDatabaseCommand = new Regex(
                @"^\s*drop\s+database\s+'\s*(?<filename>(?:''|[^'])*)\s*'\s*(?:;|$)",
                RegexOptions.IgnoreCase);

            // Provider=Microsoft.ACE.OLEDB.12.0
            // Driver=Microsoft.ACE.OLEDB.12.0
            // Data Source=Joe's Database.accdb
            // DBQ=Joe's Database.accdb
            _regExIsConnectionString = new Regex(
                @"^(?:.*;)?\s*(?:provider|driver|data source|dbq)\s*=",
                RegexOptions.IgnoreCase);

            // Provider=Microsoft.ACE.OLEDB.12.0
            // Driver=Microsoft.ACE.OLEDB.12.0
            _regExHasProvider = new Regex(
                @"^(?:.*;)?\s*(?:provider|driver)\s*=",
                RegexOptions.IgnoreCase);

            // Provider=Microsoft.ACE.OLEDB.12.0;Data Source=Joe's Database.accdb;
            // Provider=Microsoft.ACE.OLEDB.12.0;Data Source="Joe's Database.accdb";
            // Provider=Microsoft.ACE.OLEDB.12.0;Data Source='Joe''s Database.accdb';
            _regExExtractFilenameFromConnectionString = new Regex(
                @"^(?:.*;)?\s*(?:data source|dbq)\s*=\s*(?:(?<quote>[""'])\s*(?<filename>.*?)\s*\k<quote>|(?<filename>.*?))\s*(?:;|$)",
                RegexOptions.IgnoreCase);

            // CREATE DATABASE Joe's Database.accdb;
            _regExParseObsoleteCreateDatabaseCommand = new Regex(
                @"^\s*create\s+database\s+(?<filename>.*?)\s*(?:;|$)",
                RegexOptions.IgnoreCase);

            // DROP DATABASE Joe's Database.accdb;
            _regExParseObsoleteDropDatabaseCommand = new Regex(
                @"^\s*drop\s+database\s+(?<filename>.*?)\s*(?:;|$)",
                RegexOptions.IgnoreCase);

            // CREATE DATABASE Provider=Microsoft.ACE.OLEDB.12.0;Data Source=Joe's Database.accdb;
            _regExParseObsoleteCreateDatabaseCommandFromConnection = new Regex(
                @"^\s*create\s+database\s+(?<connectionString>(provider)\s*=\s*.*?)\s*$",
                RegexOptions.IgnoreCase);

            // DROP DATABASE Provider=Microsoft.ACE.OLEDB.12.0;Data Source=Joe's Database.accdb;
            _regExParseObsoleteDropDatabaseCommandFromConnection = new Regex(
                @"^\s*drop\s+database\s+(?<connectionString>provider\s*=\s*.*?)\s*$",
                RegexOptions.IgnoreCase);
        }

        public static bool ProcessDatabaseOperation(JetCommand command)
        {
            var commandText = command.CommandText;

            var match = _regExIsCreateOrDropDatabaseCommand.Match(commandText);
            if (!match.Success)
                return false;

            //
            // CREATE DATABASE:
            //
            
            match = _regExParseCreateDatabaseCommand.Match(commandText);
            if (match.Success)
            {
                var fileName = UnescapeSingleQuotes(match.Groups["filename"].Value);
                var databasePassword = UnescapeSingleQuotes(match.Groups["password"].Value);

                if (string.IsNullOrWhiteSpace(fileName))
                    throw new InvalidOperationException("CREATE DATABASE statement is missing database file name.");
                
                JetConnection.CreateDatabase(fileName, databasePassword: databasePassword);
                return true;
            }

            match = _regExParseObsoleteCreateDatabaseCommand.Match(commandText);
            if (match.Success)
            {
                var fileName = match.Groups["filename"].Value;
                
                if (string.IsNullOrWhiteSpace(fileName))
                    throw new InvalidOperationException("CREATE DATABASE statement is missing database file name or connection string.");
                
                JetConnection.CreateDatabase(fileName);
                return true;
            }
            
            match = _regExParseObsoleteCreateDatabaseCommandFromConnection.Match(commandText);
            if (match.Success)
            {
                var connectionString = ExtractFileNameFromConnectionString(match.Groups["connectionString"].Value);
                
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException("CREATE DATABASE statement is missing database file name or connection string.");
                
                JetConnection.CreateDatabase(connectionString);
                return true;
            }

            //
            // DROP DATABASE:
            //
            
            match = _regExParseDropDatabaseCommand.Match(commandText);
            if (match.Success)
            {
                var fileName = ExpandFileName(UnescapeSingleQuotes(match.Groups["filename"].Value));

                if (string.IsNullOrWhiteSpace(fileName))
                    throw new InvalidOperationException("DROP DATABASE statement is missing database file name or connection string.");

                JetConnection.DropDatabase(fileName);
                return true;
            }

            match = _regExParseObsoleteDropDatabaseCommand.Match(commandText);
            if (match.Success)
            {
                var fileName = match.Groups["filename"].Value;

                if (string.IsNullOrWhiteSpace(fileName))
                    throw new InvalidOperationException("DROP DATABASE statement is missing database file name or connection string.");

                JetConnection.DropDatabase(fileName);
                return true;
            }

            match = _regExParseObsoleteDropDatabaseCommandFromConnection.Match(commandText);
            if (match.Success)
            {
                var connectionString = match.Groups["connectionString"].Value;

                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException("DROP DATABASE statement is missing database file name or connection string.");

                JetConnection.DropDatabase(connectionString);
                return true;
            }
            
            throw new Exception($"\"{commandText}\" is not a valid database command.");
        }

        public static string ExtractFileNameFromConnectionString(string connectionString)
        {
            if (connectionString != null)
            {
                var match = _regExExtractFilenameFromConnectionString.Match(connectionString);
                if (match.Success)
                {
                    var fileName = match.Groups["filename"].Value;

                    if (match.Groups["quote"].Success)
                    {
                        var quoteChar = match.Groups["quote"].Value;
                        fileName = fileName.Replace(quoteChar + quoteChar, quoteChar);
                    }

                    return fileName;
                }
            }

            return connectionString;
        }

        public static bool IsConnectionString(string connectionString)
            => _regExIsConnectionString.IsMatch(connectionString);

        public static bool IsFileName(string fileName)
            => !string.IsNullOrWhiteSpace(fileName) &&
               !IsConnectionString(fileName) &&
               !fileName.ToCharArray()
                   .Intersect(Path.GetInvalidPathChars())
                   .Any();
        
        public static bool HasProvider(string connectionString)
            => _regExHasProvider.IsMatch(connectionString);
        
        public static void DeleteFile(string fileName)
        {
            JetConnection.ClearAllPools();

            fileName = ExpandFileName(fileName);
            
            var directoryPath = Path.GetDirectoryName(fileName) ?? string.Empty;
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);

            if (!File.Exists(fileName))
            {
                return;
            }

            File.Delete(fileName);

            if (string.IsNullOrEmpty(extension) ||
                string.Equals(extension, ".accdb", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(extension, ".mdb", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(Path.Combine(directoryPath, fileNameWithoutExtension + ".laccdb"));
            }
            
            if (string.IsNullOrEmpty(extension) ||
                string.Equals(extension, ".mdb", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(extension, ".accdb", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(Path.Combine(directoryPath, fileNameWithoutExtension + ".ldb"));
            }
        }

        private static string UnescapeSingleQuotes(string value)
            => value.Replace("''", "'");

        public static string ExpandFileName(string fileName)
        {
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));

            if (fileName.StartsWith("|DataDirectory|", StringComparison.OrdinalIgnoreCase))
            {
                var dataDirectory = AppDomain.CurrentDomain.GetData("DataDirectory") as string;
                if (string.IsNullOrEmpty(dataDirectory))
                {
                    dataDirectory = AppDomain.CurrentDomain.BaseDirectory;
                }

                fileName = Path.Combine(dataDirectory, fileName.Substring("|DataDirectory|".Length));
            }

            return EnsureFileExtension(Path.GetFullPath(fileName));
        }

        public static string EnsureFileExtension(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension))
            {
                fileName += ".accdb";
            }

            return fileName;
        }
    }
}