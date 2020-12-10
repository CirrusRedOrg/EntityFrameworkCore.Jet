using System;
using System.Data;

namespace EntityFrameworkCore.Jet.Data
{
    public class PreciseSchema : SchemaProvider
    {
        private readonly Lazy<AdoxSchema> _adoxSchema;
        private readonly Lazy<DaoSchema> _daoSchema;

        public PreciseSchema(JetConnection connection, bool readOnly)
        {
            _adoxSchema = new Lazy<AdoxSchema>(() => new AdoxSchema(connection, true, readOnly), false);
            _daoSchema = new Lazy<DaoSchema>(() => new DaoSchema(connection, true, readOnly), false);
        }

        public override void EnsureDualTable()
            => _adoxSchema.Value.EnsureDualTable(); // prefer ADOX, but DAO works too (but less precise)

        public override DataTable GetTables()
            => _adoxSchema.Value.GetTables(); // either ADOX or DAO is fine

        public override DataTable GetColumns()
        {
            // DAO lacks seed and increment values for auto increment columns and needs a costly workaround to get the
            // numeric scale of decimal columns.
            // ADOX lacks ordinal position information and has very unreliable nullable information.
            
            var dataTable = _adoxSchema.Value.GetColumns();
            var ordinalPositionsAndNullables = _daoSchema.Value.GetOrdinalPositionsAndNullables();

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
            => _adoxSchema.Value.GetIndexes(); // either ADOX or DAO is fine

        public override DataTable GetIndexColumns()
            => _daoSchema.Value.GetIndexColumns(); // either ADOX or DAO is fine

        public override DataTable GetRelations()
            => _daoSchema.Value.GetRelations(); // either ADOX or DAO is fine

        public override DataTable GetRelationColumns()
            => _daoSchema.Value.GetRelationColumns(); // either ADOX or DAO is fine

        public override DataTable GetCheckConstraints()
            => _adoxSchema.Value.GetCheckConstraints(); // DAO does not support CHECK CONSTRAINTs, but ADOX does
        
        public override void RenameTable(string oldTableName, string newTableName)
            => _adoxSchema.Value.RenameTable(oldTableName, newTableName); // either ADOX or DAO is fine

        public override void RenameColumn(string tableName, string oldColumnName, string newColumnName)
            => _adoxSchema.Value.RenameColumn(tableName, oldColumnName, newColumnName); // either ADOX or DAO is fine

        public override void Dispose()
        {
            if (_daoSchema.IsValueCreated)
            {
                _daoSchema.Value.Dispose();
            }

            if (_adoxSchema.IsValueCreated)
            {
                _adoxSchema.Value.Dispose();
            }
        }
    }
}