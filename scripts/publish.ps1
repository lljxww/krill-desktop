param([switch]$FrameworkDependent)
# Backward-compatible Windows entry point.
& (Join-Path $PSScriptRoot 'windows\publish.ps1') @PSBoundParameters
