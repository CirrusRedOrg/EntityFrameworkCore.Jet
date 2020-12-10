using System;
using System.Data;

namespace EntityFrameworkCore.Jet.Data
{
    public interface ISchemaProvider
        : IDisposable
    {
        DataTable GetTables();
        DataTable GetColumns();
        DataTable GetIndexes();
        DataTable GetIndexColumns();
        DataTable GetRelations();
        DataTable GetRelationColumns();
        DataTable GetCheckConstraints();
    }
}