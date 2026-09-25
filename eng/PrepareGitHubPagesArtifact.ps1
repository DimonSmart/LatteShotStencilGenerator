[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PublishDirectory,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ArtifactDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9-]*$')]
    [string]$RepositorySubpath
)

$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path
$indexPath = Join-Path $publishPath 'index.html'
$frameworkLoaderPath = Join-Path $publishPath '_framework/blazor.webassembly.js'

if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
    throw "Published output does not contain index.html: $publishPath"
}

if (-not (Test-Path -LiteralPath $frameworkLoaderPath -PathType Leaf)) {
    throw "Published output does not contain the Blazor framework loader: $frameworkLoaderPath"
}

$index = Get-Content -LiteralPath $indexPath -Raw
$rootBaseHref = '<base href="/" />'
if ($index -notmatch [regex]::Escape($rootBaseHref)) {
    throw 'Published index.html must declare the root base path before GitHub Pages preparation.'
}

$pagesBaseHref = '<base href="/{0}/" />' -f $RepositorySubpath
$index = $index.Replace($rootBaseHref, $pagesBaseHref)
$withoutBaseElement = [regex]::Replace($index, '<base\s+[^>]*>', '', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
$rootBoundAsset = [regex]::Match($withoutBaseElement, '(?:src|href)\s*=\s*["'']/(?!/)')
if ($rootBoundAsset.Success) {
    throw "Published index.html contains a root-bound asset URL: $($rootBoundAsset.Value)"
}

if (Test-Path -LiteralPath $ArtifactDirectory) {
    Remove-Item -LiteralPath $ArtifactDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $publishPath '*') -Destination $ArtifactDirectory -Recurse -Force
$artifactIndexPath = Join-Path $ArtifactDirectory 'index.html'
Set-Content -LiteralPath $artifactIndexPath -Value $index -NoNewline
Copy-Item -LiteralPath $artifactIndexPath -Destination (Join-Path $ArtifactDirectory '404.html') -Force
New-Item -ItemType File -Path (Join-Path $ArtifactDirectory '.nojekyll') -Force | Out-Null

$indexHash = (Get-FileHash -LiteralPath (Join-Path $ArtifactDirectory 'index.html') -Algorithm SHA256).Hash
$fallbackHash = (Get-FileHash -LiteralPath (Join-Path $ArtifactDirectory '404.html') -Algorithm SHA256).Hash
if ($indexHash -ne $fallbackHash) {
    throw 'The GitHub Pages SPA fallback must match index.html.'
}

if (-not (Test-Path -LiteralPath (Join-Path $ArtifactDirectory '.nojekyll') -PathType Leaf)) {
    throw 'The GitHub Pages artifact must include .nojekyll.'
}
