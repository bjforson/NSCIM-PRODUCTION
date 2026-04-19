@echo off
:: ============================================================
:: NSCIM Raw Image Engine — Windows Service Installer
:: Registers the Python image processing service as a Windows
:: Service using NSSM (Non-Sucking Service Manager)
::
:: Capabilities:
::   - ASE/FS6000 raw image decoding (16-bit)
::   - X-ray Inspector rendering and analysis
::   - Dual-container image splitting (8 strategies + Claude Vision)
::
:: Requirements:
::   - NSSM installed (download from nssm.cc)
::   - Python venv already created at .\venv\
::   - .env file with NICKSCAN_DB_PASSWORD set
::
:: Run as Administrator
:: ============================================================

SET SERVICE_NAME=NSCIM_RawImageEngine
SET SERVICE_DISPLAY=NSCIM Raw Image Engine
SET SERVICE_DESC=Raw X-ray image decoding, 16-bit rendering, analysis tools, and dual-container splitting
SET WORK_DIR=C:\Shared\NSCIM_PRODUCTION\services\image-splitter
SET PYTHON=%WORK_DIR%\venv\Scripts\python.exe
SET LOG_DIR=%WORK_DIR%\logs

:: Also remove old service name if it exists
SET OLD_SERVICE_NAME=NSCIM_ImageSplitter

:: Check for admin rights
net session >nul 2>&1
if errorlevel 1 (
    echo ERROR: This script must be run as Administrator
    pause
    exit /b 1
)

:: Check for NSSM
where nssm >nul 2>&1
if errorlevel 1 (
    echo ERROR: NSSM not found in PATH.
    echo Download from: https://nssm.cc/download
    echo Extract nssm.exe to C:\Windows\System32\ or add to PATH
    pause
    exit /b 1
)

echo Creating log directory...
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"

echo Removing old service name (if any)...
nssm stop %OLD_SERVICE_NAME% 2>nul
nssm remove %OLD_SERVICE_NAME% confirm 2>nul

echo Removing existing service (if any)...
nssm stop %SERVICE_NAME% 2>nul
nssm remove %SERVICE_NAME% confirm 2>nul

echo Installing service: %SERVICE_NAME%...
nssm install %SERVICE_NAME% "%PYTHON%" "-m" "uvicorn" "main:app" "--host" "0.0.0.0" "--port" "5320"

nssm set %SERVICE_NAME% DisplayName "%SERVICE_DISPLAY%"
nssm set %SERVICE_NAME% Description "%SERVICE_DESC%"
nssm set %SERVICE_NAME% AppDirectory "%WORK_DIR%"

:: Redirect stdout/stderr to log files
nssm set %SERVICE_NAME% AppStdout "%LOG_DIR%\engine.log"
nssm set %SERVICE_NAME% AppStderr "%LOG_DIR%\engine-error.log"
nssm set %SERVICE_NAME% AppRotateFiles 1
nssm set %SERVICE_NAME% AppRotateSeconds 86400
nssm set %SERVICE_NAME% AppRotateBytes 10485760

:: Depend on NSCIM_API (not WebApp — API must start first)
nssm set %SERVICE_NAME% DependOnService NSCIM_API

:: Restart on failure with throttle
nssm set %SERVICE_NAME% AppExit Default Restart
nssm set %SERVICE_NAME% AppRestartDelay 3000
nssm set %SERVICE_NAME% AppThrottle 5000

:: Startup type: Automatic (delayed)
nssm set %SERVICE_NAME% Start SERVICE_DELAYED_AUTO_START

echo Starting service...
nssm start %SERVICE_NAME%

echo.
echo ============================================================
echo Service installed: %SERVICE_NAME%
echo Display name:      %SERVICE_DISPLAY%
echo Status:
sc query %SERVICE_NAME%
echo.
echo Manage with:
echo   nssm start   %SERVICE_NAME%
echo   nssm stop    %SERVICE_NAME%
echo   nssm restart %SERVICE_NAME%
echo   nssm edit    %SERVICE_NAME%
echo ============================================================
pause
