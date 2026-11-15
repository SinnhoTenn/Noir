#requires -Version 5.1
<#
    Builds NOIR with the C# compiler that ships with Windows/.NET Framework.
    No SDK, NuGet package, or network access is required.

    Usage:
        powershell -ExecutionPolicy Bypass -File .\build.ps1
        powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test
#>
[CmdletBinding()]
param(
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path (Join-Path $frameworkDir 'csc.exe'))) {
    $frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}

$csc = Join-Path $frameworkDir 'csc.exe'
if (-not (Test-Path $csc)) {
    throw 'The .NET Framework C# compiler (csc.exe) was not found.'
}

function Find-GacAssembly {
    param([Parameter(Mandatory = $true)][string]$Name)

    $assemblyRoot = Join-Path $env:WINDIR 'Microsoft.NET\assembly'
    $candidates = @(
        (Join-Path $assemblyRoot "GAC_MSIL\$Name"),
        (Join-Path $assemblyRoot "GAC_64\$Name"),
        (Join-Path $assemblyRoot "GAC_32\$Name"),
        (Join-Path $env:WINDIR "assembly\GAC_MSIL\$Name"),
        (Join-Path $env:WINDIR "assembly\GAC_64\$Name"),
        (Join-Path $env:WINDIR "assembly\GAC_32\$Name")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            $found = Get-ChildItem -Path $candidate -Recurse -Filter "$Name.dll" -ErrorAction SilentlyContinue |
                Select-Object -First 1
            if ($found) { return $found.FullName }
        }
    }

    throw "Could not locate $Name.dll in the .NET Framework GAC."
}

$presentationFramework = Find-GacAssembly 'PresentationFramework'
$presentationCore = Find-GacAssembly 'PresentationCore'
$windowsBase = Find-GacAssembly 'WindowsBase'
$systemXaml = Find-GacAssembly 'System.Xaml'

$wpfReferences = @(
    "/r:$presentationFramework",
    "/r:$presentationCore",
    "/r:$windowsBase",
    "/r:$systemXaml",
    "/r:$(Join-Path $frameworkDir 'System.dll')",
    "/r:$(Join-Path $frameworkDir 'System.Core.dll')"
)

$appArguments = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    '/platform:anycpu',
    "/out:$root\Noir.exe",
    "/win32icon:$root\assets\noir.ico",
    "/win32manifest:$root\src\app.manifest"
) + $wpfReferences + @(
    "$root\src\AssemblyInfo.cs",
    "$root\src\NoirCipher.cs",
    "$root\src\NoirApp.cs"
)

Write-Host 'Building Noir.exe ...'
& $csc $appArguments
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }

$testArguments = @(
    '/nologo',
    '/target:exe',
    '/optimize+',
    '/platform:anycpu',
    "/out:$root\tests\NoirTests.exe",
    "$root\src\NoirCipher.cs",
    "$root\src\NoirSelfTest.cs"
)

Write-Host 'Building NoirTests.exe ...'
& $csc $testArguments
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }

Write-Host "Built: $root\Noir.exe"
Write-Host "Tests: $root\tests\NoirTests.exe"

if ($Test) {
    Write-Host ''
    & "$root\tests\NoirTests.exe"
    exit $LASTEXITCODE
}
