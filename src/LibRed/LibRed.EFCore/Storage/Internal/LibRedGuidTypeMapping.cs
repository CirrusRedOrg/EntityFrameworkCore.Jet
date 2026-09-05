using System.Data;

namespace EntityFrameworkCore.LibRed.Storage.Internal
{
    public class LibRedGuidTypeMapping : GuidTypeMapping
    {
        public static new LibRedGuidTypeMapping Default { get; } = new LibRedGuidTypeMapping("uniqueidentifier");

        public LibRedGuidTypeMapping(string storeType, DbType? dbType = System.Data.DbType.Guid) : base(storeType, dbType)
        {
        }

        protected LibRedGuidTypeMapping(RelationalTypeMappingParameters parameters) : base(parameters)
        {
        }

        /// <summary>
        ///     Creates a copy of this mapping.
        /// </summary>
        /// <param name="parameters">The parameters for this mapping.</param>
        /// <returns>The newly created mapping.</returns>
        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new LibRedGuidTypeMapping(parameters);

        /// <summary>
        ///     Gets the string format to be used to generate SQL literals of this type.
        /// </summary>
        protected override string SqlLiteralFormatString
            => "{{{0}}}";
    }
}
