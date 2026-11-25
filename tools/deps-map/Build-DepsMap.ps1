<#
.SYNOPSIS
    Draws the project-reference map of the solution as one self-contained, interactive HTML page.

.DESCRIPTION
    Reads every .csproj under the scanned roots, resolves each <ProjectReference> to the project it names and
    writes tools/deps-map/out/deps-map.html (gitignored). Nothing is built and no network is needed, so it runs
    in a second and can be run at any time against whatever tree is checked out — a worktree reads its own.

    In the page: projects are layered (what depends on nothing above, the leaves below), coloured by group;
    big groups start collapsed — double-click one to open it. Click a project to see what it uses (blue) and
    what uses it (orange); "transitive" follows the chain all the way. Dashed red edges close a cycle.

    A <ProjectReference> that resolves outside the scanned roots (an external/ submodule, say) is drawn as an
    'external' node. Analyzer references (ReferenceOutputAssembly="false") are not dependencies of the code and
    are left out unless -IncludeAnalyzers is given.

    Groups come from the project name; add a line to $groupRules to give a family of projects its own box.

.EXAMPLE
    tools/deps-map/Build-DepsMap.ps1 -Open
    Writes the map and opens it in the default browser.

.EXAMPLE
    tools/deps-map/Build-DepsMap.ps1 -IncludeTests -Json
    Adds the test projects and writes deps-map.json (the raw nodes and edges) beside the page.
#>
[CmdletBinding()]
param(
    # Where to write the page. Defaults to tools/deps-map/out/deps-map.html.
    [string] $Output,

    # Directories, relative to the repository root, whose .csproj files are mapped.
    [string[]] $Roots = @('src', 'tools'),

    # Include the Nexaflow.Tests.* projects (they reference nearly everything and bury the shape).
    [switch] $IncludeTests,

    # Include analyzer-only references.
    [switch] $IncludeAnalyzers,

    # Also write the nodes and edges as deps-map.json beside the page.
    [switch] $Json,

    # Open the page when it is written.
    [switch] $Open
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Output) { $Output = Join-Path $PSScriptRoot 'out\deps-map.html' }
$template = Join-Path $PSScriptRoot 'deps-map.template.html'

# First match wins; a project no rule names is grouped by the segment after "Nexaflow.".
$groupRules = @(
    @{ Match = '^Nexaflow\.Tests\.';                                       Group = 'Tests' }
    @{ Match = '^Nexaflow\.Analyzers\.';                                   Group = 'Analyzers' }
    @{ Match = '^Nexaflow\.Features\.Common$';                             Group = 'Features.Common' }
    @{ Match = '^Nexaflow\.Features\.Compressed\.';                        Group = 'Codec backends' }
    @{ Match = '^Nexaflow\.Features\.';                                    Group = 'Features' }
    @{ Match = '^Nexaflow\.Providers\.(Common|OpenAICompatible|Local)$';   Group = 'Providers.Shared' }
    @{ Match = '^Nexaflow\.Providers\.';                                   Group = 'Providers' }
    @{ Match = '^Nexaflow\.(Elevation|PrivilegeBridge)';                   Group = 'Elevation' }
    @{ Match = '^Nexaflow\.Language\.';                                    Group = 'Languages' }
)

function Get-ProjectGroup([string] $name) {
    foreach ($rule in $groupRules) { if ($name -match $rule.Match) { return $rule.Group } }
    if ($name -match '^Nexaflow\.([^.]+)') { return $Matches[1] }
    return 'Other'
}

