# Downloads the Visual C++ 2015-2022 Redistributable (x64) for local installer builds.
# CI/CD downloads this separately in the workflow.

$url = "https://aka.ms/vs/17/release/vc_redist.x64.exe"
$output = Join-Path $PSScriptRoot "vc_redist.x64.exe"

if (Test-Path $output) {
    Write-Host "vc_redist.x64.exe already exists, skipping download."
    return
}

Write-Host "Downloading VC++ Redistributable from $url..."
Invoke-WebRequest -Uri $url -OutFile $output
Write-Host "Downloaded to $output"
