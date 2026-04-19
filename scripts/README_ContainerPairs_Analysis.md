# Container Pairs Analysis - How to Use

This document explains how to analyze how ASE scanner multi-container pairs are processed.

## 📋 Overview

When ASE scanner records multiple containers in one scan (e.g., `"TEMU0245328, TRHU2315120"`), the system:
1. **Splits** the comma-separated string into individual containers
2. **Downloads** each container separately from ICUMS API
3. **Processes** each container individually during ingestion
4. **Detects** cross-record relationships if containers belong to different BOEs

## 🔍 Methods to Analyze Container Pairs

### Method 1: SQL Query (Direct Database Access)

Run the SQL script: `scripts/Query_ContainerPairs_Analysis.sql`

This query provides 6 comprehensive reports:
- **Part 1**: All BOE documents for each container with full details
- **Part 2**: Summary count of BOE documents per container
- **Part 3**: Cross-Record Scan entries (if any)
- **Part 4**: Container pair analysis showing relationships
- **Part 5**: ICUMS download queue status
- **Part 6**: Downloaded JSON files containing these containers

**Usage:**
```sql
-- Open SQL Server Management Studio
-- Connect to your database
-- Open and execute: scripts/Query_ContainerPairs_Analysis.sql
```

### Method 2: API Endpoint (Programmatic Access)

**Endpoint:** `POST /api/crossrecordscans/analyze-pairs`

**Request Body:**
```json
{
  "pairs": [
    {
      "container1": "TEMU0245328",
      "container2": "TRHU2315120",
      "pairName": "Pair 1"
    },
    {
      "container1": "APZU3253676",
      "container2": "CMAU2290095",
      "pairName": "Pair 2"
    }
  ]
}
```

**Example using cURL:**
```bash
curl -X POST "https://your-api-url/api/crossrecordscans/analyze-pairs" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d @scripts/API_Request_ContainerPairs_Analysis.json
```

**Example using PowerShell:**
```powershell
$headers = @{
    "Authorization" = "Bearer YOUR_TOKEN"
    "Content-Type" = "application/json"
}

$body = Get-Content "scripts/API_Request_ContainerPairs_Analysis.json" -Raw

$response = Invoke-RestMethod -Uri "https://your-api-url/api/crossrecordscans/analyze-pairs" `
    -Method Post `
    -Headers $headers `
    -Body $body

$response | ConvertTo-Json -Depth 10
```

**Response Structure:**
```json
{
  "pairs": [
    {
      "pairName": "Pair 1",
      "container1": "TEMU0245328",
      "container2": "TRHU2315120",
      "container1BOEDocuments": [
        {
          "id": 123,
          "declarationNumber": "DEC001",
          "consigneeName": "Company A",
          "clearanceType": "IM",
          "crmsLevel": "Low",
          "blNumber": "BL001",
          "rotationNumber": "ROT001",
          "processingStatus": "Completed",
          "createdAt": "2025-11-16T10:00:00Z"
        }
      ],
      "container2BOEDocuments": [
        {
          "id": 124,
          "declarationNumber": "DEC002",
          "consigneeName": "Company B",
          "clearanceType": "IM",
          "crmsLevel": "Medium",
          "blNumber": "BL002",
          "rotationNumber": "ROT002",
          "processingStatus": "Completed",
          "createdAt": "2025-11-16T10:05:00Z"
        }
      ],
      "relationshipStatus": "Different Importers (CROSS-RECORD)",
      "classification": "Cross-Record",
      "crossRecordScanId": 45,
      "crossRecordType": "DifferentImporters",
      "severity": "High",
      "reviewStatus": "Pending"
    }
  ]
}
```

## 📊 Understanding the Results

### Relationship Status Values:
- **"Same Declaration (Same Record)"** - Both containers share the same BOE declaration
- **"Same Master BL (Consolidated)"** - Both containers share the same Master BL (consolidated cargo)
- **"Different Importers (CROSS-RECORD)"** - Containers belong to different importers (most severe)
- **"Different Clearance Types (CROSS-RECORD)"** - One is Import, other is Export
- **"Different CRMS Levels (CROSS-RECORD)"** - Different risk levels
- **"Different BOEs (CROSS-RECORD)"** - Different declarations, same importer
- **"Pending BOE Data"** - One or both containers don't have BOE data yet

### Classification Values:
- **"Normal"** - Same record, no cross-record issue
- **"Cross-Record"** - Different records detected
- **"Pending"** - BOE data not yet available

## 🎯 Why You Might See More Than 4 Records

If you see more than 4 BOE documents for 4 container pairs, it's because:

1. **Multiple Declarations**: A single container can appear in multiple BOE declarations
   - Example: `TEMU0245328` might have:
     - `TEMU0245328 + Declaration001` → 1 BOE document
     - `TEMU0245328 + Declaration002` → 1 BOE document
     - Total: **2 BOE documents** for one container

2. **Deduplication**: The system prevents duplicate `ContainerNumber + DeclarationNumber` combinations
   - If the same container+declaration appears in multiple JSON files, only the first is stored
   - Subsequent duplicates are skipped during ingestion

3. **Processing Status**: Some containers may have multiple BOE documents in different processing states
   - `Pending` - Not yet processed
   - `Completed` - Successfully processed
   - `Failed` - Processing failed

## 🔗 Related Files

- **SQL Query**: `scripts/Query_ContainerPairs_Analysis.sql`
- **API Request Example**: `scripts/API_Request_ContainerPairs_Analysis.json`
- **API Controller**: `src/NickScanCentralImagingPortal.API/Controllers/CrossRecordScansController.cs`
- **Processing Logic**: `src/NickScanCentralImagingPortal.Services/IcumApi/IcumJsonIngestionService.cs`
- **Cross-Record Detection**: `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/MultiContainerValidationService.cs`

