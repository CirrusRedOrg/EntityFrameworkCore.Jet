# Regenerates the C# ANTLR lexer/parser/visitor from AccessSql.g4 into Generated/.
# The output is committed so the build needs no Java/codegen. Run this after editing the
# grammar. Requires Java; downloads the matching ANTLR tool jar on first run.
$ver = "4.13.1"   # must match the Antlr4.Runtime.Standard package version
$jar = "$PSScriptRoot\antlr-$ver-complete.jar"
if (-not (Test-Path $jar)) {
    Invoke-WebRequest "https://www.antlr.org/download/antlr-$ver-complete.jar" -OutFile $jar
}
& java -jar $jar -Dlanguage=CSharp -visitor -no-listener -package LibRed.Sql.Grammar -o Generated AccessSql.g4
Write-Output "regenerated Grammar/Generated"
