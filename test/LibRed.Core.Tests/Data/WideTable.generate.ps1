$path = "$PSScriptRoot\WideTable.accdb"
if (Test-Path $path) { Remove-Item $path -Force }
$conn = "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=$path"
$cat = New-Object -ComObject ADOX.Catalog
$cat.Create($conn) | Out-Null
$cn = $cat.ActiveConnection
$cols = (0..199 | ForEach-Object { "C{0:D3} LONG" -f $_ }) -join ", "
$cn.Execute("CREATE TABLE WideTable ($cols)") | Out-Null
# insert one row: C000=1000, C199=1199, rest null
$cn.Execute("INSERT INTO WideTable (C000, C100, C199) VALUES (1000, 1100, 1199)") | Out-Null
$cn.Close()
Write-Output "created $path ($((Get-Item $path).Length) bytes)"
