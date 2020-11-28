namespace System.Data.Jet
{
    public interface ISchemaProvider
        : IDisposable
    {
        void EnsureDualTable();
        DataTable GetTables();
        DataTable GetColumns();
        DataTable GetIndexes();
        DataTable GetIndexColumns();
        DataTable GetRelations();
        DataTable GetRelationColumns();
        DataTable GetCheckConstraints();
    }
}