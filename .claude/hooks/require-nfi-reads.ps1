# PreToolUse hook: refuses reading or searching a Nexaflow checkout's own files around nfi — Get-Content,
# Select-String, cat, head, tail, grep, rg, sed, awk, git grep, and the Read, Grep and Glob tools. Anything git
# ignores (build output, TestResults, .product), the submodules under external/, binary files given to Read and
# every path outside a checkout pass. require-nfi-reads.sh hands over only the calls that could match.
# A failure inside the hook lets the call through: the hook guards a habit, it must never stop work.

$ErrorActionPreference = 'Stop'

$PathParameters = 'Path', 'LiteralPath', 'PSPath', 'LP'
$ValueParameters = @{
    'Get-Content'   = 'Path', 'LiteralPath', 'PSPath', 'LP', 'TotalCount', 'Head', 'First', 'Tail', 'Last',
                      'ReadCount', 'Encoding', 'Delimiter', 'Filter', 'Include', 'Exclude', 'Stream'
    'Select-String' = 'Path', 'LiteralPath', 'PSPath', 'LP', 'Pattern', 'Context', 'Encoding', 'Include',
                      'Exclude', 'Culture', 'InputObject'
}
$PowerShellReaders = @{ 'get-content' = 'Get-Content'; 'gc' = 'Get-Content'; 'cat' = 'Get-Content'; 'type' = 'Get-Content'
                        'select-string' = 'Select-String'; 'sls' = 'Select-String' }
$UnixReaders = 'cat', 'head', 'tail', 'less', 'more', 'grep', 'egrep', 'fgrep', 'rg', 'sed', 'awk'
$UnixValueOptions = @{
    'grep' = '-e', '-f', '-m', '-A', '-B', '-C', '-d', '-D', '--regexp', '--file', '--max-count', '--context',
             '--include', '--exclude', '--exclude-dir'
    'rg'   = '-e', '-f', '-m', '-A', '-B', '-C', '-g', '-t', '-T', '-j', '--regexp', '--file', '--max-count',
             '--context', '--glob', '--type', '--type-not', '--threads'
    'head' = '-n', '-c', '--lines', '--bytes'
    'tail' = '-n', '-c', '--lines', '--bytes'
    'sed'  = '-e', '-f', '--expression', '--file'
    'awk'  = '-f', '-v', '-F'
}
$ScriptOptions = @{ 'grep' = '-e', '-f', '--regexp', '--file'; 'rg' = '-e', '-f', '--regexp', '--file'
                    'sed' = '-e', '-f', '--expression', '--file'; 'awk' = '-f' }
$BinaryExtensions = '.png', '.jpg', '.jpeg', '.gif', '.bmp', '.webp', '.ico', '.tif', '.tiff', '.pdf'

function Resolve-CallTarget([string] $Path, [string] $Base) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -match '[$`~]') { return $null }
    if ($Path -match '^/([a-zA-Z])(/.*)?$') { $Path = "$($Matches[1]):$($Matches[2])" }
    # A wildcard reads the folder it sits in.
    $parts = $Path -split '[\\/]'
    $fixed = @(); foreach ($p in $parts) { if ($p -match '[*?\[]') { break }; $fixed += $p }
    $Path = if ($fixed.Count) { $fixed -join '\' } else { '.' }
    if ($Path -match '^[a-zA-Z]:$') { $Path += '\' }
    return [IO.Path]::GetFullPath($Path, $Base)
}

function Get-CheckoutRelativePath([string] $Full) {
    $dir = $Full
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) { $dir = Split-Path $dir -Parent }
    while ($dir -and -not (Test-Path -LiteralPath $dir -PathType Container)) { $dir = Split-Path $dir -Parent }
    if (-not $dir) { return $null }
    $top = git -C $dir rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $top) { return $null }
    $top = [IO.Path]::GetFullPath($top)
    if (-not (Test-Path -LiteralPath (Join-Path $top 'Nexaflow.slnx'))) { return $null }
    $relative = [IO.Path]::GetRelativePath($top, $Full) -replace '\\', '/'
    if ($relative -ne '.') {
        git -C $top check-ignore -q -- $relative 2>$null
        if ($LASTEXITCODE -eq 0) { return $null }
    }
    return $relative
}

