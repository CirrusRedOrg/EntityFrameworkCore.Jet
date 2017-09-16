// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.Storage.Internal;
using EntityFrameworkCore.Jet.Update.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace EntityFrameworkCore.Jet.ValueGeneration.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetSequenceHiLoValueGenerator<TValue> : HiLoValueGenerator<TValue>
    {
        private readonly IRawSqlCommandBuilder _rawSqlCommandBuilder;
        private readonly IJetUpdateSqlGenerator _sqlGenerator;
        private readonly IJetRelationalConnection _relationalConnection;
        private readonly ISequence _sequence;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetSequenceHiLoValueGenerator(
            [NotNull] IRawSqlCommandBuilder rawSqlCommandBuilder,
            [NotNull] IJetUpdateSqlGenerator sqlGenerator,
            [NotNull] JetSequenceValueGeneratorState generatorState,
            [NotNull] IJetRelationalConnection relationalConnection)
            : base(generatorState)
        {
            Check.NotNull(rawSqlCommandBuilder, nameof(rawSqlCommandBuilder));
            Check.NotNull(sqlGenerator, nameof(sqlGenerator));
            Check.NotNull(relationalConnection, nameof(relationalConnection));

            _sequence = generatorState.Sequence;
            _rawSqlCommandBuilder = rawSqlCommandBuilder;
            _sqlGenerator = sqlGenerator;
            _relationalConnection = relationalConnection;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override long GetNewLowValue()
            => (long)Convert.ChangeType(
                _rawSqlCommandBuilder
                    .Build(_sqlGenerator.GenerateNextSequenceValueOperation(_sequence.Name, _sequence.Schema))
                    .ExecuteScalar(_relationalConnection),
                typeof(long),
                CultureInfo.InvariantCulture);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override bool GeneratesTemporaryValues => false;
    }
}
