using System.Data;

namespace EntityFrameworkCore.Jet.Data
{
    public class PreciseSchema : SchemaProvider
    {
        private readonly JetConnection _connection;
        private readonly AdoxSchema _adoxSchema;
        private readonly DaoSchema _daoSchema;
        
        public PreciseSchema(JetConnection connection)
        {
            _connection = connection;
            _adoxSchema = new AdoxSchema(connection, true);
            _daoSchema = new DaoSchema(connection, true);
        }

        public override void EnsureDualTable()
            => _adoxSchema.EnsureDualTable(); // either ADOX or DAO is fine

        public override DataTable GetTables()
            => _adoxSchema.GetTables(); // either ADOX or DAO is fine

        public override DataTable GetColumns()
        {
            // DAO lacks seed and increment values for auto increment columns and needs a costly workaround to get the
            // numeric scale of decimal columns.
            // ADOX lacks ordinal position information and has very unreliable nullable information.
            
            var dataTable = _adoxSchema.GetColumns();
            var ordinalPositionsAndNullables = _daoSchema.GetOrdinalPositionsAndNullables();

            foreach (DataRow row in dataTable.Rows)
            {
                var tableName = (string) row["TABLE_NAME"];
                var columnName = (string) row["COLUMN_NAME"];

                if (ordinalPositionsAndNullables.TryGetValue((tableName, columnName), out var ordinalPositionAndNullable))
                {
                    row["ORDINAL_POSITION"] = ordinalPositionAndNullable.OrdinalPosition;
                    row["IS_NULLABLE"] = ordinalPositionAndNullable.Nullable;
                }
            }
            
            dataTable.AcceptChanges();
            return dataTable;
        }

        public override DataTable GetIndexes()
            => _adoxSchema.GetIndexes(); // either ADOX or DAO is fine

        public override DataTable GetIndexColumns()
            => _daoSchema.GetIndexColumns(); // either ADOX or DAO is fine

        public override DataTable GetRelations()
            => _daoSchema.GetRelations(); // either ADOX or DAO is fine

        public override DataTable GetRelationColumns()
            => _daoSchema.GetRelationColumns(); // either ADOX or DAO is fine

        public override DataTable GetCheckConstraints()
            => _adoxSchema.GetCheckConstraints(); // DAO does not support CHECK CONSTRAINTs, but ADOX does

        public override void Dispose()
        {
            _daoSchema.Dispose();
            _adoxSchema.Dispose();
        }
    }
}