// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetTimeSpanTypeMapping : JetDateTimeTypeMapping
    {
        [NotNull] private readonly IJetOptions _options;

        public JetTimeSpanTypeMapping(
                [NotNull] string storeType,
                [NotNull] IJetOptions options)
            : base(storeType, options, System.Data.DbType.Time, typeof(TimeSpan))
        {
            _options = options;
        }

        protected JetTimeSpanTypeMapping(RelationalTypeMappingParameters parameters, IJetOptions options)
            : base(parameters, options)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetTimeSpanTypeMapping(parameters, _options);
        
        protected override DateTime ConvertToDateTimeCompatibleValue(object value)
            => JetConfiguration.TimeSpanOffset + (TimeSpan) value;
    }
}