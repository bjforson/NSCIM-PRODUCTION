@echo off
:: Quick-start for development / testing (no Windows Service)
cd /d C:\Shared\NSCIM_PRODUCTION\services\image-splitter
if "%SPLITTER_HOST%"=="" set "SPLITTER_HOST=127.0.0.1"
echo Starting NSCIM Image Splitter (dev mode, %SPLITTER_HOST%:5320)...
venv\Scripts\uvicorn main:app --host "%SPLITTER_HOST%" --port 5320 --reload
