# ICUMS Configuration Keys - Understanding

## 📋 Configuration Structure

The ICUMS configuration uses **3 different authentication keys** and **5 different interface keys** for different API endpoints:

### 🔑 Authentication Keys (3 types)

1. **`ICUMS:AuthKey`** 
   - **Value**: `e80b69d843b14ddca6e8a398eb6c3bb2f587d21126e643b9a31b65b6f7740675`
   - **Used for**: Main BOE data fetching (batch and container)
   - **Endpoints**: 
     - Batch fetch (`IF_P01_NSCUNI_04`)
     - Container fetch (`IF_P01_NSCUNI_05`)
     - Submit results (`IF_P01_NSCUNI_02`)
     - Read status (`IF_P01_NSCUNI_08`)

2. **`ICUMS:DocumentsAuthKey`**
   - **Value**: `e80b69d843b14ddca6e8a398eb6c3bb2f587d21126e643b9a31b65b6f7740675` (same as AuthKey)
   - **Used for**: Fetching BOE attached documents
   - **Endpoint**: Document fetch (`IF_P01_NSCUNI_09`)
   - **URL**: `https://esb.unipassghana.com:26004/api/rm/scan/boeAttDocData?DeclarationNumber={0}`

3. **`ICUMS:JsonDocumentsAuthKey`**
   - **Value**: `59d0cd35350c4a17a97f54c985d067735030e4addf0c44c596028944b5991742` (different!)
   - **Used for**: Fetching JSON documents (different API endpoint)
   - **Endpoint**: JSON document fetch (`IF_P01_GLKUNI_30`)
   - **URL**: `https://esb.unipassghana.com:26002/api/cl/dl/common/boe?boeNo={0}` (different port: 26002)

### 🔐 Interface Keys (ESB_IF_ID) - 5 different keys

Each endpoint has its own Interface Key that identifies the API operation:

1. **`IF_P01_NSCUNI_04`** - Batch BOE fetch by date range
2. **`IF_P01_NSCUNI_05`** - Single container BOE fetch ⭐ (This is what we're using for downloads)
3. **`IF_P01_NSCUNI_02`** - Submit scan results
4. **`IF_P01_NSCUNI_08`** - Read submission status
5. **`IF_P01_NSCUNI_09`** - Fetch BOE attached documents
6. **`IF_P01_GLKUNI_30`** - Fetch JSON documents (different system)

## 📊 Endpoint Mapping

| Purpose | URL | Interface Key | Auth Key |
|---------|-----|---------------|----------|
| **Batch Fetch** | `/api/rm/scan/boe?startDate={0}&endDate={1}` | `IF_P01_NSCUNI_04` | `AuthKey` |
| **Container Fetch** | `/api/rm/scan/boe/container/{0}` | `IF_P01_NSCUNI_05` | `AuthKey` |
| **Submit Results** | `/api/rm/scan/result` | `IF_P01_NSCUNI_02` | `AuthKey` |
| **Read Status** | `/api/rm/scan/readStatus` | `IF_P01_NSCUNI_08` | `AuthKey` |
| **Fetch Documents** | `/api/rm/scan/boeAttDocData?DeclarationNumber={0}` | `IF_P01_NSCUNI_09` | `DocumentsAuthKey` |
| **Fetch JSON Docs** | `/api/cl/dl/common/boe?boeNo={0}` (port 26002) | `IF_P01_GLKUNI_30` | `JsonDocumentsAuthKey` |

## 🔍 How Headers Are Set

For each API call, the code:
1. Sets **`ESB_IF_ID`** header = Interface Key (e.g., `IF_P01_NSCUNI_05`)
2. Sets **`ESB_AUTH_KEY`** header = Appropriate Auth Key based on endpoint
3. Sets **`Accept`** header = `application/json`

### Example: Container Fetch (Current Issue)
```
URL: https://esb.unipassghana.com:26004/api/rm/scan/boe/container/MRKU3468405
Headers:
  ESB_IF_ID: IF_P01_NSCUNI_05
  ESB_AUTH_KEY: e80b69d843b14ddca6e8a398eb6c3bb2f587d21126e643b9a31b65b6f7740675
  Accept: application/json
```

## ✅ Current Configuration Status

All keys in `appsettings.json` match the provided configuration:
- ✅ All URLs correct
- ✅ All Interface Keys correct
- ✅ All Auth Keys correct
- ✅ Port differences noted (26004 vs 26002)

## 🎯 Key Insight

The **"Invalid Request Header"** error suggests:
- Either the **Interface Key** (`IF_P01_NSCUNI_05`) is incorrect
- Or the **Auth Key** (`e80b69d843b14ddca6e8a398eb6c3bb2f587d21126e643b9a31b65b6f7740675`) is invalid/expired
- Or the headers are being modified/stripped by the proxy

The configuration structure itself is correct - the issue is likely with the actual key values or how they're being transmitted through the proxy.

