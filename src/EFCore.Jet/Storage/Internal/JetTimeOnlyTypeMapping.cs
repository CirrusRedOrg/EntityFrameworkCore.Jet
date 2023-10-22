// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data;
using System.Data.Common;
using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetTimeOnlyTypeMapping : TimeOnlyTypeMapping
    {
        [NotNull] private readonly IJetOptions _options;

        public JetTimeOnlyTypeMapping(
                [NotNull] string storeType,
                [NotNull] IJetOptions options)
            : base(storeType)
        {
            _options = options;
        }

        protected JetTimeOnlyTypeMapping(RelationalTypeMappingParameters parameters, IJetOptions options)
            : base(parameters)
        {
            _options = options;
        }

        protected override void ConfigureParameter(DbParameter parameter)
        {
            base.ConfigureParameter(parameter);
            if (parameter.Value is TimeOnly timeOnly)
            {
                timeOnly.Deconstruct(out int hour, out int min, out int sec);
                //parameter.Value = JetConfiguration.TimeSpanOffset.Add(new TimeSpan(hour, min, sec));
                parameter.Value = new TimeSpan(hour, min, sec);
            }
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetTimeOnlyTypeMapping(parameters, _options);

        protected override string GenerateNonNullSqlLiteral(object value)
        {
            //Format time without any milliseconds
            if (!_options.EnableMillisecondsSupport)
            {
                return FormattableString.Invariant($@"TIMEVALUE('{value:HH\:mm\:ss}')");
            }
            else
            {
                //TODO: Treat as double
                return "";
            }
        }

        protected override string ProcessStoreType(RelationalTypeMappingParameters parameters, string storeType, string storeTypeNameBase)
        {
            return base.ProcessStoreType(parameters, storeTypeNameBase, storeTypeNameBase);
        }
    }
}