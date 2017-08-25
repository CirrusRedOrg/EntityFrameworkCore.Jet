using System;

// ReSharper disable InconsistentNaming

namespace System.Data.Jet
{
    /// <summary>
    /// Jet configuration
    /// </summary>
    public static class JetConfiguration
    {

        /// <summary>
        /// The time span offset (Jet does not support timespans)
        /// </summary>
        public static DateTime TimeSpanOffset = new DateTime(1899, 12, 30);


        private static object _integerNullValue = Int32.MinValue;

        /// <summary>
        /// Gets or sets the integer null value returned by queries. This should solve a Jet issue
        /// that if I do a UNION ALL of null, int and null the Jet raises an error
        /// </summary>
        /// <value>
        /// The integer null value.
        /// </value>
        public static object IntegerNullValue
        {
            get { return _integerNullValue; }
            set
            {
                if (!(value is int) && value != null)
                    throw new ArgumentOutOfRangeException("value", "IntegerNullValue should be an int or null");
                _integerNullValue = value;
            }
        }

        public static string OleDbDefaultProvider => "Microsoft.ACE.OLEDB.12.0";


        // The SQL statement
        //
        // (SELECT COUNT(*) FROM MSysRelationships)
        //
        // is a DUAL table simulation in Access databases
        // It must be a single line table.
        // If user cannot gain access to MSysRelationships table he can create a table with 1 record
        // and change DUAL static property.
        // I.e. create table dual with one and only one record
        //
        // CREATE TABLE Dual (id COUNTER CONSTRAINT pkey PRIMARY KEY)
        // INSERT INTO Dual (id) VALUES (1)
        // ALTER TABLE Dual ADD CONSTRAINT DualTableConstraint CHECK ((SELECT Count(*) FROM Dual) = 1)
        //
        // then change the DUAL property
        //
        // JetConnection.DUAL = "Dual";
        //
        // For more information see also https://en.wikipedia.org/wiki/DUAL_table
        /// <summary>
        /// The DUAL table or query
        /// </summary>
        public static string DUAL = DUALForAccdb;

        /// <summary>
        /// The dual table for accdb
        /// </summary>
        public const string DUALForMdb = "(SELECT COUNT(*) FROM MSysRelationships)";

        /// <summary>
        /// The dual table for accdb
        /// </summary>
        public const string DUALForAccdb = "(SELECT COUNT(*) FROM MSysAccessStorage)";

        /// <summary>
        /// Gets or sets a value indicating whether append random number for foreign key names.
        /// </summary>
        /// <value>
        /// <c>true</c> if append random number for foreign key names; otherwise, <c>false</c>.
        /// </value>
        public static bool AppendRandomNumberForForeignKeyNames = true;

        /// <summary>
        /// Gets or sets a value indicating whether show SQL statements.
        /// </summary>
        /// <value>
        ///   <c>true</c> to show SQL statements; otherwise, <c>false</c>.
        /// </value>
        public static bool ShowSqlStatements = false;

    }
}
