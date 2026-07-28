namespace LibRed.Storage;

/// <summary>A pointer to a row: the data page it lives on and its slot index within that page.</summary>
public readonly record struct RowId(int Page, int Row);
