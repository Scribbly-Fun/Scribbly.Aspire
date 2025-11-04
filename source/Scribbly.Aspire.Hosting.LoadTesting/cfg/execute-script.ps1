# echo-file.ps1

param(
    [Parameter(Mandatory = $true)]
    [string]$RelativePath
)

$ScriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition
$FullPath = Join-Path $ScriptDirectory $RelativePath

if (-not (Test-Path $FullPath)) {
    Write-Error "File not found: $FullPath"
    exit 1
}

Get-Content -Path $FullPath | ForEach-Object { Write-Output $_ }
