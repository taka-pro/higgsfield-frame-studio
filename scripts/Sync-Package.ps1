# Sync managed files only; preserve user data and back up changed originals.
function Sync-Package {
    param([string]$Root, [string]$Target, [System.Collections.IDictionary]$Files)
    $rootPath = [IO.Path]::GetFullPath($Root)
    $distPath = [IO.Path]::GetFullPath((Join-Path $rootPath 'dist')) + [IO.Path]::DirectorySeparatorChar
    $targetPath = [IO.Path]::GetFullPath($Target)
    if (!$targetPath.StartsWith($distPath, [StringComparison]::OrdinalIgnoreCase)) { throw 'Target must be inside dist.' }
    $backupRoot = Join-Path $rootPath ('old/' + (Get-Date -Format 'yyyyMMdd_HHmmss_fff'))
    function Resolve-Managed([string]$relative) {
        $p = [IO.Path]::GetFullPath((Join-Path $targetPath $relative))
        if (!$p.StartsWith($targetPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid package path.' }
        return $p
    }
    function Backup-Managed([string]$path) {
        $dest = Join-Path $backupRoot $path.Substring($rootPath.Length + 1)
        New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $path -Destination $dest
    }
    New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
    $manifest = Join-Path $targetPath 'PUBLIC-FILES.txt'
    $previous = if (Test-Path -LiteralPath $manifest) { @(Get-Content -LiteralPath $manifest) } else { @() }
    foreach ($relative in $Files.Keys) {
        $source = $Files[$relative]
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing input: $relative" }
        $dest = Resolve-Managed $relative
        if (Test-Path -LiteralPath $dest) {
            if ((Get-FileHash -LiteralPath $source).Hash -eq (Get-FileHash -LiteralPath $dest).Hash) { continue }
            Backup-Managed $dest
        }
        New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $dest -Force
    }
    foreach ($relative in $previous) {
        if ($relative -and !$Files.Contains($relative)) {
            $path = Resolve-Managed $relative
            if (Test-Path -LiteralPath $path -PathType Leaf) { Backup-Managed $path; Remove-Item -LiteralPath $path }
        }
    }
    $text = (@($Files.Keys | Sort-Object) -join "`n") + "`n"
    if (!(Test-Path -LiteralPath $manifest) -or (Get-Content -LiteralPath $manifest -Raw) -cne $text) {
        if (Test-Path -LiteralPath $manifest) { Backup-Managed $manifest }
        [IO.File]::WriteAllText($manifest, $text, [Text.UTF8Encoding]::new($false))
    }
}
