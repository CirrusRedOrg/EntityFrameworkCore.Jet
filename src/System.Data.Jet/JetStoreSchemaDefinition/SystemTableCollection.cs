using System.Collections.ObjectModel;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;

namespace System.Data.Jet.JetStoreSchemaDefinition
{
    class SystemTableCollection : KeyedCollection<String, SystemTable>
    {
        public SystemTableCollection() : base(StringComparer.OrdinalIgnoreCase){}

        public void Refresh()
        {
            this.Clear();

            XmlReader xmlReader = JetProviderManifest.GetStoreSchemaDescription();
            XmlDocument xmlDocument = new XmlDocument();
            xmlDocument.Load(xmlReader);
            foreach (XmlNode node in xmlDocument.GetElementsByTagName("EntityContainer").Cast<XmlNode>().First().ChildNodes)
            {
                XmlElement xmlElement = node as XmlElement;
                if (xmlElement == null || xmlElement.Name != "EntitySet")
                    continue;

                string nameAttribute = node.Attributes["Name"].Value;
                string entityTypeAttribute = node.Attributes["EntityType"].Value;
                Debug.Assert(nameAttribute.StartsWith("S"));
                string name = nameAttribute.Substring(1);
                Debug.Assert(entityTypeAttribute.StartsWith("Self."));
                string entityType = entityTypeAttribute.Substring(5);
                MethodInfo getDataTableMethodInfo = typeof(JetStoreSchemaDefinitionRetrieve).GetMethod("Get" + name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                Func<DbConnection, DataTable> getDataTableDelegate = (Func<DbConnection, DataTable>) getDataTableMethodInfo.CreateDelegate(typeof (Func<DbConnection, DataTable>));
                SystemTable systemTable = new SystemTable()
                {
                    Name = name,
                    GetDataTable = getDataTableDelegate,
                    EntityType = entityType
                };
                this.Add(systemTable);
            }


            foreach (XmlNode node in xmlDocument.GetElementsByTagName("EntityType").Cast<XmlNode>())
            {
                string entityType = node.Attributes["Name"].Value;
                foreach (SystemTable systemTable in this.Where(_ => _.EntityType == entityType)) // Some tables are mapped to the same entity
                {
                    foreach (XmlNode propertyNode in node.ChildNodes.Cast<XmlNode>().Where(_ => (_ as XmlElement) != null && (_ as XmlElement).Name == "Property"))
                    {
                        string propertyName = propertyNode.Attributes["Name"].Value;
                        string propertyNullable = propertyNode.Attributes["Nullable"] == null ? null : propertyNode.Attributes["Nullable"].Value;
                        string propertyMaxLength = propertyNode.Attributes["MaxLength"] == null ? null : propertyNode.Attributes["MaxLength"].Value;
                        string propertyType = propertyNode.Attributes["Type"].Value;

                        Column column = new Column()
                        {
                            Name = propertyName,
                            Type = propertyType,
                            Nullable = propertyNullable != "false",
                            MaxLength = string.IsNullOrWhiteSpace(propertyMaxLength) ? (int?) null : int.Parse(propertyMaxLength)
                        };

                        systemTable.Columns.Add(column);
                    }
                }
            }


            foreach (SystemTable table in this)
            {
                table.DropStatement = string.Format("DROP TABLE `{0}`", table.TableName);
                table.ClearStatement = string.Format("DELETE FROM `{0}`", table.TableName);
                
                StringBuilder createStatementStringBuilder = new StringBuilder();
                createStatementStringBuilder.AppendFormat("CREATE TABLE `{0}`\r\n", table.TableName);
                createStatementStringBuilder.Append("(");
                bool first = true;
                foreach (Column column in table.Columns)
                {
                    if (first)
                        first = false;
                    else
                        createStatementStringBuilder.Append(",");
                    createStatementStringBuilder.AppendLine();
                    createStatementStringBuilder.AppendFormat("    `{0}` {1}{2} {3}", 
                        column.Name, 
                        column.Type, 
                        column.MaxLength == null ? "" : string.Format("({0})", column.MaxLength), 
                        column.Nullable ? "" : "NOT NULL");
                }
                createStatementStringBuilder.AppendLine();
                createStatementStringBuilder.AppendLine(")");

                table.CreateStatement = createStatementStringBuilder.ToString();
            }


        }

        protected override string GetKeyForItem(SystemTable item)
        {
            return item.Name;
        }
    }
}
