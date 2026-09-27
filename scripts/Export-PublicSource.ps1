param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Sync-Package.ps1')
$files = [ordered]@{}
foreach ($dir in @('src','tests','docs','scripts','.github')) {
    Get-ChildItem -LiteralPath (Join-Path $root $dir) -Recurse -File | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in @('.cs','.xaml','.csproj','.md','.ps1','.yml','.yaml')
    } | ForEach-Object { $files[$_.FullName.Substring($root.Length + 1)] = $_.FullName }
}
foreach ($name in @('README.md','SECURITY.md','LICENSE','SAMPLE-ASSETS.md','.gitignore','global.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $root $name))) { throw "Missing required public file: $name" }
    $files[$name] = Join-Path $root $name
}
foreach ($name in @((1..7 | ForEach-Object { 'person{0:00}.png' -f $_ }) + (1..8 | ForEach-Object { 'place{0:00}.png' -f $_ }))) {
    $files["ref/$name"] = Join-Path $root "ref/$name"
}
Sync-Package -Root $root -Target (Join-Path $root 'dist/public-source') -Files $files
Write-Output 'Public repository files: dist/public-source'

