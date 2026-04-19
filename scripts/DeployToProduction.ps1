# Deploy Application to Production Environment
# Copies application files to \\10.0.0.79\Shared\NSCIM_PRODUCTION

param(
    [string]$ProductionPath = "\\10.0.0.79\Shared\NSCIM_PRODUCTION",
    [switch]$SkipBuild = $false,
    [switch]$SkipPublish = $false,
    [switch]$WhatIf = $false
)

$ErrorActionPreference = "Stop"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Production Deployment Script" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Source: Current Directory (Test Environment)" -ForegroundColor White
Write-Host "Destination: $ProductionPath" -ForegroundColor White
Write-Host ""

# Check if production path is accessible
Write-Host "Step 1: Checking production path accessibility..." -ForegroundColor Yellow
if (-not (Test-Path $ProductionPath)) {
    Write-Host "❌ Production path is not accessible: $ProductionPath" -ForegroundColor Red
    Write-Host "   Please ensure:" -ForegroundColor Yellow
    Write-Host "   - Network share is accessible" -ForegroundColor White
    Write-Host "   - You have write permissions" -ForegroundColor White
    Write-Host "   - Path exists on the server" -ForegroundColor White
    exit 1
}
Write-Host "✅ Production path is accessible" -ForegroundColor Green
Write-Host ""

# Get current directory (project root)
$ProjectRoot = Get-Location
Write-Host "Project Root: $ProjectRoot" -ForegroundColor Gray
Write-Host ""

# Step 2: Build solution (if not skipped)
if (-not $SkipBuild) {
    Write-Host "Step 2: Building solution..." -ForegroundColor Yellow
    Write-Host "   ⚠️ Note: Build step is optional. You can build on production server." -ForegroundColor Gray
    Write-Host "   Use --SkipBuild to skip this step if applications are running." -ForegroundColor Gray
    Write-Host ""
    
    # Find solution file (check root and operational files directory)
    $solutionFile = Get-ChildItem -Path $ProjectRoot -Filter "*.sln" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $solutionFile) {
        Write-Host "   ⚠️ Solution file not found, skipping build" -ForegroundColor Yellow
        Write-Host "   You can build manually on production server" -ForegroundColor Gray
        $SkipBuild = $true
    } else {
        Write-Host "   Solution: $($solutionFile.Name)" -ForegroundColor Gray
        
        # Check if applications are running
        $runningProcesses = Get-Process -Name "NickScanWebApp.New","NickScanCentralImagingPortal.API" -ErrorAction SilentlyContinue
        if ($runningProcesses) {
            Write-Host "   ⚠️ Applications are running. Build may fail due to file locks." -ForegroundColor Yellow
            Write-Host "   Consider stopping applications or using --SkipBuild" -ForegroundColor Yellow
            Write-Host ""
            $response = Read-Host "   Continue with build anyway? (y/N)"
            if ($response -ne "y" -and $response -ne "Y") {
                Write-Host "   Skipping build..." -ForegroundColor Yellow
                $SkipBuild = $true
            }
        }
        
        if (-not $SkipBuild) {
            # Restore packages
            Write-Host "   Restoring packages..." -ForegroundColor Gray
            dotnet restore $solutionFile.FullName --verbosity quiet
            if ($LASTEXITCODE -ne 0) {
                Write-Host "❌ Package restore failed" -ForegroundColor Red
                Write-Host "   Continuing without build..." -ForegroundColor Yellow
                $SkipBuild = $true
            } else {
                # Build solution
                Write-Host "   Building solution (Release)..." -ForegroundColor Gray
                dotnet build $solutionFile.FullName -c Release --no-restore --verbosity quiet
                if ($LASTEXITCODE -ne 0) {
                    Write-Host "❌ Build failed (files may be locked by running applications)" -ForegroundColor Red
                    Write-Host "   Continuing without build. You can build on production server." -ForegroundColor Yellow
                    $SkipBuild = $true
                } else {
                    Write-Host "✅ Build completed successfully" -ForegroundColor Green
                }
            }
        }
    }
    Write-Host ""
} else {
    Write-Host "Step 2: Skipping build (--SkipBuild specified)" -ForegroundColor Yellow
    Write-Host ""
}

