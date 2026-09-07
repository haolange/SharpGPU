[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateSet('Build', 'Runtime')][string]$Gate = 'Build',
    [Parameter(Mandatory)][string]$DependencyRoot,
    [Parameter(Mandatory)][string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'This entry qualifies Windows x64 only.' }
$root = Split-Path $PSScriptRoot -Parent
$config = Get-Content -Raw (Join-Path $PSScriptRoot 'ci.json') | ConvertFrom-Json
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) { throw "Evidence directory already exists: $output" }
New-Item -ItemType Directory -Path $output | Out-Null
$savedEnvironment = @{}
function Invoke-DotNet([string[]]$Arguments)
{
    Write-Host ('dotnet ' + ($Arguments -join ' '))
    & dotnet @Arguments 2>&1 | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
Start-Transcript -Path "$output/verification.log" | Out-Null
try
{
    $roots = @{ $config.product = $root }
    foreach ($dependency in $config.dependencies.PSObject.Properties)
    {
        $path = [IO.Path]::GetFullPath((Join-Path $DependencyRoot $dependency.Name))
        if (-not (Test-Path -LiteralPath "$path/$($dependency.Name).product.props"))
        { throw "Missing dependency identity: $($dependency.Name)" }
        $head = & git -C $path rev-parse HEAD
        if ($LASTEXITCODE -ne 0 -or $head -ne $dependency.Value.commit)
        { throw "Dependency revision mismatch: $($dependency.Name)" }
        $roots[$dependency.Name] = $path
    }
    $xml = '<Project><PropertyGroup>'
    foreach ($name in $roots.Keys)
    {
        $xml += "<$($name)Root>$([Security.SecurityElement]::Escape($roots[$name]))</$($name)Root>"
        $variable = 'INFINITYSTACK_' + $name.ToUpperInvariant() + '_ROOT'
        $savedEnvironment[$variable] = [Environment]::GetEnvironmentVariable($variable)
        [Environment]::SetEnvironmentVariable($variable, $roots[$name])
    }
    $xml += '</PropertyGroup></Project>'
    $xml | Set-Content -LiteralPath "$output/stack.local.props" -Encoding utf8
    $props = @('-p:StackReferenceMode=Source', '-p:Platform=x64',
        "-p:StackLocalProps=$output/stack.local.props", "-p:StackProductRoot=$output/products",
        '-p:RestoreUseStaticGraphEvaluation=false', '-m:1', '-nr:false')
    foreach ($relative in $config.projects)
    {
        $project = Join-Path $root $relative
        Invoke-DotNet (@('restore', $project, '--locked-mode', "-p:Configuration=$Configuration") + $props)
        Invoke-DotNet (@('build', $project, '-c', $Configuration, '--no-restore') + $props)
    }
    if ($Gate -eq 'Runtime')
    {
        if ($config.product -eq 'SharpNeural')
        {
            $savedEnvironment['SHARPNEURAL_VULKAN_TARGET_FACE'] = [Environment]::GetEnvironmentVariable('SHARPNEURAL_VULKAN_TARGET_FACE')
            [Environment]::SetEnvironmentVariable('SHARPNEURAL_VULKAN_TARGET_FACE', '1')
        }
        foreach ($relative in $config.tests)
        {
            $project = Join-Path $root $relative
            $name = [IO.Path]::GetFileNameWithoutExtension($project)
            Invoke-DotNet (@('test', $project, '-c', $Configuration, '--no-build', '--no-restore',
                '--logger', 'trx', '--results-directory', "$output/results/$name") + $props)
        }
        $reports = @(Get-ChildItem -Path "$output/results" -Filter '*.trx' -Recurse)
        if ($reports.Count -ne $config.tests.Count) { throw 'Missing or duplicate test reports.' }
        foreach ($trx in $reports)
        {
            [xml]$report = Get-Content -Raw -LiteralPath $trx.FullName
            $counts = $report.TestRun.ResultSummary.Counters
            if ([int]$counts.total -le 0 -or [int]$counts.passed -ne [int]$counts.total)
            { throw "Runtime qualification requires all tests passed with no skips: $($trx.FullName)" }
        }
    }
    [ordered]@{ product=$config.product; configuration=$Configuration; sourceBuild='PASS';
        runtime=$(if ($Gate -eq 'Runtime') { 'PASS' } else { 'NOT_RUN: device qualification required' });
        packageConsumer='TODO: separate package gate'; dependencies=$config.dependencies
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$output/result.json" -Encoding utf8
}
finally
{
    foreach ($variable in $savedEnvironment.Keys)
    { [Environment]::SetEnvironmentVariable($variable, $savedEnvironment[$variable]) }
    Stop-Transcript | Out-Null
}
