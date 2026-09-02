// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace EntityFrameworkCore.Jet.Infrastructure
{
    /// <summary>
    /// The DUAL table used by the shared Jet/ACE SQL generation, and by any provider running in a
    /// Jet-compatible mode. Held here rather than on <c>JetConfiguration</c> so the generated SQL and the
    /// scaffolding factory that detects the table can both reach it without the EF layer depending on a
    /// particular ADO.NET driver.
    /// </summary>
    public static class JetDualTable
    {
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
        // JetDualTable.CustomName = "Dual";
        //
        // For more information see also https://en.wikipedia.org/wiki/DUAL_table
        /// <summary>
        /// The DUAL table or query
        /// </summary>
        public static string CustomName = "";
        //MSysRelationships
        //MSysAccessStorage
        //#Dual
        //(SELECT COUNT(*) FROM MSysAccessStorage)

        public static string DetectedName = "#Dual";

        /// <summary>
        /// The name to generate into SQL: the user's <see cref="CustomName"/> when one has been set,
        /// otherwise the <see cref="DetectedName"/> the scaffolding factory last found.
        /// </summary>
        public static string Name
            => string.IsNullOrEmpty(CustomName)
                ? DetectedName
                : CustomName;
    }
}
