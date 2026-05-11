param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot
try {
    $expectedSdkVersion = "10.0.202"
    $sdkVersion = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to determine dotnet SDK version."
    }

    if ($sdkVersion -ne $expectedSdkVersion) {
        throw "Expected .NET SDK $expectedSdkVersion from global.json, but dotnet resolved $sdkVersion."
    }

    $expectedProjects = @(
        "src\NickScanCentralImagingPortal.Core\NickScanCentralImagingPortal.Core.csproj",
        "src\NickScanCentralImagingPortal.Infrastructure\NickScanCentralImagingPortal.Infrastructure.csproj",
        "src\NickScanCentralImagingPortal.ScannerServices.ASE\NickScanCentralImagingPortal.ScannerServices.ASE.csproj",
        "src\NickScanCentralImagingPortal.ScannerServices.HeimannSmith\NickScanCentralImagingPortal.ScannerServices.HeimannSmith.csproj",
        "src\NickScanCentralImagingPortal.ScannerServices.Nuctech\NickScanCentralImagingPortal.ScannerServices.Nuctech.csproj",
        "src\NickScanCentralImagingPortal.Services.FS6000\NickScanCentralImagingPortal.Services.FS6000.csproj",
        "src\NickScanCentralImagingPortal.Services.ImageProcessing\NickScanCentralImagingPortal.Services.ImageProcessing.csproj",
        "src\NickScanCentralImagingPortal.Services\NickScanCentralImagingPortal.Services.csproj",
        "src\NickScanWebApp.Shared\NickScanWebApp.Shared.csproj",
        "src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj",
        "src\NickScanWebApp.New\NickScanWebApp.New.csproj",
        "tests\NickScanCentralImagingPortal.Core.Tests\NickScanCentralImagingPortal.Core.Tests.csproj",
        "tests\NickScanCentralImagingPortal.Integration.Tests\NickScanCentralImagingPortal.Integration.Tests.csproj",
        "src\NickScanCentralImagingPortal.Tests\NickScanCentralImagingPortal.Tests.csproj"
    )

    $buildProjects = @(
        "src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj",
        "src\NickScanWebApp.New\NickScanWebApp.New.csproj",
        "tests\NickScanCentralImagingPortal.Core.Tests\NickScanCentralImagingPortal.Core.Tests.csproj",
        "tests\NickScanCentralImagingPortal.Integration.Tests\NickScanCentralImagingPortal.Integration.Tests.csproj",
        "src\NickScanCentralImagingPortal.Tests\NickScanCentralImagingPortal.Tests.csproj"
    )

    $testProjects = @(
        "tests\NickScanCentralImagingPortal.Core.Tests\NickScanCentralImagingPortal.Core.Tests.csproj",
        "tests\NickScanCentralImagingPortal.Integration.Tests\NickScanCentralImagingPortal.Integration.Tests.csproj",
        "src\NickScanCentralImagingPortal.Tests\NickScanCentralImagingPortal.Tests.csproj"
    )

    foreach ($project in $expectedProjects) {
        if (-not (Test-Path $project)) {
            throw "Expected project not found: $project"
        }
    }

    $solutionProjects = @(dotnet sln NickscanERP.sln list | Where-Object { $_ -and $_ -notmatch "Project\(s\)|----------" })
    foreach ($project in $expectedProjects) {
        if ($solutionProjects -notcontains $project) {
            throw "Expected project is missing from NickscanERP.sln: $project"
        }
    }

    Write-Host "Using .NET SDK $sdkVersion"
    Write-Host "Restoring imaging projects..."
    foreach ($project in $buildProjects) {
        dotnet restore $project --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            throw "Restore failed for $project."
        }
    }

    Write-Host "Building imaging projects ($Configuration)..."
    foreach ($project in $buildProjects) {
        Write-Host "  $project"
        dotnet build $project --configuration $Configuration --no-restore --no-incremental /m:1 /p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed for $project."
        }
    }

    if (-not $SkipTests) {
        Write-Host "Running imaging tests ($Configuration)..."
        foreach ($project in $testProjects) {
            Write-Host "  $project"
            dotnet test $project --configuration $Configuration --no-restore --no-build /p:UseSharedCompilation=false
            if ($LASTEXITCODE -ne 0) {
                throw "Tests failed for $project."
            }
        }
    } else {
        Write-Host "Skipping test execution because -SkipTests was supplied."
    }

    Write-Host "Imaging build verification completed."
}
finally {
    Pop-Location
}