function Get-StaticValues($Element) {
    switch ($Element) {
        { $_ -is [System.Management.Automation.Language.StringConstantExpressionAst] } { return , $_.Value }
        { $_ -is [System.Management.Automation.Language.ExpandableStringExpressionAst] } {
            if ($_.NestedExpressions.Count -eq 0) { return , $_.Value }; return @() }
        { $_ -is [System.Management.Automation.Language.ArrayLiteralAst] } {
            return @($_.Elements | ForEach-Object { Get-StaticValues $_ }) }
    }
    return @()
}

# The files a PowerShell Get-Content or Select-String call names.
function Get-PowerShellReaderTargets([string] $Reader, $Elements) {
    $named = @(); $positional = @(); $patternNamed = $false
    for ($i = 0; $i -lt $Elements.Count; $i++) {
        $e = $Elements[$i]
        if ($e -is [System.Management.Automation.Language.CommandParameterAst]) {
            $name = $ValueParameters[$Reader] | Where-Object { $_ -like "$($e.ParameterName)*" } | Select-Object -First 1
            if (-not $name) { continue }
            $value = $e.Argument
            if (-not $value -and $i + 1 -lt $Elements.Count) { $i++; $value = $Elements[$i] }
            if ($name -eq 'Pattern') { $patternNamed = $true }
            if ($name -in $PathParameters -and $value) { $named += Get-StaticValues $value }
            continue
        }
        $positional += , $e
    }
    $pathAt = if ($Reader -eq 'Select-String' -and -not $patternNamed) { 1 } else { 0 }
    if ($named.Count -eq 0 -and $positional.Count -gt $pathAt) { $named += Get-StaticValues $positional[$pathAt] }
    return $named
}

