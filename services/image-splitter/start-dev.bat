@echo off
:: Quick-start for development / testing (no Windows Service)
cd /d C:\Shared\NSCIM_PRODUCTION\services\image-splitter
echo Starting NSCIM Image Splitter (dev mode, port 5320)...
venv\Scripts\uvicorn main:app --host 0.0.0.0 --port 5320 --reload
