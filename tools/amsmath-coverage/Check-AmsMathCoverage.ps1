<#
.SYNOPSIS
    Checks the LaTeX engine Nexaflow renders maths with against the amsmath package.

.DESCRIPTION
    Runs every construct in amsmath-checklist.txt through the real parser and renderer
    (WpfMath, from external/xaml-math) and reports which ones typeset, which fall over,
    and which are document-level and so have no meaning in an inline formula.

    With -Refresh it also fetches the current amsmath from CTAN and compares the
    checklist against the command and environment names the package's own user guide
    uses, so a construct added upstream shows up as a gap in the checklist rather than
    being silently missed.

.PARAMETER Refresh
    Fetch amsmath from CTAN and report checklist gaps as well as engine coverage.

.PARAMETER Json
    Write the per-construct results as JSON instead of a table.

.PARAMETER Configuration
    Which build of WpfMath to load. Debug by default.

.EXAMPLE
    pwsh tools/amsmath-coverage/Check-AmsMathCoverage.ps1

.EXAMPLE
    pwsh tools/amsmath-coverage/Check-AmsMathCoverage.ps1 -Refresh
#>
[CmdletBinding()]
param(
    [switch] $Refresh,
    [switch] $Json,
    [string] $Configuration = 'Debug',
    [string] $Checklist = "$PSScriptRoot/amsmath-checklist.txt"
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path "$PSScriptRoot/../.."
$wpfMath = Join-Path $repo "external/xaml-math/src/WpfMath/bin/$Configuration/net8.0-windows/WpfMath.dll"

if (-not (Test-Path $wpfMath)) {
    throw "WpfMath is not built at $wpfMath. Build it first:`n" +
          "  dotnet build external/xaml-math/src/WpfMath/WpfMath.csproj -f net8.0-windows"
}

# ── running the checklist ────────────────────────────────────────────────────────
# WPF wants an STA thread and its pack: scheme registered, so the work happens in a
# child pwsh started with -STA rather than in whatever apartment we were called on.
$runner = {
    param($wpfMathPath, $checklistPath)

    Add-Type -Path $wpfMathPath | Out-Null
    $parser = [WpfMath.Parsers.WpfTeXFormulaParser]::Instance
    $environment = [WpfMath.Rendering.WpfTeXEnvironment]::Create()

    foreach ($line in Get-Content -LiteralPath $checklistPath) {
        if ($line -match '^\s*#' -or $line.Trim() -eq '') { continue }
        $parts = $line -split '\|'
        $section, $name = $parts[0], $parts[1]
        $sample = if ($parts.Count -gt 2) { $parts[2] } else { '' }
        $caveat = if ($parts.Count -gt 3) { $parts[3] } else { '' }

        # A name that is not a command is an environment, so wrap the sample in it.
        $markup =
            if ($sample -eq '') { '' }
            elseif ($name.StartsWith('\')) { $sample }
            else { "\begin{$name}$sample\end{$name}" }

        $status, $detail = 'n/a', ''
        if ($markup -ne '') {
            try {
                # Render rather than merely parse: a wrong glyph mapping parses perfectly well and
                # only falls over when a box is built for it. (The atom tree is internal to the
                # assembly, so rendering is also the only view of it from out here.)
                $formula = $parser.Parse($markup)
                $geometry = [WpfMath.Rendering.WpfTeXFormulaExtensions]::RenderToGeometry(
                    $formula, $environment, 20.0, 0.0, 0.0)
                $status = if ($geometry.Bounds.IsEmpty) { 'empty' } else { 'ok' }
            } catch {
                $status = 'no'
                $detail = ($_.Exception.GetBaseException().Message -replace '\s+', ' ')
            }
        }

        # Something that renders but is known to be approximate is not the same as support.
        if ($status -eq 'ok' -and $caveat -ne '') { $status = 'partial'; $detail = $caveat }

        [pscustomobject]@{
            Status  = $status
            Section = $section
            Name    = $name
            Markup  = $markup
            Detail  = $detail
        }
    }
}

$encoded = [Convert]::ToBase64String(
    [Text.Encoding]::Unicode.GetBytes("& { $runner } '$wpfMath' '$Checklist' | ConvertTo-Json -Depth 3"))
$raw = & pwsh -STA -NoProfile -EncodedCommand $encoded
if ($LASTEXITCODE -ne 0) { throw "The checklist run failed:`n$raw" }
$results = @($raw | ConvertFrom-Json)

# ── report ───────────────────────────────────────────────────────────────────────
if ($Json) {
    $results | ConvertTo-Json -Depth 3
} else {
    $bySection = $results | Group-Object Section
    foreach ($group in $bySection) {
        $supported = @($group.Group | Where-Object Status -eq 'ok').Count
        $applicable = @($group.Group | Where-Object Status -ne 'n/a').Count
        $suffix = if ($applicable -eq 0) { 'document-level' } else { "$supported/$applicable" }
        Write-Host ''
        Write-Host ("{0}  [{1}]" -f $group.Name, $suffix)
        foreach ($r in $group.Group) {
            $mark = switch ($r.Status) {
                'ok'      { '  OK  ' }
                'n/a'     { '  --  ' }
                'partial' { '  ~~  ' }
                default   { '  NO  ' }
            }
            $note = if ($r.Detail) { "   $($r.Detail)" } else { '' }
            Write-Host ("{0} {1}{2}" -f $mark, $r.Name, $note)
        }
    }

    $ok = @($results | Where-Object Status -eq 'ok').Count
    $na = @($results | Where-Object Status -eq 'n/a').Count
    $partial = @($results | Where-Object Status -eq 'partial').Count
    $bad = $results.Count - $ok - $na - $partial
    Write-Host ''
    Write-Host ("{0} of {1} testable constructs render fully, {2} with a caveat (~~), {3} not at all." -f
        $ok, ($ok + $partial + $bad), $partial, $bad)
    $naWord = if ($na -eq 1) { 'construct is' } else { 'constructs are' }
    Write-Host ("{0} further {1} document-level (--), with no meaning in a standalone formula." -f $na, $naWord)
}

# ── checklist freshness ──────────────────────────────────────────────────────────
if (-not $Refresh) { return }

Write-Host ''
Write-Host 'Fetching amsmath from CTAN to check the checklist is still complete...'

$work = Join-Path ([IO.Path]::GetTempPath()) ("amsmath-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
try {
    $meta = Invoke-RestMethod 'https://ctan.org/json/2.0/pkg/amsmath'
    $zip = Join-Path $work 'amsmath.tds.zip'

    # CTAN's redirector answers a script with an anti-bot page rather than the file, so go to
    # named mirrors and check what came back really is an archive before trusting it.
    $mirrors = @(
        'https://ch.mirrors.cicku.me/ctan'
        'https://mirrors.mit.edu/CTAN'
        'https://ctan.math.illinois.edu'
        'https://mirror.ctan.org'
    )
    $got = $false
    foreach ($mirror in $mirrors) {
        $url = "$mirror/install$($meta.install)"
        try {
            Invoke-WebRequest -Uri $url -OutFile $zip -ErrorAction Stop
            $head = [byte[]]::new(2)
            $stream = [IO.File]::OpenRead($zip)
            try { [void]$stream.Read($head, 0, 2) } finally { $stream.Dispose() }
            if ($head[0] -eq 0x50 -and $head[1] -eq 0x4B) { $got = $true; break }  # "PK"
            Write-Host "  $mirror did not return an archive; trying the next"
        } catch {
            Write-Host "  $mirror failed: $($_.Exception.Message.Split([char]10)[0])"
        }
    }
    if (-not $got) { throw 'Could not fetch amsmath from any known CTAN mirror.' }

    Expand-Archive -LiteralPath $zip -DestinationPath $work -Force

    $sty = Get-ChildItem -LiteralPath $work -Recurse -Filter 'amsmath.sty' | Select-Object -First 1
    $version = (Select-String -LiteralPath $sty.FullName -Pattern 'ProvidesPackage\{amsmath\}\[(.+?)\]').Matches[0].Groups[1].Value
    Write-Host "  amsmath $version"

    $guide = Get-ChildItem -LiteralPath $work -Recurse -Filter 'amsldoc.tex' | Select-Object -First 1
    $guideText = Get-Content -LiteralPath $guide.FullName -Raw

    # The guide names commands with \cn{...} and shows environments with \begin{...}.
    $named = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($m in [regex]::Matches($guideText, '\\cn\{\\?(\w+\*?)\}')) { [void]$named.Add('\' + $m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($guideText, '\\begin\{(\w+\*?)\}')) { [void]$named.Add($m.Groups[1].Value) }

    # A checklist row can name several commands at once ("\bigl \bigr"), and the guide writes an
    # environment as \cn{\align} as often as \begin{align}, so compare on bare names.
    $covered = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($r in $results) {
        foreach ($part in ($r.Name -replace '[\[\(].*$', '') -split '\s+') {
            if ($part) { [void]$covered.Add($part.TrimStart('\')) }
        }
    }

    # Names the guide uses for LaTeX itself, or for its own examples and document markup, are not
    # amsmath constructs and would only be noise here.
    $notAmsMath = @(
        'addtocounter', 'addtolength', 'documentclass', 'label', 'newcommand', 'normalfont',
        'pagebreak', 'par', 'ref', 'ref*', 'relax', 'setcounter', 'setlength', 'theequation',
        'usepackage', 'value', 'qed', 'qedhere', 'C', 'R', 'sum',
        'bfseries', 'center', 'ctab', 'description', 'document', 'enumerate', 'eqxample',
        'error', 'erroro', 'infoaddress', 'itemize', 'math', 'minipage', 'raggedright',
        'tabbing', 'table', 'tabular', 'thebibliography', 'verbatim'
    )

    $missing = @($named |
        ForEach-Object { $_.TrimStart('\') } |
        Where-Object { -not $covered.Contains($_) -and $notAmsMath -notcontains $_ } |
        Sort-Object -Unique)
    if ($missing.Count -eq 0) {
        Write-Host '  The checklist names everything the guide does.'
    } else {
        Write-Host ("  {0} name(s) the guide uses that the checklist does not cover:" -f $missing.Count)
        Write-Host ('    ' + ($missing -join ' '))
        Write-Host '  Some will be LaTeX rather than amsmath, or prose rather than a construct - judge each.'
    }
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
