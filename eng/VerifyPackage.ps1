[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [Parameter(Mandatory)][string]$PackageFeed,
    [Parameter(Mandatory)][string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'This gate qualifies Windows x64 package execution.' }
$root = Split-Path $PSScriptRoot -Parent
$config = Get-Content -Raw "$PSScriptRoot/package-ci.json" | ConvertFrom-Json
$feed = (Resolve-Path -LiteralPath $PackageFeed).Path
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) { throw "Evidence directory already exists: $output" }
New-Item -ItemType Directory -Path $output | Out-Null
function Invoke-DotNet([string[]]$Arguments, [string]$LogName)
{
    Write-Host ('dotnet ' + ($Arguments -join ' '))
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath "$output/$LogName.log" | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $LASTEXITCODE" }
}
Start-Transcript -Path "$output/verification.log" | Out-Null
try
{
    $consumer = "$output/isolated consumer"
    New-Item -ItemType Directory -Path $consumer | Out-Null
    Copy-Item -Path (Join-Path $root "$($config.sample)/*.cs") -Destination $consumer
    '<Project />' | Set-Content "$consumer/Directory.Build.props" -Encoding utf8
    '<Project />' | Set-Content "$consumer/Directory.Build.targets" -Encoding utf8
    $project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><AllowUnsafeBlocks>true</AllowUnsafeBlocks><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup><ItemGroup>'
    foreach ($package in $config.packages)
    { $project += "<PackageReference Include=`"$package`" Version=`"$($config.version)`" />" }
    $project += '</ItemGroup></Project>'
    $project | Set-Content "$consumer/Consumer.csproj" -Encoding utf8
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    "<configuration><packageSources><clear/><add key=`"explicit`" value=`"$escapedFeed`"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>" |
        Set-Content "$consumer/NuGet.Config" -Encoding utf8
    Invoke-DotNet @('restore', "$consumer/Consumer.csproj", '--configfile', "$consumer/NuGet.Config", '--packages', "$output/package-cache") 'restore'
    Invoke-DotNet @('restore', "$consumer/Consumer.csproj", '--locked-mode', '--configfile', "$consumer/NuGet.Config", '--packages', "$output/package-cache") 'restore-locked'
    $assets = Get-Content -Raw "$consumer/obj/project.assets.json" | ConvertFrom-Json
    $hashes = @()
    foreach ($library in $assets.libraries.PSObject.Properties)
    {
        if ($library.Value.type -ne 'package') { throw "Non-package dependency: $($library.Name)" }
        $parts = $library.Name.Split('/')
        $name = "$($parts[0]).$($parts[1]).nupkg"
        $original = Join-Path $feed $name
        $cached = Join-Path "$output/package-cache" "$($library.Value.path)/$($name.ToLowerInvariant())"
        $originalHash = (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash
        if ((Get-FileHash -LiteralPath $cached -Algorithm SHA256).Hash -ne $originalHash)
        { throw "Cached package does not match explicit feed: $name" }
        $hashes += [ordered]@{ package=$library.Name; sha256=$originalHash }
    }
    $hashes | ConvertTo-Json | Set-Content "$output/packages.json" -Encoding utf8
    Invoke-DotNet @('build', "$consumer/Consumer.csproj", '-c', $Configuration, '--no-restore') 'build'
    Push-Location ([IO.Path]::GetTempPath())
    try
    {
        $index = 0
        foreach ($run in $config.runs)
        {
            $logName = "run-$index"
            Invoke-DotNet (@("$consumer/bin/$Configuration/net10.0/Consumer.dll") + @($run.arguments)) $logName
            $log = Get-Content -Raw "$output/$logName.log"
            foreach ($required in $run.requiredOutput)
            { if (-not $log.Contains($required)) { throw "Missing required workload result: $required" } }
            $index++
        }
    }
    finally { Pop-Location }
    [ordered]@{ product=$config.product; configuration=$Configuration; packageConsumer='PASS';
        packageCount=$hashes.Count; sourceProjects=0; sourceRevisionAlignment='NOT_ASSERTED: explicit feed qualification'
    } | ConvertTo-Json | Set-Content "$output/result.json" -Encoding utf8
}
finally { Stop-Transcript | Out-Null }