# The files a Unix reader names; with none it reads its input, except a recursive search, which reads the folder.
function Get-UnixReaderTargets([string] $Reader, [object[]] $Words, [bool] $Piped) {
    $family = switch ($Reader) { { $_ -in 'grep', 'egrep', 'fgrep' } { 'grep' } default { $_ } }
    $valueOptions = $UnixValueOptions[$family]
    $takesScript = $family -in 'grep', 'rg', 'sed', 'awk'
    $operands = @(); $scriptGiven = $false; $recursive = $false; $unresolved = $false
    for ($i = 0; $i -lt $Words.Count; $i++) {
        $w = $Words[$i]
        if ($null -eq $w) { $unresolved = $true; continue }
        if ($w -eq '--') { $operands += @($Words | Select-Object -Skip ($i + 1) | Where-Object { $null -ne $_ }); break }
        if ($w -notmatch '^-.') { $operands += $w; continue }
        if ($family -eq 'grep' -and ($w -cmatch '^-[a-zA-Z]*[rR]' -or $w -in '--recursive', '--dereference-recursive')) { $recursive = $true }
        $option = ($w -split '=', 2)[0]
        if ($option -cin $ScriptOptions[$family]) { $scriptGiven = $true }
        if ($option -cin $valueOptions -and $w -notmatch '=') { $i++ }
    }
    if ($takesScript -and -not $scriptGiven -and $operands.Count) { $operands = @($operands | Select-Object -Skip 1) }
    if ($operands.Count) { return $operands }
    if ($unresolved -or $Piped) { return @() }
    if ($family -eq 'rg' -or $recursive) { return @('.') }
    return @()
}
# Splits a POSIX command into pipeline segments of words; a word holding an expansion comes back as $null.
function Split-ShellCommand([string] $Command) {
    $segments = @(); $words = [System.Collections.Generic.List[object]]::new(); $piped = $false
    $word = [Text.StringBuilder]::new(); $inWord = $false; $dynamic = $false; $redirect = $false
    $flush = {
        if ($inWord) {
            if ($redirect) { $redirect = $false } else { $words.Add($(if ($dynamic) { $null } else { $word.ToString() })) }
        }
        [void]$word.Clear(); $inWord = $false; $dynamic = $false
    }
    $end = {
        param([bool] $nextPiped)
        . $flush
        if ($words.Count) { $segments += , @{ Words = $words.ToArray(); Piped = $piped } }
        $words.Clear(); $piped = $nextPiped
    }
    $i = 0
    while ($i -lt $Command.Length) {
        $c = $Command[$i]
        if ($c -eq "'") {
            $close = $Command.IndexOf("'", $i + 1); if ($close -lt 0) { $close = $Command.Length }
            [void]$word.Append($Command.Substring($i + 1, $close - $i - 1)); $inWord = $true; $i = $close + 1; continue
        }
        if ($c -eq '"') {
            $i++
            while ($i -lt $Command.Length -and $Command[$i] -ne '"') {
                if ($Command[$i] -eq '\' -and $i + 1 -lt $Command.Length) { $i++ }
                elseif ($Command[$i] -in '$', '`') { $dynamic = $true }
                [void]$word.Append($Command[$i]); $i++
            }
            $inWord = $true; $i++; continue
        }
        if ($c -eq '\' -and $i + 1 -lt $Command.Length) { [void]$word.Append($Command[$i + 1]); $inWord = $true; $i += 2; continue }
        if ($c -in ' ', "`t") { . $flush; $i++; continue }
        if ($c -in "`n", ';', '(', ')') { . $end $false; $i++; continue }
        if ($c -eq '&') { . $end $false; $i += $(if ($i + 1 -lt $Command.Length -and $Command[$i + 1] -eq '&') { 2 } else { 1 }); continue }
        if ($c -eq '|') {
            if ($i + 1 -lt $Command.Length -and $Command[$i + 1] -eq '|') { . $end $false; $i += 2 } else { . $end $true; $i++ }
            continue
        }
        if ($c -in '<', '>') { . $flush; $redirect = $true; $i++; while ($i -lt $Command.Length -and $Command[$i] -in '>', '&') { $i++ }; continue }
        if ($c -in '$', '`') { $dynamic = $true }
        [void]$word.Append($c); $inWord = $true; $i++
    }
    . $end $false
    return $segments
}

function Get-ShellTargets([string] $Command, [string] $Base) {
    $found = @()
    foreach ($segment in Split-ShellCommand $Command) {
        if ($segment.Words[0] -in 'cd', 'pushd' -and $segment.Words.Count -gt 1) {
            $Base = Resolve-CallTarget $segment.Words[1] $Base
            if (-not $Base) { return $found }
            continue
        }
        $all = $segment.Words; $first = 0
        while ($first -lt $all.Count -and $null -ne $all[$first] -and $all[$first] -match '^[A-Za-z_][A-Za-z0-9_]*=') { $first++ }
        if ($first -ge $all.Count -or $null -eq $all[$first]) { continue }
        $words = $all[$first..($all.Count - 1)]
        $name = ($words[0] -split '[\\/]')[-1] -replace '\.exe$', ''
        $rest = @($words | Select-Object -Skip 1)
        if ($name -eq 'git') { $found += Get-GitGrepTargets $rest | ForEach-Object { $_.Base = $Base; $_ }; continue }
        if ($name -in $UnixReaders) {
            $found += Get-UnixReaderTargets $name $rest $segment.Piped | ForEach-Object { @{ Reader = $name; Path = $_; Base = $Base } }
        }
    }
    return $found
}

# git grep searches the work tree it runs in.
function Get-GitGrepTargets([object[]] $Words) {
    $folder = '.'
    for ($i = 0; $i -lt $Words.Count; $i++) {
        if ($Words[$i] -in '-C', '-c' -and $i + 1 -lt $Words.Count) { if ($Words[$i] -eq '-C') { $folder = $Words[$i + 1] }; $i++; continue }
        if ($Words[$i] -like '-*') { continue }
        if ($Words[$i] -eq 'grep' -and $null -ne $folder) { return , @{ Reader = 'git grep'; Path = $folder } }
        return @()
    }
    return @()
}

function Get-PowerShellTargets([string] $Command) {
    $ast = [System.Management.Automation.Language.Parser]::ParseInput($Command, [ref]$null, [ref]$null)
    $found = @()
    foreach ($call in $ast.FindAll({ $args[0] -is [System.Management.Automation.Language.CommandAst] }, $true)) {
        $name = $call.GetCommandName()
        if (-not $name) { continue }
        $elements = @($call.CommandElements | Select-Object -Skip 1)
        $key = (($name -split '[\\/]')[-1] -replace '\.exe$', '').ToLowerInvariant()
        if ($PowerShellReaders.ContainsKey($key) -and $name -notmatch '\.exe$') {
            $reader = $PowerShellReaders[$key]
            $found += Get-PowerShellReaderTargets $reader $elements | ForEach-Object { @{ Reader = $reader; Path = $_ } }
            continue
        }
        $words = @($elements | ForEach-Object {
            if ($_ -is [System.Management.Automation.Language.CommandParameterAst]) { $_.Extent.Text }
            else { $v = @(Get-StaticValues $_); if ($v.Count -eq 1) { $v[0] } else { $null } } })
        $piped = $call.Parent -is [System.Management.Automation.Language.PipelineAst] -and
                 $call.Parent.PipelineElements.IndexOf($call) -gt 0
        if ($key -eq 'git') { $found += Get-GitGrepTargets $words; continue }
        if ($key -in $UnixReaders) {
            $found += Get-UnixReaderTargets $key $words $piped | ForEach-Object { @{ Reader = $key; Path = $_ } }
        }
    }
    return $found
}

function Get-Refusal([string] $Reader, [string] $Relative, [bool] $Folder) {
    $read = if ($Folder) {
        "Search it with ``nfi ask 'grep <regex> | files'`` — aimed at a feature (``node product:<slug> | owned | grep <regex>``) or a file (``grep <regex> --from file:<relpath> | source``)."
    } else {
        "Read it with ``nfi graph cat file:$Relative`` (or ``graph code <id>`` for one declaration); search it with ``nfi ask 'grep <regex> --from file:$Relative | source'``."
    }
    $what = if ($Relative -eq '.') { 'the checkout' } else { $Relative }
    "$Reader on $what reads repository source around nfi. CLAUDE.md: discovery goes through nfi, so every hit names its owning member and feature and several questions share one round trip. $read Ignored files (bin, obj, TestResults), the submodules under external/ and paths outside the checkout are not affected."
}

try {
    $hook = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $base = if ($hook.cwd) { $hook.cwd } else { (Get-Location).Path }
    $in = $hook.tool_input
    $targets = switch ($hook.tool_name) {
        'Read' {
            if ([IO.Path]::GetExtension("$($in.file_path)").ToLowerInvariant() -notin $BinaryExtensions) {
                , @{ Reader = 'The Read tool'; Path = $in.file_path } }
        }
        'Grep' { , @{ Reader = 'The Grep tool'; Path = $(if ($in.path) { $in.path } else { '.' }) } }
        'Glob' { , @{ Reader = 'The Glob tool'; Path = $(if ($in.path) { $in.path } else { '.' }) } }
        'PowerShell' { Get-PowerShellTargets $in.command }
        'Bash' { Get-ShellTargets $in.command $base }
    }
    foreach ($target in @($targets)) {
        if (-not $target) { continue }
        $full = Resolve-CallTarget $target.Path $(if ($target.Base) { $target.Base } else { $base })
        if (-not $full) { continue }
        $relative = Get-CheckoutRelativePath $full
        if (-not $relative) { continue }
        $folder = Test-Path -LiteralPath $full -PathType Container
        @{ hookSpecificOutput = @{
                hookEventName            = 'PreToolUse'
                permissionDecision       = 'deny'
                permissionDecisionReason = Get-Refusal $target.Reader $relative $folder } } | ConvertTo-Json -Compress -Depth 4
        exit 0
    }
} catch {
    [Console]::Error.WriteLine("require-nfi-reads: $($_.Exception.Message) — the call goes through unchecked")
}
exit 0
