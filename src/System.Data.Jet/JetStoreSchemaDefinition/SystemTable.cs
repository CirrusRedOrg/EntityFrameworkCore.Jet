using System;
using System.Data;
using System.Data.OleDb;

namespace System.Data.Jet.JetStoreSchemaDefinition
{
    class SystemTable
    {
        public SystemTable()
        {
            Columns = new ColumnCollection();
        }

        public string Name { get; set; }
        public string CreateStatement { get; set; }
        public string DropStatement { get; set; }
        public string ClearStatement { get; set; }
        public string EntityType { get; set; }
        public Func<OleDbConnection, DataTable> GetDataTable { get; set; }

        public ColumnCollection Columns { get; private set; }

        public string TableName
        {
            get { return "#" + Name; }
        }

        public override string ToString()
        {
            return string.Format("Name: {0}", Name);
        }
    }
}