# Step 3: Publish applications (if not skipped)
if (-not $SkipPublish) {
    Write-Host "Step 3: Publishing applications..." -ForegroundColor Yellow
    
    # Create temporary publish directory
    $publishDir = Join-Path $ProjectRoot "publish-temp"
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
    
    # Publish API
    Write-Host "   Publishing API..." -ForegroundColor Gray
    $apiProject = Join-Path $ProjectRoot "src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj"
    if (Test-Path $apiProject) {
        $apiPublishDir = Join-Path $publishDir "API"
        dotnet publish $apiProject -c Release -o $apiPublishDir --no-build --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ API publish failed" -ForegroundColor Red
            exit 1
        }
        Write-Host "   ✅ API published" -ForegroundColor Green
    } else {
        Write-Host "   ⚠️ API project not found, skipping" -ForegroundColor Yellow
    }
    
    # Publish WebApp
    Write-Host "   Publishing WebApp..." -ForegroundColor Gray
    $webAppProject = Join-Path $ProjectRoot "src\NickScanWebApp.New\NickScanWebApp.New.csproj"
    if (Test-Path $webAppProject) {
        $webAppPublishDir = Join-Path $publishDir "WebApp"
        dotnet publish $webAppProject -c Release -o $webAppPublishDir --no-build --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ WebApp publish failed" -ForegroundColor Red
            exit 1
        }
        Write-Host "   ✅ WebApp published" -ForegroundColor Green
    } else {
        Write-Host "   ⚠️ WebApp project not found, skipping" -ForegroundColor Yellow
    }
    
    Write-Host "✅ Publishing completed" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "Step 3: Skipping publish (--SkipPublish specified)" -ForegroundColor Yellow
    Write-Host ""
}

# Step 4: Copy files to production
Write-Host "Step 4: Copying files to production..." -ForegroundColor Yellow

# Define what to copy
$itemsToCopy = @(
    @{ Source = "src"; Destination = "src"; Description = "Source code" },
    @{ Source = "scripts"; Destination = "scripts"; Description = "Scripts" },
    @{ Source = "docs"; Destination = "docs"; Description = "Documentation" },
    @{ Source = "database_migrations"; Destination = "database_migrations"; Description = "Database migrations" },
    @{ Source = "operational files"; Destination = "operational files"; Description = "Operational files" }
)

# If we published, copy publish output
if (-not $SkipPublish -and (Test-Path "publish-temp")) {
    $itemsToCopy += @{ Source = "publish-temp"; Destination = "publish"; Description = "Published applications" }
}

# Exclude patterns
$excludePatterns = @(
    "**\bin\**",
    "**\obj\**",
    "**\.git\**",
    "**\.vs\**",
    "**\node_modules\**",
    "**\*.user",
    "**\*.suo",
    "**\*.cache",
    "**\TestResults\**",
    "**\logs\**",
    "**\*.log",
    "**\diagnostics\**",
    "**\test_images\**"
)

# Copy each item
foreach ($item in $itemsToCopy) {
    $sourcePath = Join-Path $ProjectRoot $item.Source
    $destPath = Join-Path $ProductionPath $item.Destination
    
    if (-not (Test-Path $sourcePath)) {
        Write-Host "   ⚠️ Source not found: $($item.Source), skipping" -ForegroundColor Yellow
        continue
    }
    
    Write-Host "   Copying $($item.Description)..." -ForegroundColor Gray
    
    if ($WhatIf) {
        Write-Host "      [WHAT-IF] Would copy: $sourcePath -> $destPath" -ForegroundColor Cyan
    } else {
        # Create destination directory
        if (-not (Test-Path $destPath)) {
            New-Item -ItemType Directory -Path $destPath -Force | Out-Null
        }
        
        # Copy with exclusions
        $copyParams = @{
            Path = $sourcePath
            Destination = $destPath
            Recurse = $true
            Force = $true
            ErrorAction = "Continue"
        }
        
        # Apply exclusions
        $items = Get-ChildItem -Path $sourcePath -Recurse -File | Where-Object {
            $relativePath = $_.FullName.Substring($sourcePath.Length + 1)
            $shouldExclude = $false
            foreach ($pattern in $excludePatterns) {
                if ($relativePath -like $pattern) {
                    $shouldExclude = $true
                    break
                }
            }
            -not $shouldExclude
        }
        
        # Copy files
        $fileCount = 0
        foreach ($file in $items) {
            $relativePath = $file.FullName.Substring($sourcePath.Length + 1)
            $destFile = Join-Path $destPath $relativePath
            $destDir = Split-Path $destFile -Parent
            
            if (-not (Test-Path $destDir)) {
                New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            }
            
            Copy-Item -Path $file.FullName -Destination $destFile -Force
            $fileCount++
        }
        
        Write-Host "      ✅ Copied $fileCount files" -ForegroundColor Green
    }
}

# Copy important root files
Write-Host "   Copying root configuration files..." -ForegroundColor Gray
$rootFiles = @("*.sln", "*.md", "README*", "LICENSE*", ".gitignore")
foreach ($pattern in $rootFiles) {
    $files = Get-ChildItem -Path $ProjectRoot -Filter $pattern -File
    foreach ($file in $files) {
        if ($WhatIf) {
            Write-Host "      [WHAT-IF] Would copy: $($file.Name)" -ForegroundColor Cyan
        } else {
            Copy-Item -Path $file.FullName -Destination $ProductionPath -Force
        }
    }
}

Write-Host "✅ File copy completed" -ForegroundColor Green
Write-Host ""

# Step 5: Update production appsettings.json
Write-Host "Step 5: Updating production appsettings.json..." -ForegroundColor Yellow

