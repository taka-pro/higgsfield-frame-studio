param([ValidatePattern('^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$')][string]$Version = '0.1.0-beta.1')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Sync-Package.ps1')
$stage = [IO.Path]::GetFullPath((Join-Path $root '.local/release-build'))
$allowed = [IO.Path]::GetFullPath((Join-Path $root '.local')) + [IO.Path]::DirectorySeparatorChar
if (!$stage.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid build scratch path.' }
if (Get-Process HiggsfieldStudio -ErrorAction SilentlyContinue) { throw 'Close HiggsfieldStudio before updating dist/FrameStudio.' }
foreach ($file in @('LICENSE','SAMPLE-ASSETS.md')) { if (!(Test-Path (Join-Path $root $file))) { throw "Missing $file" } }
dotnet run --project (Join-Path $root 'tests/HiggsfieldStudio.Tests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
dotnet publish (Join-Path $root 'src/HiggsfieldStudio/HiggsfieldStudio.csproj') -c Release -r win-x64 --self-contained true -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false -o $stage
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$files = [ordered]@{}
Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object { $files[$_.FullName.Substring($stage.Length + 1)] = $_.FullName }
foreach ($name in @('README.md','SECURITY.md','LICENSE','SAMPLE-ASSETS.md')) { $files[$name] = Join-Path $root $name }
$runtime = Get-Content -LiteralPath (Join-Path $stage 'HiggsfieldStudio.runtimeconfig.json') -Raw | ConvertFrom-Json
$nugetLine = dotnet nuget locals global-packages --list --force-english-output
if ($LASTEXITCODE -ne 0) { throw 'Cannot locate NuGet runtime packages.' }
$nuget = ($nugetLine -replace '^global-packages:\s*', '').Trim()
foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
    $package = Join-Path $nuget ($framework.name.ToLowerInvariant() + '.runtime.win-x64/' + $framework.version)
    $notices = @(Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' })
    if ($notices.Count -eq 0) { throw "Missing runtime license: $($framework.name)" }
    foreach ($notice in $notices) { $files['THIRD-PARTY/' + $framework.name + '/' + $notice.Name] = $notice.FullName }
}
foreach ($name in @((1..7 | ForEach-Object { 'person{0:00}.png' -f $_ }) + (1..8 | ForEach-Object { 'place{0:00}.png' -f $_ }))) { $files["ref/$name"] = Join-Path $root "ref/$name" }
$target = Join-Path $root 'dist/FrameStudio'
Sync-Package -Root $root -Target $target -Files $files
# ZIP allowlist excludes saved keys, history, outputs and any extra user files.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$temporaryZip = Join-Path $root '.local/FrameStudio-win-x64.zip'
if (Test-Path -LiteralPath $temporaryZip) { Remove-Item -LiteralPath $temporaryZip }
$archive = [IO.Compression.ZipFile]::Open($temporaryZip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relative in $files.Keys) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $target $relative), ('FrameStudio/' + $relative.Replace('\','/')), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $temporaryZip -Algorithm SHA256).Hash.ToLowerInvariant()
$hashFile = Join-Path $root '.local/FrameStudio-win-x64.zip.sha256'
"$hash  FrameStudio-win-x64.zip" | Set-Content -LiteralPath $hashFile -Encoding ascii
Sync-Package -Root $root -Target (Join-Path $root 'dist/releases') -Files ([ordered]@{'FrameStudio-win-x64.zip'=$temporaryZip; 'FrameStudio-win-x64.zip.sha256'=$hashFile})
Write-Output 'Download: dist/releases/FrameStudio-win-x64.zip'
Write-Output 'App: dist/FrameStudio/HiggsfieldStudio.exe'

