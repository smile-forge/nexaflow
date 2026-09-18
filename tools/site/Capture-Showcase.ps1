<#
.SYNOPSIS
    Takes the pictures of the app used by the README and the site.

.DESCRIPTION
    Drives the real Nexaflow.exe through the UI journey harness, once per theme, against a demo workspace the
    test builds and mounts as its own drive. Nothing of this machine reaches a picture: the config directory is
    a fresh temporary one, the files on show are invented, and the test reads the whole window's automation tree
    and refuses to write a picture that names the machine or its owner.

    It takes the mouse and keyboard while it runs, so leave the machine alone for a minute. Run it when the app
    or a theme has changed enough that the pictures no longer look like it, then commit what changed.

.EXAMPLE
    tools/site/Capture-Showcase.ps1
    Rewrites docs/images/showcase/ in place.
#>
[CmdletBinding()]
param(
    # Where to write the pictures. Defaults to docs/images/showcase, which the README and the site both read.
    [string] $Output,

    # Capture only these themes. Defaults to all of them.
    [string[]] $Themes = @('Dark', 'Light', 'Ocean', 'Nature', 'Sandstone', 'Gothic', 'Arctic', 'Flowers', 'Sunny'),

    # The window is captured at whatever size it opens, which is far more than a page ever shows. Every picture
    # is scaled down to this width afterwards - nine of them go on one page, so the bytes matter more than the
    # pixels nobody sees.
    [int] $MaxWidth = 1200,

    # And written as JPEG rather than PNG. Several themes paint an illustrated backdrop, which is photographic
    # enough that lossless costs three times the bytes for no visible gain at the size a page shows them.
    [int] $Quality = 90
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Output) { $Output = Join-Path $repo 'docs/images/showcase' }

$project = Join-Path $repo 'src/Nexaflow.Tests/Nexaflow.Tests.UIJourneys/Nexaflow.Tests.UIJourneys.csproj'

Write-Host 'Building the journey suite...' -ForegroundColor Cyan
dotnet build $project -c Debug --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw "the journey suite did not build (exit $LASTEXITCODE)" }

$exe = Get-ChildItem (Join-Path $repo 'src/Nexaflow.Tests/Nexaflow.Tests.UIJourneys/bin') -Recurse `
                     -Filter 'Nexaflow.Tests.UIJourneys.exe' | Select-Object -First 1
if (-not $exe) { throw 'Nexaflow.Tests.UIJourneys.exe was not found - build it first' }

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$env:NEXAFLOW_WRITE_SHOWCASE = $Output

try {
    Write-Host 'Capturing the file browser...' -ForegroundColor Cyan
    & $exe.FullName --filter 'FullyQualifiedName~ShowcaseCaptureTests.CaptureTheFileBrowser' | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'the file browser capture failed - the window may have named this machine' }

    foreach ($theme in $Themes) {
        Write-Host "Capturing the editor in $theme..." -ForegroundColor Cyan
        $env:NEXAFLOW_SHOWCASE_THEME = $theme
        & $exe.FullName --filter 'FullyQualifiedName~ShowcaseCaptureTests.CaptureTheMarkdownEditor' | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "the $theme capture failed - the window may have named this machine" }
    }
}
finally {
    Remove-Item Env:\NEXAFLOW_SHOWCASE_THEME -ErrorAction SilentlyContinue
    Remove-Item Env:\NEXAFLOW_WRITE_SHOWCASE -ErrorAction SilentlyContinue
}

Write-Host "Scaling to $MaxWidth px and writing JPEG q$Quality..." -ForegroundColor Cyan
Add-Type -AssemblyName System.Drawing

$jpeg = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
$settings = New-Object System.Drawing.Imaging.EncoderParameters 1
$settings.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter `
    ([System.Drawing.Imaging.Encoder]::Quality), ([long]$Quality)

foreach ($file in Get-ChildItem $Output -Filter *.png) {
    $source = [System.Drawing.Image]::FromFile($file.FullName)
    try {
        $width  = [math]::Min($MaxWidth, $source.Width)
        $height = [int][math]::Round($source.Height * ($width / $source.Width))

        $scaled = New-Object System.Drawing.Bitmap $width, $height
        $graphics = [System.Drawing.Graphics]::FromImage($scaled)
        try {
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source, 0, 0, $width, $height)
        }
        finally { $graphics.Dispose() }

        $scaled.Save([IO.Path]::ChangeExtension($file.FullName, '.jpg'), $jpeg, $settings)
        $scaled.Dispose()
    }
    finally { $source.Dispose() }

    [IO.File]::Delete($file.FullName)
}

Write-Host "Done. Look at every picture before committing it." -ForegroundColor Green
Get-ChildItem $Output -Filter *.jpg | Select-Object Name, @{ n = 'KB'; e = { [math]::Round($_.Length / 1KB) } } | Out-Host
