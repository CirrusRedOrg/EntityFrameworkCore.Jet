// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using EntityFrameworkCore.Jet.Storage.Internal;
using EntityFrameworkCore.Jet.Update.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace EntityFrameworkCore.Jet.ValueGeneration.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetSequenceValueGeneratorFactory : IJetSequenceValueGeneratorFactory
    {
        private readonly IRawSqlCommandBuilder _rawSqlCommandBuilder;
        private readonly IJetUpdateSqlGenerator _sqlGenerator;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetSequenceValueGeneratorFactory(
            [NotNull] IRawSqlCommandBuilder rawSqlCommandBuilder,
            [NotNull] IJetUpdateSqlGenerator sqlGenerator)
        {
            Check.NotNull(rawSqlCommandBuilder, nameof(rawSqlCommandBuilder));
            Check.NotNull(sqlGenerator, nameof(sqlGenerator));

            _rawSqlCommandBuilder = rawSqlCommandBuilder;
            _sqlGenerator = sqlGenerator;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual ValueGenerator Create(IProperty property, JetSequenceValueGeneratorState generatorState, IJetRelationalConnection relationalConnection)
        {
            Check.NotNull(property, nameof(property));
            Check.NotNull(generatorState, nameof(generatorState));
            Check.NotNull(relationalConnection, nameof(relationalConnection));

            var type = property.ClrType.UnwrapNullableType().UnwrapEnumType();

            if (type == typeof(long))
            {
                return new JetSequenceHiLoValueGenerator<long>(_rawSqlCommandBuilder, _sqlGenerator, generatorState, relationalConnection);
            }

            if (type == typeof(int))
            {
                return new JetSequenceHiLoValueGenerator<int>(_rawSqlCommandBuilder, _sqlGenerator, generatorState, relationalConnection);
            }

            if (type == typeof(short))
            {
                return new JetSequenceHiLoValueGenerator<short>(_rawSqlCommandBuilder, _sqlGenerator, generatorState, relationalConnection);
            }

            if (type == typeof(byte))
            {
                return new JetSequenceHiLoValueGenerator<byte>(_rawSqlCommandBuilder, _sqlGenerator, generatorState, relationalConnection);
            }

            if (type == typeof(char))
            {
                return new JetSequenceHiLoValueGenerator<char>(_rawSqlCommandBuilder, _sqlGenerator, generatorState, relationalConnection);
            }

            if (type == typeof(ulong))
            {
                return new JetSequenceHiLoValueGenerator<ulong>(_rawSqlCommandBuilder, _sqlGenerator, generatorState, relationalConnection);
            }

            if (type == typeof(uint))
            {
                return new JetSequenceHiLoValueGenerator<uint>(_rawSqlCommandBuilder, _sqlGenerator, generatorState, relationalConnection);
            }

            if (type == typeof(ushort))
            {
                return new JetSequenceHiLoValueGenerator<ushort>(_rawSqlCommandBuilder, _sqlGenerator, generatorState, relationalConnection);
            }

            if (type == typeof(sbyte))
            {
                return new JetSequenceHiLoValueGenerator<sbyte>(_rawSqlCommandBuilder, _sqlGenerator, generatorState, relationalConnection);
            }

            throw new ArgumentException(
                CoreStrings.InvalidValueGeneratorFactoryProperty(
                    nameof(JetSequenceValueGeneratorFactory), property.Name, property.DeclaringEntityType.DisplayName()));
        }
    }
}
