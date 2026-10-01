[CmdletBinding()]
param(
    [switch]$Instantiation
)

# HistoryJuno 自身的只读项目合同检查。
#
# 体系里那份跨仓共享规则（HistoryDiana 的 OneHistory.ModuleContract.ps1）当前不在库中，
# 转发过去的项目会直接 exit 2。本仓因此自带规则，只检查对本模块真正成立的事：
# 版本三处一致、模块身份、目录级别、跨仓引用与本地链接。
#
# 本脚本只读，不提交、不推送、不发布、不改外部系统。

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$errors = [Collections.Generic.List[string]]::new()

function Require-File([string]$RelativePath) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $RelativePath) -PathType Leaf)) {
        $errors.Add("缺少必需文件: $RelativePath")
    }
}

function Require-Directory([string]$RelativePath) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $RelativePath) -PathType Container)) {
        $errors.Add("缺少必需目录: $RelativePath")
    }
}

try {
    $manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'project.manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
}
catch {
    throw "无法读取 project.manifest.json: $($_.Exception.Message)"
}

foreach ($field in @('id', 'name', 'title', 'status', 'version', 'branch')) {
    if ([string]::IsNullOrWhiteSpace([string]$manifest.project.$field)) {
        $errors.Add("project.manifest.json 缺少 project.$field")
    }
}
if ($manifest.project.name -ne 'HistoryJuno') {
    $errors.Add('project.name 必须为 HistoryJuno')
}
if ($manifest.template.isTemplate) {
    $errors.Add('派生项目的 template.isTemplate 必须为 false')
}

Require-File 'README.md'
Require-File 'AGENTS.md'
Require-File 'Logo.png'
foreach ($property in $manifest.documents.PSObject.Properties) {
    Require-File $property.Value
}
foreach ($directory in @($manifest.paths.activeRoots)) {
    Require-Directory $directory
}

# 根目录可见文件夹只允许 a-/b-/z- 三个级别。
$allowedPrefixes = @($manifest.paths.allowedRootDirectoryPrefixes)
Get-ChildItem -LiteralPath $repoRoot -Directory |
    Where-Object { -not $_.Name.StartsWith('.') } |
    ForEach-Object {
        $name = $_.Name
        if (-not ($allowedPrefixes | Where-Object { $name.StartsWith($_) })) {
            $errors.Add("根目录出现无级别文件夹: $name")
        }
    }

# 版本三处一致。宿主对版本漂移的处理是静默跳过整个模块，只在日志留一行警告，
# 所以这条必须是构建前的硬失败，而不是发布后的现场排查。
$componentRoot = Join-Path $repoRoot 'b-Code\HistoryJuno'
$versionProps = [xml](Get-Content -LiteralPath (Join-Path $componentRoot 'HistoryJunoVersion.props') -Raw -Encoding UTF8)
$moduleVersion = [string]$versionProps.Project.PropertyGroup.HistoryJunoVersion
$moduleManifest = Get-Content -LiteralPath (Join-Path $componentRoot 'module.manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.project.version -ne $moduleVersion -or $moduleManifest.version -ne $moduleVersion) {
    $errors.Add("版本必须三处一致：project.manifest=$($manifest.project.version) props=$moduleVersion module.manifest=$($moduleManifest.version)")
}
if ($moduleManifest.name -ne 'HistoryJuno' -or $moduleManifest.type -ne 'HistoryVulcan.Module') {
    $errors.Add('模块 manifest 身份无效')
}
if ($moduleManifest.artifact -ne 'HistoryJuno.dll') {
    $errors.Add('模块 manifest 的 artifact 必须是 HistoryJuno.dll')
}

$moduleInfoSource = Get-Content -LiteralPath (Join-Path $componentRoot 'ModuleInfo.cs') -Raw -Encoding UTF8
if ($moduleInfoSource -notmatch 'ModuleName\s*=>\s*"HistoryJuno"') {
    $errors.Add('ModuleInfo.ModuleName 必须显式返回 HistoryJuno：缺省值是程序集名，同时也是指令域')
}

$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'b-Code') -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(?:bin|obj)\\' }
foreach ($source in $sourceFiles) {
    $content = Get-Content -LiteralPath $source.FullName -Raw -Encoding UTF8
    if ($content -match 'context\.Settings|context\.Log|context\.DataDirectory') {
        $errors.Add("模块不得读取已移除的 IModuleContext 成员: $($source.FullName)")
    }
}

foreach ($projectFile in Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Filter '*.csproj') {
    $content = Get-Content -LiteralPath $projectFile.FullName -Raw -Encoding UTF8
    if ($content -match '<ProjectReference[^>]+(?:\.\.\\|\.\./)2026-') {
        $errors.Add("不得跨仓 ProjectReference: $($projectFile.FullName)")
    }
}

# 现行文档里的本地 Markdown 链接必须指向存在的文件。
foreach ($document in Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Filter '*.md' |
    Where-Object { $_.FullName -notmatch '\\(?:bin|obj|z-Publish)\\' }) {
    $content = Get-Content -LiteralPath $document.FullName -Raw -Encoding UTF8
    foreach ($match in [regex]::Matches($content, '\]\((?<target>[^)#:]+)(?:#[^)]*)?\)')) {
        $target = $match.Groups['target'].Value.Trim()
        if ($target.StartsWith('http') -or $target.StartsWith('mailto:')) { continue }
        $resolved = Join-Path (Split-Path -Parent $document.FullName) $target
        if (-not (Test-Path -LiteralPath $resolved)) {
            $errors.Add("失效的本地链接: $($document.FullName) → $target")
        }
    }
}

if ($Instantiation) {
    $textFiles = Get-ChildItem -LiteralPath $repoRoot -Recurse -File |
        Where-Object { $_.Extension -in @('.md', '.json', '.props', '.csproj') } |
        Where-Object { $_.FullName -notmatch '\\(?:bin|obj|z-Publish)\\' -and $_.Name -ne 'AGENTS.md' }
    foreach ($file in $textFiles) {
        if ((Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8) -match '\{\{.+?\}\}') {
            $errors.Add("存在未实例化占位符: $($file.FullName)")
        }
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "HistoryJuno project contract: PASS ($moduleVersion; Vulcan 6.0.0)"
