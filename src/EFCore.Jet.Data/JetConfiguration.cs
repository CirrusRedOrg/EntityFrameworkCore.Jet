﻿using System;

namespace EntityFrameworkCore.Jet.Data
{
    /// <summary>
    /// Jet configuration
    /// </summary>
    public static class JetConfiguration
    {
        /// <summary>
        /// The time span offset (Jet does not support timespans)
        /// </summary>
        public static DateTime TimeSpanOffset { get; set; } = new DateTime(1899, 12, 30);

        private static object _integerNullValue = Int32.MinValue;

        // CHECK: Replace with Nullable<Int32>
        /// <summary>
        /// Gets or sets the integer null value returned by queries. This should solve a Jet issue
        /// that if I do a UNION ALL of null, int and null the Jet raises an error
        /// </summary>
        /// <value>
        /// The integer null value.
        /// </value>
        public static object IntegerNullValue
        {
            get => _integerNullValue;
            set
            {
                if (!(value is int) && value != null)
                    throw new ArgumentOutOfRangeException("value", "IntegerNullValue should be an int or null");
                _integerNullValue = value;
            }
        }
        
        public static DataAccessProviderType DefaultDataAccessProviderType { get; set; } = DataAccessProviderType.Odbc; 
        
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
        // JetConfiguration.DUAL = "Dual";
        //
        // For more information see also https://en.wikipedia.org/wiki/DUAL_table
        /// <summary>
        /// The DUAL table or query
        /// </summary>
        public static string DUAL { get; set; } = DUALForAccdb;

        /// <summary>
        /// The dual table for accdb
        /// </summary>
        public const string DUALForMdb = "(SELECT COUNT(*) FROM MSysRelationships)";

        /// <summary>
        /// The dual table for accdb
        /// </summary>
        public const string DUALForAccdb = "(SELECT COUNT(*) FROM MSysAccessStorage)";

        /// <summary>
        /// Gets or sets a value indicating whether show SQL statements.
        /// </summary>
        /// <value>
        ///   <c>true</c> to show SQL statements; otherwise, <c>false</c>.
        /// </value>
        public static bool ShowSqlStatements { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether the connection pooling should be used
        /// </summary>
        /// <value>
        /// <c>true</c> to use the connection pooling; otherwise, <c>false</c>.
        /// </value>
        public static bool UseConnectionPooling { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether to return a default value for the column
        /// if the column is not meant to be null and somehow the value stored is actually null
        /// </summary>
        /// <value>
        /// <c>true</c> to return a default value; otherwise, <c>false</c>.
        /// </value>
        public static bool UseDefaultValueOnDBNullConversionError { get; set; } = false;
    }
}