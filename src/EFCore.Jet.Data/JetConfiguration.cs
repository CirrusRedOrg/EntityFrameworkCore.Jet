using System;

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
        public static DateTime TimeSpanOffset { get; set; } = new(1899, 12, 30);

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
                    throw new ArgumentOutOfRangeException(nameof(value), "IntegerNullValue should be an int or null");
                _integerNullValue = value;
            }
        }
        
        public static DataAccessProviderType DefaultDataAccessProviderType { get; set; } = DataAccessProviderType.Odbc; 
        
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