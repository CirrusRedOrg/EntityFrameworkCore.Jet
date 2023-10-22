// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetTimeSpanTypeMapping : TimeSpanTypeMapping
    {
        [NotNull] private readonly IJetOptions _options;

        public JetTimeSpanTypeMapping(
                [NotNull] string storeType,
                [NotNull] IJetOptions options)
            : base(storeType)
        {
            _options = options;
        }

        protected JetTimeSpanTypeMapping(RelationalTypeMappingParameters parameters, IJetOptions options)
            : base(parameters)
        {
            _options = options;
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetTimeSpanTypeMapping(parameters, _options);

        /*protected override DateTime ConvertToDateTimeCompatibleValue(object value)
            => JetConfiguration.TimeSpanOffset + (TimeSpan)value;*/

        protected override string ProcessStoreType(RelationalTypeMappingParameters parameters, string storeType, string storeTypeNameBase)
        {
            return base.ProcessStoreType(parameters, storeTypeNameBase, storeTypeNameBase);
        }

        protected override string SqlLiteralFormatString
            => "TIMEVALUE('{0:hh\\:mm\\:ss}')";
    }
}