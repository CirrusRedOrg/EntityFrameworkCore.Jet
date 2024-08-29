using System;
using System.Data;

namespace EntityFrameworkCore.Jet.Data
{
    public class PreciseSchema(JetConnection connection, bool readOnly) : SchemaProvider
    {
        private readonly Lazy<AdoxSchema> _adoxSchema = new(() => new AdoxSchema(connection, true, readOnly), false);
        private readonly Lazy<DaoSchema> _daoSchema = new(() => new DaoSchema(connection, true, readOnly), false);

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
            => _adoxSchema.Value.GetIndexColumns(); // either ADOX or DAO is fine

        //ADOX can get more detail for the onupdate and ondelete rules (set null and set default)
        //DAO can get the relation type (1 to 1 or 1 to many)
        //DAO can get isenforced and isherited
        public override DataTable GetRelations()
        {
            var dataTable = _adoxSchema.Value.GetRelations();
            var reltypes = _daoSchema.Value.GetRelationTypes();

            foreach (DataRow row in dataTable.Rows)
            {
                var relationName = (string)row["RELATION_NAME"];

                if (reltypes.TryGetValue(relationName, out var relationType))
                {
                    row["RELATION_TYPE"] = relationType.relationType;
                    row["IS_ENFORCED"] = relationType.isEnforced;
                    row["IS_INHERITED"] = relationType.isInherited;
                }
            }
            dataTable.AcceptChanges();
            return dataTable;
        }

        public override DataTable GetRelationColumns()
            => _adoxSchema.Value.GetRelationColumns(); // either ADOX or DAO is fine

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