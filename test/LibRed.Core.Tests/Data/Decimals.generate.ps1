# Generates Decimals.accdb: a table with Decimal/Numeric columns and known values that
# exercise the 17-byte numeric encoding (small, mid-word, and hi-word magnitudes; signs).
# Requires the ACE OLE DB provider. Committed as a test asset (small file).
$path = "$PSScriptRoot\Decimals.accdb"
if (Test-Path $path) { Remove-Item $path -Force }
$cat = New-Object -ComObject ADOX.Catalog
$cat.Create("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=$path") | Out-Null
$cn = $cat.ActiveConnection
$cn.Execute("CREATE TABLE Nums (Id COUNTER PRIMARY KEY, Price DECIMAL(12,3), Big DECIMAL(28,4))") | Out-Null
$cn.Execute("INSERT INTO Nums (Price, Big) VALUES (12.345, 123456789012.3456)") | Out-Null
$cn.Execute("INSERT INTO Nums (Price, Big) VALUES (-9.999, -9876543210987654.3210)") | Out-Null
$cn.Execute("INSERT INTO Nums (Price, Big) VALUES (0, 0)") | Out-Null
$cn.Close()
Write-Output "size=$((Get-Item $path).Length)"
