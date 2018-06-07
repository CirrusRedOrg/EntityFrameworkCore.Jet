// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFrameworkCore.Jet.Infrastructure
{
#pragma warning disable 1574
    /// <summary>
    ///     <para>
    ///         Allows Jet specific configuration to be performed on <see cref="DbContextOptions" />.
    ///     </para>
    ///     <para>
    ///         Instances of this class are returned from a call to
    ///         <see
    ///             cref="JetDbContextOptionsExtensions.UseJet" />
    ///         and it is not designed to be directly constructed in your application code.
    ///     </para>
    /// </summary>
#pragma warning restore 1574
    public class JetDbContextOptionsBuilder
        : RelationalDbContextOptionsBuilder<JetDbContextOptionsBuilder, JetOptionsExtension>
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="JetDbContextOptionsBuilder" /> class.
        /// </summary>
        /// <param name="optionsBuilder"> The options builder. </param>
        public JetDbContextOptionsBuilder([NotNull] DbContextOptionsBuilder optionsBuilder)
            : base(optionsBuilder)
        {
        }
    }
}
