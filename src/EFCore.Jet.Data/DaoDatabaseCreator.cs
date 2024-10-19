using System;
using EntityFrameworkCore.Jet.Data.JetStoreSchemaDefinition;
using System.Linq;

namespace EntityFrameworkCore.Jet.Data
{
    public class DaoDatabaseCreator
        : JetDatabaseCreator
    {
        public override void CreateDatabase(
            string fileNameOrConnectionString,
            DatabaseVersion version = DatabaseVersion.NewestSupported,
            CollatingOrder collatingOrder = CollatingOrder.General,
            string? databasePassword = null)
        {
            if (databasePassword is { Length: > 20 })
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
                using var dbEngine = CreateDbEngine();

                var databaseType = version switch
                {
                    DatabaseVersion.Version10 => (int) DatabaseTypeEnum.dbVersion10,
                    DatabaseVersion.Version11 => (int) DatabaseTypeEnum.dbVersion11,
                    DatabaseVersion.Version20 => (int) DatabaseTypeEnum.dbVersion20,
                    DatabaseVersion.Version30 => (int) DatabaseTypeEnum.dbVersion30,
                    DatabaseVersion.Version40 => (int) DatabaseTypeEnum.dbVersion40,
                    DatabaseVersion.Version120 => (int) DatabaseTypeEnum.dbVersion120,
                    _ => 0,
                };

                var daoCollatingOrder = (CollatingOrderEnum) collatingOrder;
                var collatingOrderString = daoCollatingOrder switch
                {
                    CollatingOrderEnum.dbSortArabic => ";LANGID=0x0401;CP=1256;COUNTRY=0",
                    CollatingOrderEnum.dbSortChineseSimplified => ";LANGID=0x0804;CP=936;COUNTRY=0",
                    CollatingOrderEnum.dbSortChineseTraditional => ";LANGID=0x0404;CP=950;COUNTRY=0",
                    CollatingOrderEnum.dbSortCyrillic => ";LANGID=0x0419;CP=1251;COUNTRY=0",
                    CollatingOrderEnum.dbSortCzech => ";LANGID=0x0405;CP=1250;COUNTRY=0",
                    CollatingOrderEnum.dbSortDutch => ";LANGID=0x0413;CP=1252;COUNTRY=0",
                    CollatingOrderEnum.dbSortGeneral => ";LANGID=0x0409;CP=1252;COUNTRY=0",
                    CollatingOrderEnum.dbSortGreek => ";LANGID=0x0408;CP=1253;COUNTRY=0",
                    CollatingOrderEnum.dbSortHebrew => ";LANGID=0x040D;CP=1255;COUNTRY=0",
                    CollatingOrderEnum.dbSortHungarian => ";LANGID=0x040E;CP=1250;COUNTRY=0",
                    CollatingOrderEnum.dbSortIcelandic => ";LANGID=0x040F;CP=1252;COUNTRY=0",
                    CollatingOrderEnum.dbSortJapanese => ";LANGID=0x0411;CP=932;COUNTRY=0",
                    CollatingOrderEnum.dbSortKorean => ";LANGID=0x0412;CP=949;COUNTRY=0",
                    //CollatingOrderEnum.dbSortNordic => ";LANGID=0x041D;CP=1252;COUNTRY=0",
                    CollatingOrderEnum.dbSortNorwdan => ";LANGID=0x0406;CP=1252;COUNTRY=0",
                    CollatingOrderEnum.dbSortPolish => ";LANGID=0x0415;CP=1250;COUNTRY=0",
                    CollatingOrderEnum.dbSortSlovenian => ";LANGID=0x0424;CP=1250;COUNTRY=0",
                    CollatingOrderEnum.dbSortSpanish => ";LANGID=0x040A;CP=1252;COUNTRY=0",
                    CollatingOrderEnum.dbSortSwedFin => ";LANGID=0x041D;CP=1252;COUNTRY=0",
                    CollatingOrderEnum.dbSortThai => ";LANGID=0x041E;CP=874;COUNTRY=0",
                    CollatingOrderEnum.dbSortTurkish => ";LANGID=0x041F;CP=1254;COUNTRY=0",
                    _ => ";LANGID=0x0409;CP=1252;COUNTRY=0",
                };

                if (!string.IsNullOrEmpty(databasePassword))
                {
                    collatingOrderString += $";pwd={databasePassword}";
                }

                using var workspace = dbEngine.CreateWorkspace(string.Empty, "admin", string.Empty, WorkspaceTypeEnum.dbUseJet);
                using var database = databaseType > 0
                    ? workspace.CreateDatabase(filePath, collatingOrderString, databaseType)
                    : workspace.CreateDatabase(filePath, collatingOrderString);
            }
            catch (Exception e)
            {
                throw new Exception($"Cannot create database \"{filePath}\" using DAO.", e);
            }
        }
        
        private static dynamic CreateDbEngine()
        {
            var progids = Enumerable.Range(12, 6)
                .Reverse()
                .Select(n => n * 10)
                .Concat(
                    Environment.Is64BitProcess
                        ? []
                        : new[] {36}) // DAO 3.6 is only available as an x86 library
                .Select(n => "DAO.DBEngine." + n)
                .ToArray();
            return ComObject.CreateFirstFrom(progids);
        }

        protected enum WorkspaceTypeEnum
        {
            dbUseJet = 2,
        }

        protected enum CollatingOrderEnum
        {
            dbSortUndefined = -1,
            dbSortNeutral = 1024,
            dbSortArabic = 1025,
            dbSortChineseTraditional = 1028,
            dbSortCzech = 1029,
            dbSortNorwdan = 1030,
            dbSortPDXNor = 1030,
            dbSortGreek = 1032,
            dbSortGeneral = 1033,
            dbSortPDXIntl = 1033,
            dbSortSpanish = 1034,
            dbSortHebrew = 1037,
            dbSortHungarian = 1038,
            dbSortIcelandic = 1039,
            dbSortJapanese = 1041,
            dbSortKorean = 1042,
            dbSortDutch = 1043,
            dbSortPolish = 1045,
            dbSortCyrillic = 1049,
            dbSortPDXSwe = 1053,
            dbSortSwedFin = 1053,
            dbSortThai = 1054,
            dbSortTurkish = 1055,
            dbSortSlovenian = 1060,
            dbSortChineseSimplified = 2052,
        }

        protected enum DatabaseTypeEnum
        {
            dbVersion10 = 0x00000001,
            dbEncrypt = 0x00000002,
            dbDecrypt = 0x00000004,
            dbVersion11 = 0x00000008,
            dbVersion20 = 0x00000010,
            dbVersion30 = 0x00000020,
            dbVersion40 = 0x00000040,
            dbVersion120 = 0x00000080
        }
        
        [Flags]
        protected enum TableDefAttributeEnum
        {
            dbSystemObject = (1 << 31) | 0x0000002, // 0x80000002, this actually means hidden
            dbHiddenObject = 0x00000001, // does not really mean hidden, but temporary (which will also be hidden)
            dbAttachExclusive = 0x00010000,
            dbAttachSavePWD = 0x00020000,
            dbAttachedODBC = 0x20000000,
            dbAttachedTable = 0x40000000,
        }
    }
}