using System.Data.OleDb;
using System.Globalization;
using LibRed;
using Xunit;

namespace LibRed.Engine.Tests;

// A column DEFAULT written by ACE's own DDL, applied by ACE and by LibRed to the same file: the value an insert taking
// it stores must be the same. And a value stored into a column of another type — a date into a number, a number into a
// date — converted as ACE converts it. Each case runs ACE and LibRed on copies of one file and compares what is stored.
[Collection(AceCollection.Name)]
public class DefaultValueParityAccessTests
{
    [Theory]
    [InlineData("LONG", "=1")]                      // a leading = is stored as written and read past
    [InlineData("LONG", "=1+1")]
    [InlineData("TEXT(20)", "='a'")]
    [InlineData("YESNO", "=1")]
    [InlineData("LONG", "True")]                    // the bare constants are what they are, not text
    [InlineData("TEXT(20)", "Yes")]
    [InlineData("LONG", "Null")]
    [InlineData("TEXT(20)", "Null")]
    [InlineData("DATETIME", "On")]
    [InlineData("TEXT(20)", "Z")]                   // any other bare word is text
    [InlineData("DATETIME", "0")]                   // a number into a date is the date at that serial
    [InlineData("DATETIME", "1e3")]
    [InlineData("LONG", "#2020-01-02#")]            // a date into a number is its serial
    [InlineData("DOUBLE", "#18:00:00#")]           // ACE's DDL refuses a date literal with a space in it
    [InlineData("YESNO", "#18:00:00#")]
    [InlineData("TEXT(20)", "'abcdefghijklmnopqrstuvwxyz'")] // text is cut to the column's width
    [InlineData("TEXT(20)", "abcdefghijklmnopqrstuvwxyz")]
    [InlineData("TEXT(5)", "12345678")]
    [InlineData("CHAR(5)", "'abcdefgh'")]
    [InlineData("LONG", "'5'")]
    public void A_default_ACE_wrote_inserts_what_ACE_inserts(string type, string defaultSql)
    {
        string path = TemporaryDatabase.CreatePath("default-parity-");
        try
        {
            JetDatabase.Create(path);
            Ace(path, $"CREATE TABLE T (K LONG, V {type} DEFAULT {defaultSql})", "INSERT INTO T (K) VALUES (1)");

            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);
            engine.ExecuteNonQuery("INSERT INTO T (K) VALUES (2)");
            object?[][] rows = [.. engine.ExecuteQuery("SELECT V FROM T ORDER BY K").Rows];
            Assert.Equal(Text(rows[0][0]), Text(rows[1][0]));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // An AutoNumber whose DEFAULT is GenUniqueID() is a Random AutoNumber to ACE with or without the leading =.
    [Theory]
    [InlineData("GenUniqueID()")]
    [InlineData("=GenUniqueID()")]
    public void An_AutoNumber_defaulting_to_GenUniqueID_is_random_with_or_without_the_equals(string defaultSql)
    {
        string path = TemporaryDatabase.CreatePath("random-autonumber-");
        try
        {
            JetDatabase.Create(path);
            Ace(path, $"CREATE TABLE T (Id COUNTER DEFAULT {defaultSql}, K LONG)");

            using var db = JetDatabase.Open(path);
            Assert.True(db.Catalog.FindTable("T")!.FindColumn("Id")!.IsRandomAutoNumber);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Theory]
    [InlineData("LONG", "#2020-01-02#")]
    [InlineData("LONG", "#2020-01-02 18:00:00#")]
    [InlineData("DOUBLE", "#2020-01-02 18:00:00#")]
    [InlineData("YESNO", "#1899-12-30 18:00:00#")]
    [InlineData("YESNO", "#1899-12-30 06:00:00#")]
    [InlineData("DATETIME", "0")]
    [InlineData("DATETIME", "1000")]
    [InlineData("DATETIME", "-1")]
    [InlineData("DATETIME", "True")]
    [InlineData("DATETIME", "2.5")]
    public void A_value_inserted_into_another_type_is_stored_as_ACE_stores_it(string type, string value)
    {
        string basePath = TemporaryDatabase.CreatePath("stored-date-base-");
        string acePath = TemporaryDatabase.CreatePath("stored-date-ace-");
        string libredPath = TemporaryDatabase.CreatePath("stored-date-libred-");
        try
        {
            JetDatabase.Create(basePath);
            using (var db = JetDatabase.Open(basePath, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery($"CREATE TABLE T (K LONG, V {type})");
            File.Copy(basePath, acePath, overwrite: true);
            File.Copy(basePath, libredPath, overwrite: true);
            string insert = $"INSERT INTO T (K, V) VALUES (1, {value})";

            Ace(acePath, insert);
            using (var db = JetDatabase.Open(libredPath, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(insert);

            Assert.Equal(StoredValue(acePath), StoredValue(libredPath));
        }
        finally
        {
            TemporaryDatabase.Delete(basePath);
            TemporaryDatabase.Delete(acePath);
            TemporaryDatabase.Delete(libredPath);
        }
    }

    // Each statement through ACE, on one connection released before LibRed opens the file.
    private static void Ace(string path, params string[] statements)
    {
        using (OleDbConnection connection = AceTestDatabase.Open(path))
            foreach (string sql in statements)
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        OleDbConnection.ReleaseObjectPool();
    }

    private static string StoredValue(string path)
    {
        using var db = JetDatabase.Open(path);
        return Text(new QueryEngine(db).ExecuteQuery("SELECT V FROM T").Rows.Single()[0]);
    }

    private static string Text(object? value) => value switch
    {
        null => "NULL",
        DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)!,
    };
}
