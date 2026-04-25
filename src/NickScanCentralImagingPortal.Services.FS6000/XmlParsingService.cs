using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.FS6000;

namespace NickScanCentralImagingPortal.Services.FS6000
{
    public interface IXmlParsingService
    {
        Task<List<FS6000Scan>> ParseXmlFileAsync(string xmlFilePath);
        Task<bool> ValidateXmlFileAsync(string xmlFilePath);
        FS6000Scan? ParseXmlData(string xmlContent);
    }

    public class XmlParsingService : IXmlParsingService
    {
        private readonly ILogger<XmlParsingService> _logger;
        private const string SERVICE_ID = "[FS6000-XML-PARSER]";

        // Round-1 audit H-9: hardened XmlReader settings used everywhere we
        // turn raw FS6000 XML into XDocument. DtdProcessing.Prohibit blocks
        // XXE/billion-laughs; XmlResolver=null prevents external entity fetch
        // even if a DTD slips through; the size cap stops a malicious 16 MB
        // single-element document from blowing memory.
        private static readonly XmlReaderSettings _hardenedSettings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 25_000_000,
            MaxCharactersFromEntities = 0,
            CloseInput = true,
        };

        public XmlParsingService(ILogger<XmlParsingService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Parse a string of XML content using the hardened settings. Surfaces
        /// XmlException to the caller so existing fix-then-retry paths still
        /// work; the only behavioural difference is that DTDs and external
        /// entities are now rejected with an XmlException.
        /// </summary>
        private static XDocument ParseHardened(string xmlContent)
        {
            using var stringReader = new StringReader(xmlContent);
            using var xmlReader = XmlReader.Create(stringReader, _hardenedSettings);
            return XDocument.Load(xmlReader);
        }

        public async Task<List<FS6000Scan>> ParseXmlFileAsync(string xmlFilePath)
        {
            var scans = new List<FS6000Scan>();
            try
            {
                _logger.LogDebug("Starting to parse XML file: {XmlFilePath}", xmlFilePath);

                // Read and fix XML content
                string xmlContent = await ReadAndFixXmlFile(xmlFilePath);

                if (string.IsNullOrEmpty(xmlContent))
                {
                    _logger.LogWarning("Could not read XML file content: {XmlFilePath}", xmlFilePath);
                    return new List<FS6000Scan>();
                }

                XDocument doc;
                try
                {
                    doc = ParseHardened(xmlContent);
                }
                catch (XmlException ex)
                {
                    _logger.LogWarning(ex, "Initial XML parsing failed, attempting enhanced fixes: {XmlFilePath}", xmlFilePath);

                    // Apply enhanced fixes for malformed XML
                    xmlContent = EnhancedXmlFixes(xmlContent);

                    try
                    {
                        doc = ParseHardened(xmlContent);
                        _logger.LogDebug("Successfully parsed XML after enhanced fixes: {XmlFilePath}", xmlFilePath);
                    }
                    catch (XmlException ex2)
                    {
                        _logger.LogError(ex2, "Failed to parse XML file even after enhanced fixes: {XmlFilePath}", xmlFilePath);
                        return new List<FS6000Scan>();
                    }
                }

                // Parse the XML structure
                var root = doc.Root;
                if (root == null)
                {
                    _logger.LogWarning("XML file has no root element: {XmlFilePath}", xmlFilePath);
                    return scans;
                }

                // Look for scan data in FS6000 structure: IDR -> IDR_IMAGE
                var scanElements = new List<XElement>();

                // Check if root is IDR and contains IDR_IMAGE elements
                if (root.Name.LocalName == "IDR")
                {
                    scanElements = root.Descendants("IDR_IMAGE").ToList();
                    _logger.LogDebug("Found {Count} IDR_IMAGE elements in IDR root", scanElements.Count);
                }
                else
                {
                    // Fallback to other possible structures
                    scanElements = root.Descendants("Scan")
                        .Concat(root.Descendants("scan"))
                        .Concat(root.Descendants("SCAN"))
                        .Concat(root.Descendants("Data"))
                        .Concat(root.Descendants("data"))
                        .Concat(root.Descendants("DATA"))
                        .Concat(root.Descendants("IDR"))
                        .ToList();
                }

                if (!scanElements.Any())
                {
                    _logger.LogWarning("No scan elements found in XML file: {XmlFilePath}", xmlFilePath);
                    return scans;
                }

                foreach (var scanElement in scanElements)
                {
                    try
                    {
                        // ✅ FIX: ParseScanElement can now return multiple scans when multiple containers are found
                        var parsedScans = ParseScanElement(scanElement);
                        if (parsedScans != null && parsedScans.Count > 0)
                        {
                            scans.AddRange(parsedScans);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error parsing scan element in XML file: {XmlFilePath}", xmlFilePath);
                    }
                }

                _logger.LogDebug("{ServiceId} Successfully parsed {Count} scans from XML file: {XmlFilePath}", SERVICE_ID, scans.Count, xmlFilePath);
                return scans;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing XML file: {XmlFilePath}", xmlFilePath);
                return new List<FS6000Scan>();
            }
        }

        private async Task<string> ReadAndFixXmlFile(string xmlFilePath)
        {
            try
            {
                // Method 1: Try with UTF-16 encoding first (most likely for FS6000)
                try
                {
                    using var reader = new StreamReader(xmlFilePath, Encoding.Unicode, detectEncodingFromByteOrderMarks: true);
                    var content = await reader.ReadToEndAsync();
                    if (!string.IsNullOrEmpty(content))
                    {
                        content = FixXmlDeclaration(content);
                        if (content.Contains("<?xml"))
                        {
                            _logger.LogDebug("Successfully read and fixed XML file using UTF-16 encoding: {XmlFilePath}", xmlFilePath);
                            return content;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("UTF-16 encoding failed for {XmlFilePath}: {Error}", xmlFilePath, ex.Message);
                }

                // Method 2: Try with UTF-8 encoding
                try
                {
                    using var reader = new StreamReader(xmlFilePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    var content = await reader.ReadToEndAsync();
                    if (!string.IsNullOrEmpty(content))
                    {
                        content = FixXmlDeclaration(content);
                        if (content.Contains("<?xml"))
                        {
                            _logger.LogDebug("Successfully read and fixed XML file using UTF-8 encoding: {XmlFilePath}", xmlFilePath);
                            return content;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("UTF-8 encoding failed for {XmlFilePath}: {Error}", xmlFilePath, ex.Message);
                }

                // Method 3: Try with ASCII encoding
                try
                {
                    using var reader = new StreamReader(xmlFilePath, Encoding.ASCII);
                    var content = await reader.ReadToEndAsync();
                    if (!string.IsNullOrEmpty(content))
                    {
                        content = FixXmlDeclaration(content);
                        if (content.Contains("<?xml"))
                        {
                            _logger.LogDebug("Successfully read and fixed XML file using ASCII encoding: {XmlFilePath}", xmlFilePath);
                            return content;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("ASCII encoding failed for {XmlFilePath}: {Error}", xmlFilePath, ex.Message);
                }

                // Method 4: Try with Default encoding
                try
                {
                    using var reader = new StreamReader(xmlFilePath, Encoding.Default);
                    var content = await reader.ReadToEndAsync();
                    if (!string.IsNullOrEmpty(content))
                    {
                        content = FixXmlDeclaration(content);
                        if (content.Contains("<?xml"))
                        {
                            _logger.LogDebug("Successfully read and fixed XML file using Default encoding: {XmlFilePath}", xmlFilePath);
                            return content;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "All encoding attempts failed for {XmlFilePath}.", xmlFilePath);
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading XML file: {XmlFilePath}", xmlFilePath);
                return string.Empty;
            }
        }

        private string FixXmlDeclaration(string xmlContent)
        {
            try
            {
                // Remove any BOM characters
                xmlContent = xmlContent.TrimStart('\uFEFF');

                // AGGRESSIVE FIX: Replace problematic XML declarations
                // The issue is that UTF-16 files have encoding="UTF-16" but when read as UTF-16, 
                // the parser expects UTF-8 encoding declaration

                // Fix 1: Replace UTF-16 declaration with UTF-8
                xmlContent = xmlContent.Replace("<?xml version=\"1.0\" encoding=\"UTF-16\"?>", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>");

                // Fix 2: Replace any other problematic declarations
                xmlContent = xmlContent.Replace("<?xml version=\"1.0\" encoding=\"utf-16\"?>", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                xmlContent = xmlContent.Replace("<?xml version=\"1.0\" encoding=\"UTF-16\"?>", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>");

                // Fix 3: If no XML declaration exists, add one
                if (!xmlContent.StartsWith("<?xml"))
                {
                    xmlContent = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + xmlContent;
                }

                // Fix 4: Remove any null characters
                xmlContent = xmlContent.Replace("\0", "");

                // Fix 5: REMOVED - Do NOT decode HTML entities as they are required for valid XML
                // The XML parser needs properly escaped entities like &amp; &lt; &gt; etc.
                // Decoding them would create invalid XML

                return xmlContent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fixing XML declaration");
                return xmlContent;
            }
        }

        private string EnhancedXmlFixes(string xmlContent)
        {
            try
            {
                _logger.LogDebug("Applying enhanced XML fixes for entity errors");

                // Fix 1: Remove problematic IMGTYPE element that contains malformed nested XML
                xmlContent = Regex.Replace(xmlContent,
                    @"<IMGTYPE>.*?</IMGTYPE>",
                    "<IMGTYPE></IMGTYPE>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);

                // Fix 2: Handle malformed entity references - escape standalone ampersands
                // This is the #1 cause of "error occurred while parsing EntityName" errors
                // First, protect already-escaped entities temporarily
                var protectedContent = xmlContent
                    .Replace("&amp;", "___AMP___")
                    .Replace("&lt;", "___LT___")
                    .Replace("&gt;", "___GT___")
                    .Replace("&quot;", "___QUOT___")
                    .Replace("&apos;", "___APOS___");

                // Fix 3: Now escape any remaining unescaped ampersands
                protectedContent = Regex.Replace(protectedContent, @"&", "&amp;");

                // Fix 4: Restore the protected entities
                xmlContent = protectedContent
                    .Replace("___AMP___", "&amp;")
                    .Replace("___LT___", "&lt;")
                    .Replace("___GT___", "&gt;")
                    .Replace("___QUOT___", "&quot;")
                    .Replace("___APOS___", "&apos;");

                // Fix 5: Remove any remaining malformed nested XML patterns
                xmlContent = Regex.Replace(xmlContent,
                    @"<[^>]*&lt;[^>]*>",
                    "",
                    RegexOptions.Singleline);

                // Fix 6: Remove any null characters or control characters
                xmlContent = Regex.Replace(xmlContent, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "");

                _logger.LogDebug("Enhanced XML fixes completed - all ampersands properly escaped");
                return xmlContent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying enhanced XML fixes");
                return xmlContent;
            }
        }

        public async Task<bool> ValidateXmlFileAsync(string xmlFilePath)
        {
            try
            {
                if (!File.Exists(xmlFilePath))
                {
                    _logger.LogWarning("XML file does not exist: {XmlFilePath}", xmlFilePath);
                    return false;
                }

                var xmlContent = await ReadAndFixXmlFile(xmlFilePath);

                if (string.IsNullOrEmpty(xmlContent))
                {
                    return false;
                }

                ParseHardened(xmlContent);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "XML file validation failed: {XmlFilePath}", xmlFilePath);
                return false;
            }
        }

        public FS6000Scan? ParseXmlData(string xmlContent)
        {
            try
            {
                if (string.IsNullOrEmpty(xmlContent))
                {
                    _logger.LogWarning("XML content is empty");
                    return null;
                }

                _logger.LogDebug("Starting to parse XML data");

                // Fix XML content
                xmlContent = FixXmlDeclaration(xmlContent);

                XDocument doc;
                try
                {
                    doc = ParseHardened(xmlContent);
                }
                catch (XmlException ex)
                {
                    _logger.LogWarning(ex, "Initial XML parsing failed, attempting enhanced fixes");
                    xmlContent = EnhancedXmlFixes(xmlContent);
                    doc = ParseHardened(xmlContent);
                }

                var root = doc.Root;
                if (root == null)
                {
                    _logger.LogWarning("XML content has no root element");
                    return null;
                }

                var scanElement = root.Descendants("IDR_IMAGE").FirstOrDefault();
                if (scanElement == null)
                {
                    // Fallback to other structures
                    scanElement = root.Descendants("Scan")
                        .Concat(root.Descendants("scan"))
                        .Concat(root.Descendants("SCAN"))
                        .Concat(root.Descendants("Data"))
                        .Concat(root.Descendants("data"))
                        .Concat(root.Descendants("DATA"))
                        .Concat(root.Descendants("IDR"))
                        .FirstOrDefault();
                }

                if (scanElement == null)
                {
                    _logger.LogWarning("No scan elements found in XML content");
                    return null;
                }

                var parsedScans = ParseScanElement(scanElement);
                return parsedScans?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing XML content");
                return null;
            }
        }

        private List<FS6000Scan> ParseScanElement(XElement scanElement)
        {
            var scans = new List<FS6000Scan>();
            try
            {
                // ✅ FIX: Extract ALL container numbers (not just the first) to handle multiple containers in one record
                var containerNumbers = ValidateAndExtractAllContainerNumbers(scanElement);
                if (containerNumbers == null || containerNumbers.Count == 0)
                {
                    _logger.LogWarning("Container number validation failed - skipping scan");
                    return scans;
                }

                // Parse common fields once (same for all containers)
                var scanTimeStr = GetElementValue(scanElement, "SCANTIME", "scantime", "ScanTime", "scan_time", "SCAN_TIME", "TIMESTAMP", "CHECKINTIME");
                DateTime scanTime = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(scanTimeStr))
                {
                    if (DateTime.TryParse(scanTimeStr, out var parsed))
                    {
                        scanTime = parsed.Kind == DateTimeKind.Utc
                            ? parsed
                            : DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                    }
                    else
                    {
                        _logger.LogWarning("Invalid scan time format: {ScanTime}", scanTimeStr);
                        scanTime = DateTime.UtcNow;
                    }
                }

                var picNumber = GetElementValue(scanElement, "PICNO", "picno", "PicNo", "PIC_NUMBER", "pic_number");
                var fycoPresent = GetElementValue(scanElement, "fyco_present", "FYCO_PRESENT", "FycoPresent");
                var vesselName = GetElementValue(scanElement, "name_vessel", "VesselName", "VESSEL_NAME", "vessel_name");
                var goodsVehicleNo = GetElementValue(scanElement, "g_v_no", "G_V_NO", "GV_NO", "gv_no");
                var operatorId = GetElementValue(scanElement, "OPERATORID", "OPERATOR_ID", "operator_id", "OperatorId", "OPERATOR");
                var scanResult = GetElementValue(scanElement, "TYPE", "SCAN_RESULT", "scan_result", "ScanResult", "RESULT");
                var goodsDescription = GetElementValue(scanElement, "descripion_of_goods", "GOODS_DESCRIPTION", "goods_description", "GoodsDescription", "DESCRIPTION");
                var shippingCompany = GetElementValue(scanElement, "shipping_company", "SHIPPING_COMPANY", "ShippingCompany", "SHIPPER");
                var consignee = GetElementValue(scanElement, "consignee", "CONSIGNEE", "Consignee", "CONSIGNEE_NAME");

                // ✅ FIX: Create one FS6000Scan record for each container number
                foreach (var containerNumber in containerNumbers)
                {
                    // g_v_no is the "proper" field for truck plate but operators use name_vessel instead
                    var truckPlate = !string.IsNullOrWhiteSpace(goodsVehicleNo)
                        ? goodsVehicleNo
                        : vesselName;

                    var scan = new FS6000Scan
                    {
                        ContainerNumber = containerNumber,
                        ScanTime = scanTime,
                        PicNumber = picNumber,
                        FycoPresent = fycoPresent,
                        VesselName = vesselName,
                        TruckPlate = truckPlate,
                        OperatorId = operatorId,
                        ScanResult = scanResult,
                        GoodsDescription = goodsDescription,
                        ShippingCompany = shippingCompany,
                        Consignee = consignee
                    };

                    scans.Add(scan);
                }

                if (scans.Count > 1)
                {
                    _logger.LogInformation("✅ Multi-container record: Created {Count} scan records for containers: {Containers}",
                        scans.Count, string.Join(", ", containerNumbers));
                }

                _logger.LogDebug("Parsed {Count} scan(s): Containers={Containers}, PicNumber={PicNumber}, FycoPresent={FycoPresent}, ScanTime={ScanTime}",
                    scans.Count, string.Join(", ", containerNumbers), picNumber, fycoPresent, scanTime);

                return scans;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing scan element");
                return scans;
            }
        }

        /// <summary>
        /// ✅ FIX: Extract ALL container numbers from XML element (handles comma-separated values)
        /// Returns a list of all valid container numbers found in the XML element
        /// </summary>
        private List<string> ValidateAndExtractAllContainerNumbers(XElement scanElement)
        {
            try
            {
                // Extract both container number fields
                var unitId = GetElementValue(scanElement, "UNITID", "unitid", "UnitId");
                var containerNo = GetElementValue(scanElement, "container_no", "CONTAINER_NO", "container_number", "ContainerNumber");

                _logger.LogDebug("Container validation - UNITID: '{UnitId}', container_no: '{ContainerNo}'", unitId, containerNo);

                // Handle comma-separated values
                var unitIdNumbers = !string.IsNullOrEmpty(unitId)
                    ? unitId.Split(',').Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList()
                    : new List<string>();

                var containerNoNumbers = !string.IsNullOrEmpty(containerNo)
                    ? containerNo.Split(',').Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList()
                    : new List<string>();

                // Cross-validation logic: Return ALL matching container numbers
                if (unitIdNumbers.Any() && containerNoNumbers.Any())
                {
                    // ✅ FIX: Return ALL matches (not just the first one)
                    var matches = unitIdNumbers.Intersect(containerNoNumbers).Distinct().ToList();
                    if (matches.Any())
                    {
                        _logger.LogDebug("Container validation SUCCESS - Found {Count} match(es): {ContainerNumbers}",
                            matches.Count, string.Join(", ", matches));
                        return matches;
                    }
                    else
                    {
                        _logger.LogWarning("Container validation FAILED - No matches found between UNITID: [{UnitIds}] and container_no: [{ContainerNos}]",
                            string.Join(", ", unitIdNumbers), string.Join(", ", containerNoNumbers));
                        return new List<string>();
                    }
                }
                else if (unitIdNumbers.Any())
                {
                    // Fallback to UNITID if container_no is empty - return ALL UNITID values
                    _logger.LogDebug("Container validation SUCCESS - Using UNITID fallback: {Count} container(s): {ContainerNumbers}",
                        unitIdNumbers.Count, string.Join(", ", unitIdNumbers));
                    return unitIdNumbers.Distinct().ToList();
                }
                else if (containerNoNumbers.Any())
                {
                    // Fallback to container_no if UNITID is empty - return ALL container_no values
                    _logger.LogDebug("Container validation SUCCESS - Using container_no fallback: {Count} container(s): {ContainerNumbers}",
                        containerNoNumbers.Count, string.Join(", ", containerNoNumbers));
                    return containerNoNumbers.Distinct().ToList();
                }
                else
                {
                    _logger.LogWarning("Container validation FAILED - Both UNITID and container_no are empty");
                    return new List<string>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during container number validation");
                return new List<string>();
            }
        }

        /// <summary>
        /// Legacy method for backward compatibility - returns first container number only
        /// </summary>
        [Obsolete("Use ValidateAndExtractAllContainerNumbers instead")]
        private string ValidateAndExtractContainerNumber(XElement scanElement)
        {
            var containerNumbers = ValidateAndExtractAllContainerNumbers(scanElement);
            return containerNumbers.FirstOrDefault() ?? string.Empty;
        }

        private string GetElementValue(XElement parent, params string[] elementNames)
        {
            foreach (var elementName in elementNames)
            {
                try
                {
                    // Try direct child first
                    var element = parent.Element(elementName);
                    if (element != null && !string.IsNullOrEmpty(element.Value))
                    {
                        return element.Value.Trim();
                    }

                    // Try nested elements using XPath (searches entire subtree)
                    var nestedElement = parent.XPathSelectElement($"//{elementName}");
                    if (nestedElement != null && !string.IsNullOrEmpty(nestedElement.Value))
                    {
                        return nestedElement.Value.Trim();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Error searching for element '{ElementName}': {Error}", elementName, ex.Message);
                }
            }
            return string.Empty;
        }
    }
}
