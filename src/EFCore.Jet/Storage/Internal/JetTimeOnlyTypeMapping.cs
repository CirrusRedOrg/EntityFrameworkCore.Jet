// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Infrastructure.Internal;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetTimeOnlyTypeMapping : TimeOnlyTypeMapping
    {
        private readonly IJetOptions _options;

        public JetTimeOnlyTypeMapping(
                string storeType,
                IJetOptions options)
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