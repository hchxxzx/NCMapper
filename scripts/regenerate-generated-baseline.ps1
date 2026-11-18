# Full-accuracy baseline regeneration (twin of verify-generated-baseline.ps1;
# build params MUST match verify: -f net10.0 -c Release --no-incremental -m:1 EmitCompilerGeneratedFiles)
param(
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$baselineRoot = Join-Path $repo 'generated-baseline'
$outRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'ncmapper-baseline-regen'
if (Test-Path $outRoot) { Remove-Item $outRoot -Recurse -Force }
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

function Normalize-Copy([string]$src, [string]$dst) {
    $text = [System.IO.File]::ReadAllText($src).Replace("`r`n", "`n")
    [System.IO.File]::WriteAllText($dst, $text, (New-Object System.Text.UTF8Encoding($false)))
}
function Find-Gen([string]$root, [string]$filter) {
    $found = @()
    if (-not (Test-Path $root)) { return $found }
    Get-ChildItem $root -Recurse -File -Filter $filter -ErrorAction SilentlyContinue |
        ForEach-Object { $found += $_.FullName }
    return $found
}

$projects = @(
    @{ Name = 'samples';    Proj = 'samples/NCMapper.Samples/NCMapper.Samples.csproj';       Out = 'samples' },
    @{ Name = 'test';       Proj = 'test/NCMapper.Tests/NCMapper.Tests.csproj';              Out = 'test' },
    @{ Name = 'benchmarks'; Proj = 'benchmarks/NCMapper.SelfBench/NCMapper.SelfBench.csproj'; Out = 'benchmarks' }
)

foreach ($p in $projects) {
    if (-not $SkipBuild) {
        Write-Host "[baseline-regen] Building $($p.Name) ..."
        $buildOut = Join-Path $outRoot $p.Out
        & dotnet build (Join-Path $repo $p.Proj) -f net10.0 --configuration Release --nologo --no-incremental -m:1 `
            -p:EnableSourceLink=false `
            -p:EmitCompilerGeneratedFiles=true `
            -p:CompilerGeneratedFilesOutputPath=$buildOut 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Error "[baseline-regen] build failed: $($p.Name)"; exit 1 }
    }
    $srcRoot = Join-Path $outRoot $p.Out
    $gen = Find-Gen $srcRoot '*GeneratedMapper.g.cs'    | Where-Object { $_ -match 'NCMapper.SourceGen' } | Select-Object -First 1
    $rs  = Find-Gen $srcRoot 'NCMapper_RuntimeSnapshotMetadata.g.cs'  | Select-Object -First 1
    $id  = Find-Gen $srcRoot 'NCMapper_FluentSnapshotRegistry.g.cs'   | Select-Object -First 1
    if ($gen) { Normalize-Copy $gen (Join-Path $baselineRoot "$($p.Name)/NCMapper_Generated_GeneratedMapper.g.cs"); Write-Host "[regen] $($p.Name) GeneratedMapper OK" }
    else { Write-Host "[regen] $($p.Name) GeneratedMapper NOT FOUND" }
    if ($rs)  { Normalize-Copy $rs  (Join-Path $baselineRoot "runtime-snapshot/$($p.Name)/NCMapper_RuntimeSnapshotMetadata.g.cs"); Write-Host "[regen] $($p.Name) RuntimeSnapshot OK" }
    if ($id)  { Normalize-Copy $id  (Join-Path $baselineRoot "runtime-snapshot/$($p.Name)/NCMapper_FluentSnapshotRegistry.g.cs"); Write-Host "[regen] $($p.Name) FluentRegistry OK" }
}
Write-Host "[baseline-regen] DONE"
