namespace EntityFrameworkCore.LibRed.Storage.Internal;

public class LibRedIntTypeMapping : IntTypeMapping
{
    public static new LibRedIntTypeMapping Default { get; } = new LibRedIntTypeMapping("integer");

    public LibRedIntTypeMapping(string storeType)
        : base(storeType, System.Data.DbType.Int32)
    {
    }

    protected LibRedIntTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    // LibRedIntTypeMapping is also used for an explicit counter type, because we actually want it to be integer unless
    // the value generation type is also OnAdd.
    // We therefore lock the store type to its original value (which should be "integer").
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new LibRedIntTypeMapping(parameters.WithStoreTypeAndSize(Parameters.StoreType, parameters.Size));
}
