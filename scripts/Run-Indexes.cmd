@echo off
echo Creating Date Indexes for Memory Optimization
echo =============================================
echo.

echo 1. Creating indexes for ContainerScanQueues table...
sqlcmd -S "127.0.0.1,1433" -d "NS_CIS" -i "%~dp0Add_ContainerScanQueue_Date_Indexes.sql" -W
if %ERRORLEVEL% EQU 0 (
    echo    Indexes created successfully
) else (
    echo    Error creating indexes
)
echo.

echo 2. Creating indexes for all other tables...
sqlcmd -S "127.0.0.1,1433" -d "NS_CIS" -i "%~dp0Add_Date_Indexes_All_Tables.sql" -W
if %ERRORLEVEL% EQU 0 (
    echo    Indexes created successfully
) else (
    echo    Error creating indexes
)
echo.

echo Index creation complete!
pause

