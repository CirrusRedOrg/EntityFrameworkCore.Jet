using System.Data;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetOdbcGuidTypeMapping : JetGuidTypeMapping
    {
        public JetOdbcGuidTypeMapping(string storeType) : base(storeType, System.Data.DbType.Guid)
        {
        }

        protected JetOdbcGuidTypeMapping(RelationalTypeMappingParameters parameters) : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetOdbcGuidTypeMapping(parameters);

        /// <summary>
        ///     Gets the string format to be used to generate SQL literals of this type.
        /// </summary>
        protected override string SqlLiteralFormatString
            => "'{{{0}}}'";
    }
}