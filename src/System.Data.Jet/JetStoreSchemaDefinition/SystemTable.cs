using System.Data.Common;

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
        public Func<DbConnection, DataTable> GetDataTable { get; set; }

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