using System.Text.RegularExpressions;

namespace System.Data.Jet.JetStoreSchemaDefinition
{
    static class JetStoreDatabaseHandling
    {
        private static Regex _regExParseCreateDatabaseCommand;
        private static Regex _regExParseCreateDatabaseCommandFromConnection;
        private static Regex _regExParseDropDatabaseCommand;
        private static Regex _regExParseDropDatabaseCommandFromConnection;
        private static Regex _regExIsCreateOrDropDatabaseCommand;
        private static Regex _regExExtractFilenameFromConnectionString;
        static JetStoreDatabaseHandling()
        {
            _regExIsCreateOrDropDatabaseCommand = new Regex(
                @"(^\s*create\s*database\s*.*$)+|(^drop\s*database\s*.*$)",
                RegexOptions.IgnoreCase);

            _regExParseCreateDatabaseCommand = new Regex(
                @"^\s*create\s*database\s*(?<filename>.*)\s*$",
                RegexOptions.IgnoreCase);

            _regExParseDropDatabaseCommand = new Regex(
                @"^\s*drop\s*database\s*(?<filename>.*)\s*;*\s*$",
                RegexOptions.IgnoreCase | RegexOptions.RightToLeft);


            _regExParseCreateDatabaseCommandFromConnection = new Regex(
                @"^\s*create\s*database\s*(?<connectionString>provider\s*=\s*.*)\s*$",
                RegexOptions.IgnoreCase);

            _regExParseDropDatabaseCommandFromConnection = new Regex(
                @"^\s*drop\s*database\s*(?<connectionString>provider\s*=\s*.*)\s*$",
                RegexOptions.IgnoreCase);

            _regExExtractFilenameFromConnectionString = new Regex(
                @"provider=.*;\s*data\s+source\s*=\s*(?<filename>.*)\s*;??.*$",
                RegexOptions.IgnoreCase);


        }

        public static bool TryDatabaseOperation(string commandText)
        {
            Match match = _regExIsCreateOrDropDatabaseCommand.Match(commandText);
            if (!match.Success)
                return false;

            match = _regExParseCreateDatabaseCommandFromConnection.Match(commandText);
            if (match.Success)
            {
                AdoxWrapper.CreateEmptyDatabase(match.Groups["connectionString"].Value);
                return true;
            }

            match = _regExParseCreateDatabaseCommand.Match(commandText);
            if (match.Success)
            {
                string fileName = match.Groups["filename"].Value;
                if (string.IsNullOrWhiteSpace(fileName))
                    throw new Exception("Missing file name");
                AdoxWrapper.CreateEmptyDatabase(JetConnection.GetConnectionString(fileName));
                return true;
            }

            match = _regExParseDropDatabaseCommandFromConnection.Match(commandText);
            if (match.Success)
            {
                string fileName;
                string connectionString = match.Groups["connectionString"].Value;
                fileName = ExtractFileNameFromConnectionString(connectionString);

                if (string.IsNullOrWhiteSpace(fileName))
                    throw new Exception("Missing file name");

                DeleteFile(fileName.Trim());
                return true;
            }

            match = _regExParseDropDatabaseCommand.Match(commandText);
            if (match.Success)
            {
                string fileName = match.Groups["filename"].Value;

                if (string.IsNullOrWhiteSpace(fileName))
                    throw new Exception("Missing file name");

                DeleteFile(fileName.Trim());
                return true;
            }

            throw new Exception(commandText + " is not a valid database command");

        }

        public static string ExtractFileNameFromConnectionString(string connectionString)
        {
            string fileName;
            Match match =_regExExtractFilenameFromConnectionString.Match(connectionString);
            if (match.Success)
                fileName = match.Groups["filename"].Value;
            else
                fileName = connectionString;
            return fileName;
        }

        public static void DeleteFile(string fileName, bool throwOnError = true)
        {
            if (throwOnError && !System.IO.File.Exists(fileName.Trim()))
                throw new System.IO.FileNotFoundException("Database file not found", fileName.Trim());

            try
            {
                System.IO.File.Delete(fileName.Trim());
            }
            catch
            {
                if (throwOnError)
                    throw;
            }
        }
    }
}