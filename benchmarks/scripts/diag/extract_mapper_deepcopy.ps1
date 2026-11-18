$RepoRoot = Resolve-Path "$PSScriptRoot/../../.."
$dir = "$RepoRoot\BenchmarkDotNet.Artifacts\disasm_compare"
foreach ($fw in @('net8.0','net9.0','net10.0')) {
    $log = Join-Path $dir "jit_disasm_$fw.log"
    $out = Join-Path $dir "Mapper_DeepCopy_$fw.txt"
    $lines = Get-Content $log
    $startIdx = -1
    $endIdx = $lines.Count
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^; Assembly listing for method NCMapper\.Mapper:DeepCopy') {
            if ($startIdx -eq -1) { $startIdx = $i }
            else { $endIdx = $i; break }
        }
    }
    if ($startIdx -ge 0) {
        $extracted = $lines[$startIdx..($endIdx - 1)]
        $extracted | Out-File $out -Encoding utf8
        $size = (Get-Item $out).Length
        Write-Host "$fw : $($extracted.Count) lines, $size bytes (offset $startIdx -> $endIdx)"
    } else {
        Write-Host "$fw : NOT FOUND"
    }
}
