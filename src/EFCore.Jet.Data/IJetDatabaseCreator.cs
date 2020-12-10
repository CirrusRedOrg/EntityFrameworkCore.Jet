namespace EntityFrameworkCore.Jet.Data
{
    public enum CollatingOrder
    {
        Undefined = -1,
        Neutral = 1024,
        Arabic = 1025,
        ChineseTraditional = 1028,
        Czech = 1029,
        Norwdan = 1030,
        PDXNor = 1030,
        Greek = 1032,
        General = 1033,
        PDXIntl = 1033,
        Spanish = 1034,
        Hebrew = 1037,
        Hungarian = 1038,
        Icelandic = 1039,
        Japanese = 1041,
        Korean = 1042,
        Dutch = 1043,
        Polish = 1045,
        Cyrillic = 1049,
        PDXSwe = 1053,
        SwedFin = 1053,
        Thai = 1054,
        Turkish = 1055,
        Slovenian = 1060,
        ChineseSimplified = 2052,
    }

    public enum DatabaseVersion
    {
        Newest = -1,
        NewestSupported = 0,
        Version10 = 10,
        Version11 = 11,
        Version20 = 20,
        Version30 = 30,
        Version40 = 40,
        Version120 = 120
    }
    
    public interface IJetDatabaseCreator
    {
        void CreateDatabase(
            string fileNameOrConnectionString,
            DatabaseVersion version = DatabaseVersion.NewestSupported,
            CollatingOrder collatingOrder = CollatingOrder.General,
            string databasePassword = null);

        void CreateDualTable(
            string fileNameOrConnectionString,
            string databasePassword = null);
    }
}