function Resolve-ReferencePath([string] $include, [string] $directory) {
    $relative = $include.Replace('$(MSBuildThisFileDirectory)', "$directory\").Replace('/', '\')
    if ($relative.Contains('$(')) { return $null }
    return [IO.Path]::GetFullPath((Join-Path $directory $relative))
}

# --- scan -----------------------------------------------------------------------------------------------------

$files = foreach ($root in $Roots) {
    $dir = Join-Path $repo $root
    if (Test-Path $dir) {
        Get-ChildItem -Path $dir -Recurse -Filter *.csproj |
            Where-Object { $_.FullName -notmatch '\\(bin|obj|node_modules)\\' }
    }
}
if (-not $files) { throw "no .csproj found under: $($Roots -join ', ')" }

$projects = foreach ($file in $files) {
    [xml] $xml = Get-Content -LiteralPath $file.FullName -Raw
    $references = foreach ($node in $xml.SelectNodes('//ProjectReference')) {
        $include = $node.GetAttribute('Include')
        if (-not $include) { continue }
        $analyzer = ($node.GetAttribute('ReferenceOutputAssembly') -eq 'false') -or ($node.GetAttribute('OutputItemType') -eq 'Analyzer')
        [pscustomobject]@{
            Include  = $include
            Path     = Resolve-ReferencePath $include $file.DirectoryName
            Analyzer = $analyzer
        }
    }
    [pscustomobject]@{
        Name       = $file.BaseName
        FullPath   = $file.FullName
        Relative   = $file.FullName.Substring($repo.Length + 1).Replace('\', '/')
        References = @($references)
    }
}

# A name shared by two projects is told apart by its path; everything else is just its name.
$nameCounts = @{}
foreach ($p in $projects) { $nameCounts[$p.Name] = 1 + [int] $nameCounts[$p.Name] }
$idByPath = @{}
foreach ($p in $projects) {
    $p | Add-Member -NotePropertyName Id -NotePropertyValue ($(if ($nameCounts[$p.Name] -gt 1) { $p.Relative } else { $p.Name }))
    $idByPath[$p.FullPath.ToLowerInvariant()] = $p.Id
}
$idByName = @{}
foreach ($p in $projects) { if ($nameCounts[$p.Name] -eq 1) { $idByName[$p.Name] = $p.Id } }

$nodes = [ordered]@{}
foreach ($p in $projects) {
    $group = Get-ProjectGroup $p.Name
    if ($group -eq 'Tests' -and -not $IncludeTests) { continue }
    $nodes[$p.Id] = [ordered]@{ id = $p.Id; group = $group; path = $p.Relative; external = $false }
}

$edges = [System.Collections.Generic.HashSet[string]]::new()
$edgeList = [System.Collections.Generic.List[object]]::new()
foreach ($p in $projects) {
    if (-not $nodes.Contains($p.Id)) { continue }
    foreach ($ref in $p.References) {
        if ($ref.Analyzer -and -not $IncludeAnalyzers) { continue }

        $targetId = $null
        if ($ref.Path) { $targetId = $idByPath[$ref.Path.ToLowerInvariant()] }
        if (-not $targetId) {
            $refName = [IO.Path]::GetFileNameWithoutExtension($ref.Include)
            $targetId = $idByName[$refName]
            if (-not $targetId) {
                # Not under a scanned root: an external/ submodule or a project somewhere else.
                $targetId = $refName
                if (-not $nodes.Contains($targetId)) {
                    $where = if ($ref.Path -and $ref.Path.StartsWith($repo, 'OrdinalIgnoreCase')) { $ref.Path.Substring($repo.Length + 1).Replace('\', '/') } else { $ref.Include.Replace('\', '/') }
                    $nodes[$targetId] = [ordered]@{ id = $targetId; group = 'external'; path = $where; external = $true }
                }
            }
        }
        if ($targetId -eq $p.Id -or -not $nodes.Contains($targetId)) { continue }
        if ($edges.Add("$($p.Id)`n$targetId")) { $edgeList.Add([ordered]@{ from = $p.Id; to = $targetId }) }
    }
}

# --- write ----------------------------------------------------------------------------------------------------

$commit = try { (git -C $repo rev-parse --short HEAD 2>$null) } catch { $null }
$branch = try { (git -C $repo rev-parse --abbrev-ref HEAD 2>$null) } catch { $null }

$model = [ordered]@{
    generated        = (Get-Date).ToString('yyyy-MM-dd HH:mm')
    commit           = "$commit"
    branch           = "$branch"
    includesTests    = [bool] $IncludeTests
    includesAnalyzers = [bool] $IncludeAnalyzers
    nodes            = @($nodes.Values)
    edges            = @($edgeList)
}
$data = ($model | ConvertTo-Json -Depth 5 -Compress).Replace('</', '<\/')

$outDir = Split-Path -Parent $Output
if ($outDir) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

$page = (Get-Content -LiteralPath $template -Raw).Replace('__DEPS_DATA__', $data)
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($Output, $page, $utf8)

if ($Json) {
    $jsonPath = [IO.Path]::ChangeExtension($Output, '.json')
    [IO.File]::WriteAllText($jsonPath, ($model | ConvertTo-Json -Depth 5), $utf8)
    Write-Host "json: $jsonPath"
}

$groups = $nodes.Values | Group-Object { $_.group } | Sort-Object Count -Descending
Write-Host ("{0} projects, {1} references" -f $nodes.Count, $edgeList.Count) -ForegroundColor Cyan
Write-Host (($groups | ForEach-Object { "$($_.Name) $($_.Count)" }) -join '  ·  ')
Write-Host "map: $Output"

if ($Open) { Invoke-Item -LiteralPath $Output }
