# Regenerates the installer branding assets: banner.bmp (493x58), dialog.bmp (493x312)
# and License.rtf (from the repo LICENSE.txt). Run with Windows PowerShell (needs GDI+):
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File generate-branding.ps1
# Update $version below when cutting a new release.

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$version  = 'v1.0.0'
$dir      = $PSScriptRoot
$repoRoot = Split-Path $PSScriptRoot -Parent
$ico      = Join-Path $repoRoot 'src\Nexaflow.Core\Appicon.ico'

$indigoTop = [System.Drawing.Color]::FromArgb(34,28,68)
$indigoBot = [System.Drawing.Color]::FromArgb(75,63,187)
$accent    = [System.Drawing.Color]::FromArgb(124,108,240)
$ink       = [System.Drawing.Color]::FromArgb(34,28,68)
$subtle    = [System.Drawing.Color]::FromArgb(201,194,245)

function Set-Quality($g) {
  $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
}

# ---------- dialog.bmp : 493 x 312 (Welcome / Exit background) ----------
$w = 493; $h = 312; $band = 132
$d = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($d)
Set-Quality $g
$g.Clear([System.Drawing.Color]::White)
$rectBand = New-Object System.Drawing.Rectangle(0, 0, $band, $h)
$lg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rectBand, $indigoTop, $indigoBot, 90)
$g.FillRectangle($lg, $rectBand)
$g.FillRectangle((New-Object System.Drawing.SolidBrush($accent)), ($band-3), 0, 3, $h)

$isz = 64
$icoObj = New-Object System.Drawing.Icon($ico, $isz, $isz)
$ib = $icoObj.ToBitmap()
$g.DrawImage($ib, [int](($band-$isz)/2), 52, $isz, $isz)

$fName = New-Object System.Drawing.Font('Segoe UI', 16, [System.Drawing.FontStyle]::Bold)
$fSub  = New-Object System.Drawing.Font('Segoe UI', 9)
$white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$subBr = New-Object System.Drawing.SolidBrush($subtle)
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$g.DrawString('Nexaflow', $fName, $white, (New-Object System.Drawing.RectangleF(0, 142, $band, 32)), $sf)
$g.DrawString('Your workflow, unified', $fSub, $subBr, (New-Object System.Drawing.RectangleF(2, 178, ($band-4), 32)), $sf)
$g.DrawString($version, $fSub, $subBr, (New-Object System.Drawing.RectangleF(0, ($h-30), $band, 20)), $sf)
$g.Dispose()
$d.Save((Join-Path $dir 'dialog.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$d.Dispose()

# ---------- banner.bmp : 493 x 58 (interior dialog header) ----------
$w = 493; $h = 58
$b = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($b)
Set-Quality $g
$g.Clear([System.Drawing.Color]::White)
$g.FillRectangle((New-Object System.Drawing.SolidBrush($accent)), 0, ($h-2), $w, 2)

$isz = 32
$icoObj2 = New-Object System.Drawing.Icon($ico, $isz, $isz)
$ib2 = $icoObj2.ToBitmap()
$g.DrawImage($ib2, ($w-12-$isz), 13, $isz, $isz)

$fBan = New-Object System.Drawing.Font('Segoe UI', 14, [System.Drawing.FontStyle]::Bold)
$inkBr = New-Object System.Drawing.SolidBrush($ink)
$sfR = New-Object System.Drawing.StringFormat
$sfR.Alignment = [System.Drawing.StringAlignment]::Far
$sfR.LineAlignment = [System.Drawing.StringAlignment]::Center
$g.DrawString('Nexaflow', $fBan, $inkBr, (New-Object System.Drawing.RectangleF(250, 0, 193, $h)), $sfR)
$g.Dispose()
$b.Save((Join-Path $dir 'banner.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$b.Dispose()

# ---------- License.rtf (from the repo LICENSE.txt) ----------
$lic = Get-Content (Join-Path $repoRoot 'LICENSE.txt') -Raw
$esc = $lic -replace '\\','\\' -replace '\{','\{' -replace '\}','\}'
$esc = $esc -replace "`r`n","`n" -replace "`r","`n" -replace "`n",'\par '
$rtf = '{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fswiss\fcharset0 Segoe UI;}}\fs18\f0 ' + $esc + '}'
Set-Content (Join-Path $dir 'License.rtf') -Value $rtf -Encoding ASCII -NoNewline

# ---------- bundlebanner.png : 485 x 72 (Burn bootstrapper header) ----------
$bundleDir = Join-Path $repoRoot 'nexaflowBundle'
if (Test-Path $bundleDir) {
  $w = 485; $h = 72
  $bb = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
  $g = [System.Drawing.Graphics]::FromImage($bb)
  Set-Quality $g
  $rectAll = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
  $lg2 = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rectAll, $indigoTop, $indigoBot, 0)
  $g.FillRectangle($lg2, $rectAll)
  $g.FillRectangle((New-Object System.Drawing.SolidBrush($accent)), 0, ($h-3), $w, 3)

  $isz = 44
  $icoObj3 = New-Object System.Drawing.Icon($ico, $isz, $isz)
  $g.DrawImage($icoObj3.ToBitmap(), 18, [int](($h-$isz)/2), $isz, $isz)

  $fWord = New-Object System.Drawing.Font('Segoe UI', 22, [System.Drawing.FontStyle]::Bold)
  $fTag  = New-Object System.Drawing.Font('Segoe UI', 9)
  $whiteB = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
  $subB2  = New-Object System.Drawing.SolidBrush($subtle)
  $g.DrawString('Nexaflow', $fWord, $whiteB, 72, 12)
  $g.DrawString('Your workflow, unified', $fTag, $subB2, 76, 48)
  $sfFar = New-Object System.Drawing.StringFormat
  $sfFar.Alignment = [System.Drawing.StringAlignment]::Far
  $g.DrawString($version, $fTag, $subB2, (New-Object System.Drawing.RectangleF(0, 48, ($w-14), 16)), $sfFar)
  $g.Dispose()
  $bb.Save((Join-Path $bundleDir 'bundlebanner.png'), [System.Drawing.Imaging.ImageFormat]::Png)
  $bb.Dispose()
  Write-Output "Wrote nexaflowBundle\bundlebanner.png"
}

Write-Output "Regenerated banner.bmp, dialog.bmp, License.rtf, bundlebanner.png ($version)"
