// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Runtime.CompilerServices;
using EntityFrameworkCore.Jet.Metadata;
using EntityFrameworkCore.Jet.Metadata.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore
{
    /// <summary>
    ///     Jet specific extension methods for <see cref="ModelBuilder" />.
    /// </summary>
    public static class JetReferenceCollectionExtensions
    {
        public static ReferenceCollectionBuilder<TPrincipalEntity, TDependentEntity> MatchSimple<TPrincipalEntity, TDependentEntity>(this ReferenceCollectionBuilder<TPrincipalEntity, TDependentEntity> referenceCollectionBuilder)
            where TPrincipalEntity : class
            where TDependentEntity : class
        {
            referenceCollectionBuilder.Metadata.SetAnnotation(JetAnnotationNames.Prefix + "MatchSimple", "MatchSimple");
            return referenceCollectionBuilder;
        }
    }
}