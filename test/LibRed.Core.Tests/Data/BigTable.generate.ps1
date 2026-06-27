# Generates BigTable.accdb: a table large enough (~115k rows, ~38k pages, ~150 MB)
# that its owned-pages usage map is a REFERENCE map (type 0x01) pointing at dedicated
# bitmap pages, rather than an inline bitmap. Northwind only exercises inline maps, and
# a file big enough for a reference map is too large to commit — so this regenerates it
# locally for verifying UsageMap's reference-map path. Requires the ACE OLE DB provider.
#
# The generated file is gitignored. Run, then point a local check at it.
$path = "$PSScriptRoot\BigTable.accdb"
if (Test-Path $path) { Remove-Item $path -Force }
$conn = "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=$path"
$cat = New-Object -ComObject ADOX.Catalog
$cat.Create($conn) | Out-Null
$cn = $cat.ActiveConnection
$cn.Execute("CREATE TABLE Seed (n LONG)") | Out-Null
for ($i = 0; $i -lt 340; $i++) { $cn.Execute("INSERT INTO Seed (n) VALUES ($i)") | Out-Null }
$cn.Execute("CREATE TABLE Big (Id COUNTER PRIMARY KEY, F1 TEXT(255), F2 TEXT(255), F3 TEXT(255))") | Out-Null
$f = ('a' * 210)
$cn.Execute("INSERT INTO Big (F1,F2,F3) SELECT '$f','$f','$f' FROM Seed a, Seed b") | Out-Null
$rows = $cn.Execute("SELECT COUNT(*) AS c FROM Big").Fields.Item("c").Value
$cn.Close()
Write-Output "rows=$rows size=$((Get-Item $path).Length)"
