using System.Data.Common;
using LibRed.Data;
using Xunit;

namespace LibRed.Ado.Tests;

public class LibRedTransactionTests
{
    private static string FreshDb()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-txn-{Guid.NewGuid():N}.accdb");
        LibRedConnection.CreateDatabase($"Data Source={path}");
        return path;
    }

    private static LibRedConnection OpenWithTable(string path)
    {
        var conn = new LibRedConnection($"Data Source={path}");
        conn.Open();
        using var create = conn.CreateCommand();
        create.CommandText = "CREATE TABLE `T` (`Id` INTEGER PRIMARY KEY)";
        create.ExecuteNonQuery();
        return conn;
    }

    private static void Insert(LibRedConnection conn, DbTransaction? txn, int id)
    {
        using var c = conn.CreateCommand();
        c.Transaction = txn;
        c.CommandText = $"INSERT INTO `T` (`Id`) VALUES ({id})";
        c.ExecuteNonQuery();
    }

    private static int Count(LibRedConnection conn)
    {
        using var c = conn.CreateCommand();
        c.CommandText = "SELECT COUNT(*) FROM `T`";
        return Convert.ToInt32(c.ExecuteScalar());
    }

    [Fact]
    public void Rolling_back_to_a_savepoint_undoes_only_work_after_it()
    {
        string path = FreshDb();
        try
        {
            using LibRedConnection conn = OpenWithTable(path);
            using DbTransaction txn = conn.BeginTransaction();
            Assert.True(txn.SupportsSavepoints);

            Insert(conn, txn, 1);
            txn.Save("sp1");
            Insert(conn, txn, 2);
            txn.Rollback("sp1"); // undoes id=2 only
            txn.Commit();

            Assert.Equal(1, Count(conn)); // id=1 kept, id=2 gone
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Releasing_a_savepoint_leaves_its_work_under_the_transaction()
    {
        string path = FreshDb();
        try
        {
            using LibRedConnection conn = OpenWithTable(path);
            using DbTransaction txn = conn.BeginTransaction();

            Insert(conn, txn, 1);
            txn.Save("sp1");
            Insert(conn, txn, 2);
            txn.Release("sp1");  // sp1's work merges into the transaction, not committed independently
            txn.Rollback();       // rolls the whole transaction back → both inserts undone

            Assert.Equal(0, Count(conn));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_command_bound_to_another_connections_transaction_is_rejected()
    {
        string path = FreshDb();
        try
        {
            using LibRedConnection connA = OpenWithTable(path);
            using var connB = new LibRedConnection($"Data Source={path}");
            connB.Open();
            using DbTransaction txnB = connB.BeginTransaction();

            using DbCommand cmd = connA.CreateCommand();
            cmd.Transaction = txnB; // foreign transaction
            cmd.CommandText = "INSERT INTO `T` (`Id`) VALUES (1)";

            Assert.Throws<InvalidOperationException>(() => cmd.ExecuteNonQuery());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_command_bound_to_a_completed_transaction_is_rejected()
    {
        string path = FreshDb();
        try
        {
            using LibRedConnection conn = OpenWithTable(path);
            DbTransaction txn = conn.BeginTransaction();
            txn.Commit(); // now completed; the connection no longer holds it

            using DbCommand cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = "INSERT INTO `T` (`Id`) VALUES (1)";

            Assert.Throws<InvalidOperationException>(() => cmd.ExecuteNonQuery());
        }
        finally { File.Delete(path); }
    }
}
