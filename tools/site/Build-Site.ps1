<#
.SYNOPSIS
    Regenerates the GitHub Pages site from the help pages the app ships.

.DESCRIPTION
    Run this when cutting a release, then commit site/ with the release commit — the Pages workflow only
    uploads what is committed, so nothing is built on the runner. The version on the site is read from
    src/Nexaflow.Core/Nexaflow.Core.csproj, so bump that first.

.EXAMPLE
    tools/site/Build-Site.ps1
    Rewrites site/ in place. Review `git status` before committing.
#>
[CmdletBinding()]
param(
    # Where to write the site. Defaults to site/ at the repo root, which is what the Pages workflow uploads.
    [string] $Output
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'Nexaflow.SiteGenerator/Nexaflow.SiteGenerator.csproj'

Write-Host 'Building the site generator...' -ForegroundColor Cyan
dotnet build $project -c Release --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw "the site generator did not build (exit $LASTEXITCODE)" }

Write-Host 'Generating the site...' -ForegroundColor Cyan
$arguments = @('run', '--project', $project, '-c', 'Release', '--no-build', '--')
if ($Output) { $arguments += $Output }

dotnet @arguments | Out-Host
if ($LASTEXITCODE -ne 0) { throw "the site generator failed (exit $LASTEXITCODE)" }

Write-Host 'Done. Review `git status`, then commit site/ with the release.' -ForegroundColor Green
