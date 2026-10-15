param([switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$projectFile = Join-Path $projectRoot 'apps\windows\src\Krill.Desktop\Krill.Desktop.csproj'
$flavorSuffix = if ($FrameworkDependent) { '-framework-dependent' } else { '' }
$publishVersion = ([xml](Get-Content -LiteralPath $projectFile -Raw)).Project.PropertyGroup.Version
$publishDirectory = Join-Path $projectRoot ("dist\Krill.Desktop-$publishVersion-win-x64" + $flavorSuffix)
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
dotnet publish $projectFile -c Release -r win-x64 --self-contained $selfContained -p:PublishSingleFile=false -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $publishDirectory 'README.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $publishDirectory -Recurse -Force
$archive = Join-Path $projectRoot ("dist\Krill.Desktop-$publishVersion-win-x64" + $flavorSuffix + '.zip')
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archive -Force
Write-Output $archive
