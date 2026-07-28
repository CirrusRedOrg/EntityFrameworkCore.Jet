# Generates Ace16Types.accdb: an ACE 16 (version-byte 0x06) database exercising the
# Office 2016 types BIGINT (Large Number) and DATETIME2. Requires the ACE 16 OLE DB
# provider (Microsoft.ACE.OLEDB.16.0), which creates the newer format by default.
$path = "$PSScriptRoot\Ace16Types.accdb"
if (Test-Path $path) { Remove-Item $path -Force }
$cat = New-Object -ComObject ADOX.Catalog
$cat.Create("Provider=Microsoft.ACE.OLEDB.16.0;Data Source=$path") | Out-Null
$cn = $cat.ActiveConnection
$cn.Execute("CREATE TABLE T (Id LONG, Big BIGINT, Dt DATETIME2)") | Out-Null
$cn.Execute("INSERT INTO T (Id,Big,Dt) VALUES (1, 9223372036854775807, #2020-06-15 13:45:30#)") | Out-Null
$cn.Execute("INSERT INTO T (Id,Big,Dt) VALUES (2, -42, #1899-12-30 00:00:00#)") | Out-Null
$cn.Execute("INSERT INTO T (Id,Big,Dt) VALUES (3, 0, #2000-01-01 12:00:00#)") | Out-Null
# fractional-second probe (DATETIME2's reason for existing)
try { $cn.Execute("INSERT INTO T (Id,Big,Dt) VALUES (4, 1234567890123456789, '2021-03-04 09:08:07.1234567')") | Out-Null; Write-Output "frac insert ok" }
catch { Write-Output "frac insert failed: $($_.Exception.Message.Split([char]10)[0])" }
$cn.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($cat)
[GC]::Collect(); [GC]::WaitForPendingFinalizers()
Write-Output "size=$((Get-Item $path).Length)"
