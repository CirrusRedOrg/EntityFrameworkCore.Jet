// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Infrastructure.Internal;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetTimeSpanTypeMapping : TimeSpanTypeMapping
    {
        public static new JetTimeSpanTypeMapping Default { get; } = new JetTimeSpanTypeMapping("time");

        public JetTimeSpanTypeMapping(
                string storeType)
            : base(storeType)
        {
        }

        protected JetTimeSpanTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetTimeSpanTypeMapping(parameters);

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