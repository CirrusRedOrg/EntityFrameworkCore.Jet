// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Infrastructure.Internal;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetTimeOnlyTypeMapping : TimeOnlyTypeMapping
    {
        public static new JetTimeOnlyTypeMapping Default { get; } = new JetTimeOnlyTypeMapping("time");
        public JetTimeOnlyTypeMapping(
                string storeType)
            : base(storeType)
        {
        }

        protected JetTimeOnlyTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
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
            => new JetTimeOnlyTypeMapping(parameters);

        protected override string GenerateNonNullSqlLiteral(object value)
        {
            return FormattableString.Invariant($@"TIMEVALUE('{value:HH\:mm\:ss}')");
        }

        protected override string ProcessStoreType(RelationalTypeMappingParameters parameters, string storeType, string storeTypeNameBase)
        {
            return base.ProcessStoreType(parameters, storeTypeNameBase, storeTypeNameBase);
        }
    }
}