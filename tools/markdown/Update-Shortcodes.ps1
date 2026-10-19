<#
.SYNOPSIS
    Writes the emoji shortcodes markdown reads — MarkdownShortcodes' list — from GitHub's gemoji.

.DESCRIPTION
    Every alias in gemoji's db/emoji.json, each with the code points of the emoji it names, written into the list in
    src/Nexaflow.Markdown/Prose/MarkdownShortcodes.cs as that class reads it: a name, an equals sign, its code points in
    hexadecimal, a semicolon. The names are in ordinal order, so a new release of gemoji changes only the lines it changes.

    The release is pinned here, and named in the file beside the list; the list is never edited by hand.

.PARAMETER Release
    The gemoji tag to read.

.PARAMETER Into
    The file holding the list.

.EXAMPLE
    pwsh tools/markdown/Update-Shortcodes.ps1
#>
[CmdletBinding()]
param(
    [string]$Release = 'v4.1.0',
    [string]$Into = (Join-Path $PSScriptRoot '..\..\src\Nexaflow.Markdown\Prose\MarkdownShortcodes.cs')
)

$ErrorActionPreference = 'Stop'

$emoji = Invoke-RestMethod -Uri "https://raw.githubusercontent.com/github/gemoji/$Release/db/emoji.json"

$entries = [System.Collections.Generic.List[string]]::new()

foreach ($entry in $emoji) {
    $points = [System.Collections.Generic.List[string]]::new()
    $text = [string]$entry.emoji

    for ($at = 0; $at -lt $text.Length; $at++) {
        $point = [char]::ConvertToUtf32($text, $at)
        if ($point -gt 0xFFFF) { $at++ }
        $points.Add($point.ToString('X'))
    }

    foreach ($alias in $entry.aliases) {
        if ($alias -notmatch '^[a-z0-9_+-]+$') { throw "gemoji $Release names an emoji '$alias', which MarkdownShortcodes cannot read." }
        $entries.Add("$alias=$($points -join ',');")
    }
}

$sorted = $entries.ToArray()
[Array]::Sort($sorted, [StringComparer]::Ordinal)

# Wrapped as the file wraps it: indented eight, no line past 120.
$lines = [System.Collections.Generic.List[string]]::new()
$line = [System.Text.StringBuilder]::new()

foreach ($entry in $sorted) {
    if ($line.Length -gt 0 -and 8 + $line.Length + $entry.Length -gt 120) {
        $lines.Add('        ' + $line.ToString())
        $line.Clear() | Out-Null
    }

    $line.Append($entry) | Out-Null
}

if ($line.Length -gt 0) { $lines.Add('        ' + $line.ToString()) }

$bytes = [System.IO.File]::ReadAllBytes($Into)
$bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
$source = [System.Text.UTF8Encoding]::new($false).GetString($bytes, $(if ($bom) { 3 } else { 0 }), $bytes.Length - $(if ($bom) { 3 } else { 0 }))
$newline = if ($source.Contains("`r`n")) { "`r`n" } else { "`n" }

$list = [regex]::new('(?ms)^(?<indent>[ ]*)// gemoji\b[^\r\n]*\r?\n[ ]*private const string List = """\r?\n.*?^[ ]*""";')
$found = $list.Match($source)
if (-not $found.Success) { throw "No list in $Into to write — it opens with a '// gemoji' line above 'private const string List'." }

$indent = $found.Groups['indent'].Value
$written = @(
    "$indent// gemoji $Release (MIT, Copyright (c) 2019 GitHub, Inc.) — written by tools/markdown/Update-Shortcodes.ps1, never by hand."
    "${indent}private const string List = """""""
    $lines
    "$indent    """""";"
) -join $newline

$source = $source.Remove($found.Index, $found.Length).Insert($found.Index, $written)

$encoding = [System.Text.UTF8Encoding]::new($bom)
[System.IO.File]::WriteAllText((Resolve-Path $Into), $source, $encoding)

Write-Host "Wrote $($sorted.Length) shortcodes from gemoji $Release into $Into."
