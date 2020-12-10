using System;

namespace EntityFrameworkCore.Jet.Data
{
    public interface ISchemaOperationsProvider
        : IDisposable
    {
        void EnsureDualTable();

        void RenameTable(string oldTableName, string newTableName);
        void RenameColumn(string tableName, string oldColumnName, string newColumnName);
    }
}