# Where to Find Output Files

## 📍 Production Server Location
**Base Path:** `\\10.0.0.79\Shared\NSCIM_PRODUCTION`

---

## 🔨 Build Output

### API Build Output
```
\\10.0.0.79\Shared\NSCIM_PRODUCTION\src\NickScanCentralImagingPortal.API\bin\Debug\net8.0\
```
**Contains:**
- `NickScanCentralImagingPortal.API.dll` - Main API executable
- `NickScanCentralImagingPortal.API.exe` - Entry point
- All dependency DLLs
- `appsettings.json` - Configuration
- `nlog.config` - Logging configuration

### Frontend Build Output
```
\\10.0.0.79\Shared\NSCIM_PRODUCTION\src\NickScanWebApp.New\bin\Debug\net8.0\
```
**Contains:**
- `NickScanWebApp.New.dll` - Frontend executable
- `NickScanWebApp.New.exe` - Entry point
- All dependency DLLs
- `appsettings.json` - Configuration

### Release Build Output (if built with -c Release)
```
\\10.0.0.79\Shared\NSCIM_PRODUCTION\src\NickScanCentralImagingPortal.API\bin\Release\net8.0\
\\10.0.0.79\Shared\NSCIM_PRODUCTION\src\NickScanWebApp.New\bin\Release\net8.0\
```

---

## 📝 Application Logs

### API Logs
**Location:** Relative to where API runs from
```
\\10.0.0.79\Shared\NSCIM_PRODUCTION\src\NickScanCentralImagingPortal.API\logs\
```

**Log Files:**
- `nick-scan-api-{date}.log` - All API logs
- `errors-api-{date}.log` - Error logs only
- `structured-api-{date}.log` - Structured logs
- `nickscan-{date}.txt` - Serilog logs

**Example:**
```
\\10.0.0.79\Shared\NSCIM_PRODUCTION\src\NickScanCentralImagingPortal.API\logs\nick-scan-api-20260119.log
```

### Internal NLog Log
```
C:\temp\internal-nlog-AspNetCore.txt
```
(On the production server itself, not on the network share)

---

## 🚀 Published Output (if using publish)

### Published API
```
\\10.0.0.79\Shared\NSCIM_PRODUCTION\publish\API\
```
**Contains:**
- All compiled DLLs
- Configuration files
- Ready-to-deploy application

### Published Frontend
```
\\10.0.0.79\Shared\NSCIM_PRODUCTION\publish\WebApp\
```

---

## 🔍 How to Check Output

### Check Build Output
```powershell
cd \\10.0.0.79\Shared\NSCIM_PRODUCTION
Get-ChildItem -Recurse -Filter "*.dll" -Path "src\NickScanCentralImagingPortal.API\bin" | Select-Object FullName, LastWriteTime
```

### Check Logs
```powershell
cd \\10.0.0.79\Shared\NSCIM_PRODUCTION\src\NickScanCentralImagingPortal.API
Get-ChildItem logs -Filter "*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 5
```

### View Latest Log
```powershell
cd \\10.0.0.79\Shared\NSCIM_PRODUCTION\src\NickScanCentralImagingPortal.API
$today = Get-Date -Format "yyyyMMdd"
Get-Content "logs\nick-scan-api-$today.log" -Tail 50
```

### View Latest Errors
```powershell
cd \\10.0.0.79\Shared\NSCIM_PRODUCTION\src\NickScanCentralImagingPortal.API
$today = Get-Date -Format "yyyyMMdd"
Get-Content "logs\errors-api-$today.log" -Tail 50
```

---

## 📊 Script Output

### Build Script Output
When you run `Build-Production.ps1`, output appears in:
- **Console window** - Real-time build messages
- **Build output** - Goes to `bin\Debug\net8.0\` or `bin\Release\net8.0\`

### Diagnostic Script Output
When you run `Diagnose-BuildIssues.ps1`, output appears in:
- **Console window** - Diagnostic results

---

## 🎯 Quick Reference

| Type | Location |
|------|----------|
| **API DLLs** | `\\10.0.0.79\Shared\NSCIM_PRODUCTION\src\NickScanCentralImagingPortal.API\bin\Debug\net8.0\` |
| **Frontend DLLs** | `\\10.0.0.79\Shared\NSCIM_PRODUCTION\src\NickScanWebApp.New\bin\Debug\net8.0\` |
| **API Logs** | `\\10.0.0.79\Shared\NSCIM_PRODUCTION\src\NickScanCentralImagingPortal.API\logs\` |
| **Published Apps** | `\\10.0.0.79\Shared\NSCIM_PRODUCTION\publish\` |
| **Scripts** | `\\10.0.0.79\Shared\NSCIM_PRODUCTION\scripts\` |

---

## 💡 Tips

1. **Real-time Output**: When running `StartApplication.ps1`, watch the PowerShell windows for real-time output
2. **Log Rotation**: Logs are archived daily to `logs\archives\` folder
3. **Build Timestamps**: Check DLL `LastWriteTime` to verify latest build
4. **Error Logs**: Always check `errors-api-{date}.log` first for issues

