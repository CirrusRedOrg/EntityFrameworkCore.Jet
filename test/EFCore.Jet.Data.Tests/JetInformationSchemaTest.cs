using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.Data.Tests
{
    // TODO: Call all tests for ODBC and OLE DB.
    [TestClass]
    public class JetInformationSchemaTest
    {
        private const string StoreName = nameof(JetInformationSchemaTest) + ".accdb";

        [ClassInitialize]
        public static void TestFixtureSetup(TestContext context)
        {
            using var connection = Helpers.CreateAndOpenDatabase(StoreName);
            
            var scriptPath = Path.Combine(Path.GetDirectoryName(typeof(JetInformationSchemaTest).Assembly.Location) ?? string.Empty, "Northwind.sql");
            var script = File.ReadAllText(scriptPath);
            Helpers.ExecuteScript(connection, script);
        }

        [ClassCleanup]
        public static void TestFixtureTearDown()
        {
            Helpers.DeleteDatabase(StoreName);
        }

        private void AssertDataReaderContent(string actual, string expected)
            => Assert.AreEqual(expected.Trim(), actual);
        
        [DataTestMethod]
        [DataRow(DataAccessProviderType.Odbc)]
        [DataRow(DataAccessProviderType.OleDb)]
        public void Tables(DataAccessProviderType providerType)
        {
            using var connection = Helpers.OpenDatabase(StoreName, providerType);
            var result = Helpers.GetDataReaderContent(connection, @"SELECT * FROM `INFORMATION_SCHEMA.TABLES`
WHERE TABLE_TYPE <> 'SYSTEM TABLE'
ORDER BY TABLE_NAME");
            
            AssertDataReaderContent(result, @"
`TABLE_NAME` | `TABLE_TYPE` | `VALIDATION_RULE` | `VALIDATION_TEXT`
--- | --- | --- | ---
#Dual | INTERNAL TABLE |  | 
Alphabetical list of products | VIEW |  | 
Categories | BASE TABLE |  | 
Category Sales for 1997 | VIEW |  | 
Current Product List | VIEW |  | 
Customer and Suppliers by City | VIEW |  | 
CustomerCustomerDemo | BASE TABLE |  | 
CustomerDemographics | BASE TABLE |  | 
Customers | BASE TABLE |  | 
Employees | BASE TABLE |  | 
EmployeeTerritories | BASE TABLE |  | 
Invoices | VIEW |  | 
Order Details | BASE TABLE |  | 
Order Details Extended | VIEW |  | 
Order Subtotals | VIEW |  | 
Orders | BASE TABLE |  | 
Orders Qry | VIEW |  | 
Product Sales for 1997 | VIEW |  | 
Products | BASE TABLE |  | 
Products Above Average Price | VIEW |  | 
Products by Category | VIEW |  | 
Quarterly Orders | VIEW |  | 
Region | BASE TABLE |  | 
Sales by Category | VIEW |  | 
Sales Totals by Amount | VIEW |  | 
Shippers | BASE TABLE |  | 
Summary of Sales by Quarter | VIEW |  | 
Summary of Sales by Year | VIEW |  | 
Suppliers | BASE TABLE |  | 
Ten Most Expensive Products | VIEW |  | 
Territories | BASE TABLE |  |");
        }
        
        [DataTestMethod]
        [DataRow(DataAccessProviderType.Odbc)]
        [DataRow(DataAccessProviderType.OleDb)]
        public void Columns(DataAccessProviderType providerType)
        {
            using var connection = Helpers.OpenDatabase(StoreName, providerType);
            var result = Helpers.GetDataReaderContent(
                connection,
                @"SELECT * FROM `INFORMATION_SCHEMA.COLUMNS`
WHERE TABLE_NAME IN ('Orders', 'Order Details Extended', 'Categories')
ORDER BY TABLE_NAME,
         COLUMN_NAME");
            
            AssertDataReaderContent(result, @"
`TABLE_NAME` | `COLUMN_NAME` | `ORDINAL_POSITION` | `DATA_TYPE` | `IS_NULLABLE` | `CHARACTER_MAXIMUM_LENGTH` | `NUMERIC_PRECISION` | `NUMERIC_SCALE` | `COLUMN_DEFAULT` | `VALIDATION_RULE` | `VALIDATION_TEXT` | `IDENTITY_SEED` | `IDENTITY_INCREMENT`
--- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | ---
Categories | CategoryID | 0 | counter | False |  |  |  |  |  |  | 9 | 1
Categories | CategoryName | 1 | varchar | False | 15 |  |  |  |  |  |  | 
Categories | Description | 2 | longchar | True |  |  |  |  |  |  |  | 
Categories | Picture | 3 | longbinary | True |  |  |  |  |  |  |  | 
Order Details Extended | Discount | 5 | single | False |  |  |  |  |  |  |  | 
Order Details Extended | ExtendedPrice | 6 | currency | True |  |  |  |  |  |  |  | 
Order Details Extended | OrderID | 0 | integer | False |  |  |  |  |  |  |  | 
Order Details Extended | ProductID | 1 | integer | False |  |  |  |  |  |  |  | 
Order Details Extended | ProductName | 2 | varchar | False | 40 |  |  |  |  |  |  | 
Order Details Extended | Quantity | 4 | smallint | False |  |  |  |  |  |  |  | 
Order Details Extended | UnitPrice | 3 | currency | False |  |  |  |  |  |  |  | 
Orders | CustomerID | 1 | char | True | 5 |  |  |  |  |  |  | 
Orders | EmployeeID | 2 | integer | True |  |  |  |  |  |  |  | 
Orders | Freight | 7 | decimal | True |  | 18 | 2 | 0 |  |  |  | 
Orders | OrderDate | 3 | datetime | True |  |  |  |  |  |  |  | 
Orders | OrderID | 0 | counter | False |  |  |  |  |  |  | 11078 | 1
Orders | RequiredDate | 4 | datetime | True |  |  |  |  |  |  |  | 
Orders | ShipAddress | 9 | varchar | True | 60 |  |  |  |  |  |  | 
Orders | ShipCity | 10 | varchar | True | 15 |  |  |  |  |  |  | 
Orders | ShipCountry | 13 | varchar | True | 15 |  |  |  |  |  |  | 
Orders | ShipName | 8 | varchar | True | 40 |  |  |  |  |  |  | 
Orders | ShippedDate | 5 | datetime | True |  |  |  |  |  |  |  | 
Orders | ShipPostalCode | 12 | varchar | True | 10 |  |  |  |  |  |  | 
Orders | ShipRegion | 11 | varchar | True | 15 |  |  |  |  |  |  | 
Orders | ShipVia | 6 | integer | True |  |  |  |  |  |  |  |");
        }

        [DataTestMethod]
        [DataRow(DataAccessProviderType.Odbc)]
        [DataRow(DataAccessProviderType.OleDb)]
        public void Indexes(DataAccessProviderType providerType)
        {
            using var connection = Helpers.OpenDatabase(StoreName, providerType);
            var result = Helpers.GetDataReaderContent(
                connection,
                @"SELECT * FROM `INFORMATION_SCHEMA.INDEXES`
WHERE TABLE_NAME = 'Orders' OR 
      TABLE_NAME = 'Order Details'
ORDER BY TABLE_NAME,
         INDEX_NAME");
            
            AssertDataReaderContent(result, @"
`TABLE_NAME` | `INDEX_NAME` | `INDEX_TYPE` | `IS_NULLABLE` | `IGNORES_NULLS`
--- | --- | --- | --- | ---
Order Details | FK_Order_Details_Orders | INDEX | True | False
Order Details | FK_Order_Details_Products | INDEX | True | False
Order Details | OrderID | INDEX | True | False
Order Details | OrdersOrder_Details | INDEX | True | False
Order Details | PK_Order_Details | PRIMARY | False | False
Order Details | ProductID | INDEX | True | False
Order Details | ProductsOrder_Details | INDEX | True | False
Orders | CustomerID | INDEX | True | False
Orders | CustomersOrders | INDEX | True | False
Orders | EmployeeID | INDEX | True | False
Orders | EmployeesOrders | INDEX | True | False
Orders | FK_Orders_Customers | INDEX | True | False
Orders | FK_Orders_Employees | INDEX | True | False
Orders | FK_Orders_Shippers | INDEX | True | False
Orders | OrderDate | INDEX | True | False
Orders | PK_Orders | PRIMARY | False | False
Orders | ShippedDate | INDEX | True | False
Orders | ShippedDateDescending | INDEX | True | False
Orders | ShippersOrders | INDEX | True | False
Orders | ShipPostalCode | INDEX | True | False");
        }

        [DataTestMethod]
        [DataRow(DataAccessProviderType.Odbc)]
        [DataRow(DataAccessProviderType.OleDb)]
        public void IndexColumns(DataAccessProviderType providerType)
        {
            using var connection = Helpers.OpenDatabase(StoreName, providerType);
            var result = Helpers.GetDataReaderContent(
                connection,
                @"SELECT * FROM `INFORMATION_SCHEMA.INDEX_COLUMNS`
WHERE TABLE_NAME = 'Orders' OR 
      TABLE_NAME = 'Order Details'
ORDER BY TABLE_NAME,
         INDEX_NAME,
         ORDINAL_POSITION");
            
            AssertDataReaderContent(result, @"
`TABLE_NAME` | `INDEX_NAME` | `ORDINAL_POSITION` | `COLUMN_NAME` | `IS_DESCENDING`
--- | --- | --- | --- | ---
Order Details | FK_Order_Details_Orders | 0 | OrderID | False
Order Details | FK_Order_Details_Products | 0 | ProductID | False
Order Details | OrderID | 0 | OrderID | False
Order Details | OrdersOrder_Details | 0 | OrderID | False
Order Details | PK_Order_Details | 0 | OrderID | False
Order Details | PK_Order_Details | 1 | ProductID | False
Order Details | ProductID | 0 | ProductID | False
Order Details | ProductsOrder_Details | 0 | ProductID | False
Orders | CustomerID | 0 | CustomerID | False
Orders | CustomersOrders | 0 | CustomerID | False
Orders | EmployeeID | 0 | EmployeeID | False
Orders | EmployeesOrders | 0 | EmployeeID | False
Orders | FK_Orders_Customers | 0 | CustomerID | False
Orders | FK_Orders_Employees | 0 | EmployeeID | False
Orders | FK_Orders_Shippers | 0 | ShipVia | False
Orders | OrderDate | 0 | OrderDate | False
Orders | PK_Orders | 0 | OrderID | False
Orders | ShippedDate | 0 | ShippedDate | False
Orders | ShippedDateDescending | 0 | ShippedDate | True
Orders | ShippersOrders | 0 | ShipVia | False
Orders | ShipPostalCode | 0 | ShipPostalCode | False");
        }
        
        [DataTestMethod]
        [DataRow(DataAccessProviderType.Odbc)]
        [DataRow(DataAccessProviderType.OleDb)]
        public void Relations(DataAccessProviderType providerType)
        {
            using var connection = Helpers.OpenDatabase(StoreName, providerType);
            var result = Helpers.GetDataReaderContent(
                connection,
                @"SELECT * FROM `INFORMATION_SCHEMA.RELATIONS`
WHERE PRINCIPAL_TABLE_NAME = 'Orders' AND 
      REFERENCING_TABLE_NAME = 'Order Details'
ORDER BY RELATION_NAME");
            
            AssertDataReaderContent(result, @"
`RELATION_NAME` | `REFERENCING_TABLE_NAME` | `PRINCIPAL_TABLE_NAME` | `RELATION_TYPE` | `ON_DELETE` | `ON_UPDATE` | `IS_ENFORCED` | `IS_INHERITED`
--- | --- | --- | --- | --- | --- | --- | ---
FK_Order_Details_Orders | Order Details | Orders | MANY | NO ACTION | NO ACTION | True | True");
        }

        [DataTestMethod]
        [DataRow(DataAccessProviderType.Odbc)]
        [DataRow(DataAccessProviderType.OleDb)]
        public void RelationColumns(DataAccessProviderType providerType)
        {
            using var connection = Helpers.OpenDatabase(StoreName, providerType);
            var result = Helpers.GetDataReaderContent(
                connection,
                @"SELECT * FROM `INFORMATION_SCHEMA.RELATION_COLUMNS`
WHERE RELATION_NAME = 'FK_Order_Details_Orders'
ORDER BY RELATION_NAME, REFERENCING_COLUMN_NAME, PRINCIPAL_COLUMN_NAME");
            
            AssertDataReaderContent(result, @"
`RELATION_NAME` | `REFERENCING_COLUMN_NAME` | `PRINCIPAL_COLUMN_NAME`
--- | --- | ---
FK_Order_Details_Orders | OrderID | OrderID");
        }

        [DataTestMethod]
        [DataRow(DataAccessProviderType.Odbc)]
        [DataRow(DataAccessProviderType.OleDb)]
        public void CheckConstraints(DataAccessProviderType providerType)
        {
            using var connection = Helpers.OpenDatabase(StoreName, providerType);
            var result = Helpers.GetDataReaderContent(
                connection,
                @"SELECT * FROM `INFORMATION_SCHEMA.CHECK_CONSTRAINTS`
ORDER BY TABLE_NAME, CONSTRAINT_NAME");
            
            AssertDataReaderContent(result, @"
`TABLE_NAME` | `CONSTRAINT_NAME` | `CHECK_CLAUSE`
--- | --- | ---
#Dual | SingleRecord | (SELECT COUNT(*) FROM [#Dual]) = 1
Employees | CK_BirthDate | [BirthDate] < NOW()
Order Details | CK_Discount | [Discount] >= 0 and ([Discount] <= 1)
Order Details | CK_Quantity | [Quantity] > 0
Order Details | CK_UnitPrice | [UnitPrice] >= 0
Products | CK_Products_UnitPrice | [UnitPrice] >= 0
Products | CK_ReorderLevel | [ReorderLevel] >= 0
Products | CK_UnitsInStock | [UnitsInStock] >= 0
Products | CK_UnitsOnOrder | [UnitsOnOrder] >= 0");
        }
    }
}