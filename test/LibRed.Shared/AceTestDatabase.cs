// Explicit usings — see the note in TemporaryDatabase.cs.
using System;
using System.Data.OleDb;
using System.Threading;

namespace LibRed.Tests.Shared;

/// <summary>Opens test databases through an installed ACE OLE DB provider with consistent retry behavior.</summary>
internal static class AceTestDatabase
{
    private static readonly string[] Providers = ["Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0"];

    public static OleDbConnection Open(string path, string? password = null, int attempts = 12)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (attempts < 1) throw new ArgumentOutOfRangeException(nameof(attempts));

        Exception? last = null;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            foreach (string provider in Providers)
            {
                try
                {
                    string passwordPart = password is null ? "" : $"Jet OLEDB:Database Password={password};";
                    var connection = new OleDbConnection(
                        $"Provider={provider};Data Source={path};{passwordPart}OLE DB Services=-4;");
                    connection.Open();
                    return connection;
                }
                catch (Exception ex) when (ex is OleDbException or InvalidOperationException)
                {
                    last = ex;
                }
            }

            if (attempt + 1 < attempts)
                Thread.Sleep(40);
        }

        throw new InvalidOperationException("No Microsoft ACE OLE DB provider could open the test database.", last);
    }
}
