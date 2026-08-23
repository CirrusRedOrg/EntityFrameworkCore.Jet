<#
.SYNOPSIS
    Builds LibRed.Core's embedded sorting-weight table for the "General" (v1) text collation.

.DESCRIPTION
    ACE's General sort order is the Windows Server 2008 (Vista SP1-era) NLS sorting weight table, frozen —
    identified by reconstructing measured ACE v1 index keys from every published Windows table and scoring
    25/25 against Server 2008 alone (Win7/2008R2 24/25, Vista 23/25, Win8+ 22/25, NT4-2003 18/25).

    The source is Microsoft's published "Windows Server 2008 Sorting Weight Table", linked from
    [MS-UCODEREF]: Windows Protocols Unicode Reference. Its Open Specifications notice permits copying to
    develop implementations and distributing portions within them, and extends that permission to referenced
    documents.

    Input columns are `codepoint  SM  AW  DW  CW`:
      SM  Script Member    ) together the 2-byte primary weight
      AW  Alphabetic Weight)
      DW  Diacritic Weight  - the secondary weight ACE keeps
      CW  Case Weight       - the tertiary weight ACE truncates, which is why case and width fold

    CW is therefore dropped here. Only the DEFAULT SORTKEY block (to ENDSORTKEY) and the EXPANSION section
    are read: the per-locale COMPRESSION tables that follow redefine the same code points and will silently
    corrupt a naive parse.

    Output is five Deflate-compressed streams (codepoint deltas, SM, AW, DW, expansions). Splitting them
    matters: interleaved records compress to ~194 KB, separate homogeneous streams to ~16 KB.

.PARAMETER SourceTable
    Path to "Windows Server 2008 Sorting Weight Table.txt".

.PARAMETER OutputPath
    Where to write the binary resource (default: the LibRed.Core Resources folder).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $SourceTable,
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\..\src\LibRed\LibRed.Core\Resources\SortKeyTableV1.bin')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $SourceTable)) { throw "Source table not found: $SourceTable" }

$weights = [System.Collections.Generic.SortedDictionary[int, int[]]]::new()
$expansions = [System.Collections.Generic.SortedDictionary[int, int[]]]::new()
$section = $null

foreach ($line in [System.IO.File]::ReadLines($SourceTable)) {
    $trimmed = $line.Trim()
    if ($trimmed.StartsWith('SORTKEY')) { $section = 'weights'; continue }
    if ($trimmed.StartsWith('ENDSORTKEY')) { $section = $null; continue }
    if ($trimmed.StartsWith('EXPANSION')) { $section = 'expansions'; continue }
    if ($trimmed -match '^(COMPRESSION|MULTIPLEWEIGHTS|REVERSEDIACRITICS|SORTTABLES|ENDSORTTABLES)') { $section = $null; continue }
    if (-not $line.StartsWith('0x')) { continue }

    $fields = ($line -split ';')[0].Trim() -split '\s+'
    if ($section -eq 'weights' -and $fields.Count -eq 5) {
        # codepoint SM AW DW CW - CW dropped.
        $weights[[Convert]::ToInt32($fields[0], 16)] = @([int]$fields[1], [int]$fields[2], [int]$fields[3])
    }
    elseif ($section -eq 'expansions' -and $fields.Count -ge 2 -and ($fields | ForEach-Object { $_.StartsWith('0x') }) -notcontains $false) {
        $expansions[[Convert]::ToInt32($fields[0], 16)] =
            @($fields[1..($fields.Count - 1)] | ForEach-Object { [Convert]::ToInt32($_, 16) })
    }
}

Write-Host "parsed $($weights.Count) weights, $($expansions.Count) expansions"
if ($weights.Count -lt 50000) { throw "Only $($weights.Count) weights parsed - the DEFAULT block looks truncated." }

function Write-VarInt([System.Collections.Generic.List[byte]] $target, [int] $value) {
    while ($value -ge 0x80) { $target.Add([byte](($value -band 0x7F) -bor 0x80)); $value = $value -shr 7 }
    $target.Add([byte]$value)
}

$deltas = [System.Collections.Generic.List[byte]]::new()
$sm = [System.Collections.Generic.List[byte]]::new()
$aw = [System.Collections.Generic.List[byte]]::new()
$dw = [System.Collections.Generic.List[byte]]::new()
$previous = 0
foreach ($cp in $weights.Keys) {
    Write-VarInt $deltas ($cp - $previous); $previous = $cp
    $sm.Add([byte]$weights[$cp][0]); $aw.Add([byte]$weights[$cp][1]); $dw.Add([byte]$weights[$cp][2])
}

$exp = [System.Collections.Generic.List[byte]]::new()
$previous = 0
foreach ($cp in $expansions.Keys) {
    Write-VarInt $exp ($cp - $previous); $previous = $cp
    $sequence = $expansions[$cp]
    $exp.Add([byte]$sequence.Count)
    foreach ($c in $sequence) { $exp.Add([byte]($c -band 0xFF)); $exp.Add([byte](($c -shr 8) -band 0xFF)) }
}

function Compress([byte[]] $data) {
    $output = [System.IO.MemoryStream]::new()
    $deflate = [System.IO.Compression.ZLibStream]::new($output, [System.IO.Compression.CompressionLevel]::SmallestSize)
    $deflate.Write($data, 0, $data.Length); $deflate.Dispose()
    return $output.ToArray()
}

# NB the comma: piping byte[] through ForEach-Object unrolls it into individual bytes, which silently
# produces five "streams" of one byte each.
$streams = @()
foreach ($stream in @($deltas, $sm, $aw, $dw, $exp)) { $streams += , (Compress $stream.ToArray()) }

$blob = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($blob)
$writer.Write([int]$weights.Count)
$writer.Write([int]$expansions.Count)
# The explicit (byte[], int, int) overload: Write($stream) alone binds to Write(byte) and emits one byte.
foreach ($stream in $streams) { $writer.Write([int]$stream.Length); $writer.Write([byte[]]$stream, 0, $stream.Length) }
$writer.Flush()

$directory = Split-Path -Parent $OutputPath
if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
[System.IO.File]::WriteAllBytes($OutputPath, $blob.ToArray())

# Read the file back and check every section is present and the right size. Both bugs this script hit during
# development wrote a structurally valid but empty file, and both would have shipped silently.
$check = [System.IO.BinaryReader]::new([System.IO.MemoryStream]::new([System.IO.File]::ReadAllBytes($OutputPath)))
$checkWeights = $check.ReadInt32(); $checkExpansions = $check.ReadInt32()
if ($checkWeights -ne $weights.Count -or $checkExpansions -ne $expansions.Count) { throw 'Header counts do not round-trip.' }
for ($i = 0; $i -lt $streams.Count; $i++) {
    $length = $check.ReadInt32()
    if ($length -ne $streams[$i].Length) { throw "Stream $i length is $length, expected $($streams[$i].Length)." }
    if ($check.ReadBytes($length).Length -ne $length) { throw "Stream $i is truncated in the output file." }
}
Write-Host ("wrote {0} ({1:N0} bytes; {2} weights, {3} expansions)" -f `
    $OutputPath, $blob.Length, $weights.Count, $expansions.Count)