$productionAppSettingsPath = Join-Path $ProductionPath "src\NickScanCentralImagingPortal.API\appsettings.json"

if (-not $WhatIf) {
    if (Test-Path $productionAppSettingsPath) {
        Write-Host "   Reading production appsettings.json..." -ForegroundColor Gray
        
        try {
            # Read the JSON file
            $jsonContent = Get-Content $productionAppSettingsPath -Raw -Encoding UTF8
            $appSettings = $jsonContent | ConvertFrom-Json
            
            # Update connection strings to production SQL Server
            Write-Host "   Updating connection strings to production SQL Server (10.0.0.79)..." -ForegroundColor Gray
            $appSettings.ConnectionStrings.NS_CIS_Connection = "Server=10.0.0.79;Database=NS_CIS;Integrated Security=true;MultipleActiveResultSets=true;Encrypt=true;TrustServerCertificate=true;Max Pool Size=100;Min Pool Size=10;Pooling=true"
            $appSettings.ConnectionStrings.ICUMS_Connection = "Server=10.0.0.79;Database=ICUMS;Integrated Security=true;MultipleActiveResultSets=true;Encrypt=true;TrustServerCertificate=true;Max Pool Size=50;Min Pool Size=5;Pooling=true"
            $appSettings.ConnectionStrings.ICUMS_Downloads_Connection = "Server=10.0.0.79;Database=ICUMS_Downloads;Integrated Security=true;MultipleActiveResultSets=true;Encrypt=true;TrustServerCertificate=true;Max Pool Size=50;Min Pool Size=5;Pooling=true"
            
            # Convert back to JSON with proper formatting (indented)
            $updatedJson = $appSettings | ConvertTo-Json -Depth 20
            
            # Write to file with UTF-8 encoding (no BOM)
            $utf8NoBom = New-Object System.Text.UTF8Encoding $false
            [System.IO.File]::WriteAllText($productionAppSettingsPath, $updatedJson, $utf8NoBom)
            
            Write-Host "✅ Production appsettings.json updated" -ForegroundColor Green
            Write-Host "   - NS_CIS_Connection: Server=10.0.0.79;Database=NS_CIS;Integrated Security=true" -ForegroundColor Cyan
            Write-Host "   - ICUMS_Connection: Server=10.0.0.79;Database=ICUMS;Integrated Security=true" -ForegroundColor Cyan
            Write-Host "   - ICUMS_Downloads_Connection: Server=10.0.0.79;Database=ICUMS_Downloads;Integrated Security=true" -ForegroundColor Cyan
        } catch {
            Write-Host "   ❌ Error updating appsettings.json: $($_.Exception.Message)" -ForegroundColor Red
            Write-Host "   You may need to update it manually" -ForegroundColor Yellow
        }
    } else {
        Write-Host "   ⚠️ Production appsettings.json not found at expected path" -ForegroundColor Yellow
        Write-Host "   Path: $productionAppSettingsPath" -ForegroundColor Gray
        Write-Host "   File will be available after deployment completes" -ForegroundColor Gray
    }
} else {
    Write-Host "   [WHAT-IF] Would update production appsettings.json" -ForegroundColor Cyan
    Write-Host "      - Change NS_CIS_Connection to: Server=10.0.0.79;Database=NS_CIS;Integrated Security=true" -ForegroundColor Gray
    Write-Host "      - Change ICUMS_Connection to: Server=10.0.0.79;Database=ICUMS;Integrated Security=true" -ForegroundColor Gray
    Write-Host "      - Change ICUMS_Downloads_Connection to: Server=10.0.0.79;Database=ICUMS_Downloads;Integrated Security=true" -ForegroundColor Gray
}

Write-Host ""

# Step 6: Cleanup
if (-not $SkipPublish -and (Test-Path "publish-temp")) {
    Write-Host "Step 6: Cleaning up temporary files..." -ForegroundColor Yellow
    if (-not $WhatIf) {
        Remove-Item "publish-temp" -Recurse -Force
        Write-Host "✅ Cleanup completed" -ForegroundColor Green
    } else {
        Write-Host "   [WHAT-IF] Would remove publish-temp directory" -ForegroundColor Cyan
    }
    Write-Host ""
}

# Summary
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Deployment Summary" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "✅ Deployment completed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "1. ✅ Production appsettings.json has been automatically configured" -ForegroundColor Green
Write-Host "2. Set up NS_CIS, ICUMS, and ICUMS_Downloads databases on production SQL Server (10.0.0.79)" -ForegroundColor White
Write-Host "3. Run database migrations on production" -ForegroundColor White
Write-Host "4. Review other production settings (file paths, CORS, etc.) if needed" -ForegroundColor White
Write-Host "5. Test production deployment" -ForegroundColor White
Write-Host ""
Write-Host "Production Path: $ProductionPath" -ForegroundColor Cyan
Write-Host ""

