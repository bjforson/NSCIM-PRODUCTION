# Activate Claude Vision for AI image analysis
# Usage: .\activate-claude-vision.ps1 -ApiKey "sk-ant-api03-..."
#
# This sets the Anthropic API key as a machine environment variable
# and updates the AI workflow config to use claude-vision provider.
# Restart the NSCIM_API service after running this script.

param(
    [Parameter(Mandatory=$true)]
    [string]$ApiKey,
    [string]$Model = "claude-sonnet-4-20250514"
)

Write-Host "=== Activating Claude Vision ===" -ForegroundColor Cyan

# Set environment variable (machine-level, persists across reboots)
[System.Environment]::SetEnvironmentVariable("NICKSCAN_AI_CLAUDE_API_KEY", $ApiKey, "Machine")
Write-Host "Set NICKSCAN_AI_CLAUDE_API_KEY environment variable" -ForegroundColor Green

# Update appsettings in both locations
$configPaths = @(
    "C:\Shared\NSCIM_PRODUCTION\src\NickScanCentralImagingPortal.API\appsettings.json",
    "C:\Shared\NSCIM_PRODUCTION\deploy\api\appsettings.json"
)

foreach ($path in $configPaths) {
    if (Test-Path $path) {
        $json = Get-Content $path -Raw | ConvertFrom-Json
        if ($json.AiWorkflow) {
            $json.AiWorkflow.ActiveProvider = "claude-vision"
            $json.AiWorkflow.ClaudeApiKey = "***USE_ENV_VAR_NICKSCAN_AI_CLAUDE_API_KEY***"
            $json.AiWorkflow.ClaudeModelId = $Model
            $json | ConvertTo-Json -Depth 10 | Set-Content $path
            Write-Host "Updated: $path" -ForegroundColor Green
        }
    }
}

Write-Host ""
Write-Host "=== Next Steps ===" -ForegroundColor Yellow
Write-Host "1. Restart NSCIM_API service: sc stop NSCIM_API && sc start NSCIM_API"
Write-Host "2. Test: POST /api/aiworkflow/image/suggestions/generate with a group ID"
Write-Host "3. Monitor: /operations/ai-shadow-dashboard"
Write-Host ""
Write-Host "To deactivate: Set ActiveProvider back to 'stub' in appsettings.json" -ForegroundColor Yellow
