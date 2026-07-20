using LibRed.Data;
using Xunit;

namespace LibRed.Ado.Tests;

public class LibRedConnectionTests
{
    [Fact]
    public void Quoted_semicolon_in_data_source_is_not_treated_as_a_separator()
    {
        string path = Path.Combine(Path.GetTempPath(), "libred;quoted.accdb");

        using var connection = new LibRedConnection($"Data Source=\"{path}\";Mode=Read");

        Assert.Equal(Path.GetFullPath(path), connection.DataSource);
    }

    [Fact]
    public void Canonical_data_source_wins_when_aliases_are_mixed()
    {
        string canonical = Path.Combine(Path.GetTempPath(), "canonical.accdb");
        string alias = Path.Combine(Path.GetTempPath(), "alias.accdb");

        using var connection = new LibRedConnection($"DBQ={alias};Data Source={canonical}");

        Assert.Equal(Path.GetFullPath(canonical), connection.DataSource);
    }
}
