// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace EntityFrameworkCore.Jet.ValueGeneration.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetSequenceValueGeneratorState : HiLoValueGeneratorState
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetSequenceValueGeneratorState([NotNull] ISequence sequence)
            : base(Check.NotNull(sequence, nameof(sequence)).IncrementBy)
        {
            Sequence = sequence;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual ISequence Sequence { get; }
    }
}
