using System.Collections;
using System.Data.Common;

namespace LibRed.Data;

/// <summary>Parameter collection backed by a simple list.</summary>
public sealed class LibRedParameterCollection : DbParameterCollection
{
    private readonly List<LibRedParameter> _items = [];

    public override int Count => _items.Count;
    public override object SyncRoot { get; } = new();

    public override int Add(object value)
    {
        _items.Add((LibRedParameter)value);
        return _items.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (object value in values) Add(value);
    }

    public override void Clear() => _items.Clear();

    public override bool Contains(object value) => _items.Contains((LibRedParameter)value);
    public override bool Contains(string value) => IndexOf(value) >= 0;

    public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    public override IEnumerator GetEnumerator() => _items.GetEnumerator();

    public override int IndexOf(object value) => _items.IndexOf((LibRedParameter)value);

    public override int IndexOf(string parameterName) =>
        _items.FindIndex(p => string.Equals(p.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase));

    public override void Insert(int index, object value) => _items.Insert(index, (LibRedParameter)value);

    public override void Remove(object value) => _items.Remove((LibRedParameter)value);

    public override void RemoveAt(int index) => _items.RemoveAt(index);

    public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));

    protected override DbParameter GetParameter(int index) => _items[index];

    protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];

    protected override void SetParameter(int index, DbParameter value) => _items[index] = (LibRedParameter)value;

    protected override void SetParameter(string parameterName, DbParameter value) =>
        _items[IndexOf(parameterName)] = (LibRedParameter)value;
}
