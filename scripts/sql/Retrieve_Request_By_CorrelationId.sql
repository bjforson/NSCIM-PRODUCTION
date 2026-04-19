-- Retrieve Request by Correlation ID
-- Request ID: d9efc524-411d-44af-92e3-dc58fefc51bc
-- This script queries the EndpointUsageLog table to find all records with the given CorrelationId

USE [NS_CIS];  -- Adjust database name if different
GO

-- Main query: Get all endpoint usage logs for this correlation ID
SELECT 
    Id,
    Endpoint,
    Method,
    StatusCode,
    ResponseTimeMs,
    IpAddress,
    UserAgent,
    Timestamp,
    IsDeprecated,
    IsPhase3Route,
    CorrelationId
FROM 
    EndpointUsageLog
WHERE 
    CorrelationId = 'd9efc524-411d-44af-92e3-dc58fefc51bc'
ORDER BY 
    Timestamp ASC;
GO

-- Summary: Count of records and status code breakdown
SELECT 
    COUNT(*) AS TotalRequests,
    MIN(Timestamp) AS FirstRequest,
    MAX(Timestamp) AS LastRequest,
    AVG(ResponseTimeMs) AS AvgResponseTimeMs,
    SUM(CASE WHEN StatusCode >= 200 AND StatusCode < 300 THEN 1 ELSE 0 END) AS SuccessCount,
    SUM(CASE WHEN StatusCode >= 400 AND StatusCode < 500 THEN 1 ELSE 0 END) AS ClientErrorCount,
    SUM(CASE WHEN StatusCode >= 500 THEN 1 ELSE 0 END) AS ServerErrorCount
FROM 
    EndpointUsageLog
WHERE 
    CorrelationId = 'd9efc524-411d-44af-92e3-dc58fefc51bc';
GO

-- Detailed breakdown by endpoint
SELECT 
    Endpoint,
    Method,
    StatusCode,
    COUNT(*) AS RequestCount,
    AVG(ResponseTimeMs) AS AvgResponseTimeMs,
    MIN(ResponseTimeMs) AS MinResponseTimeMs,
    MAX(ResponseTimeMs) AS MaxResponseTimeMs
FROM 
    EndpointUsageLog
WHERE 
    CorrelationId = 'd9efc524-411d-44af-92e3-dc58fefc51bc'
GROUP BY 
    Endpoint, Method, StatusCode
ORDER BY 
    Endpoint, Method, StatusCode;
GO


