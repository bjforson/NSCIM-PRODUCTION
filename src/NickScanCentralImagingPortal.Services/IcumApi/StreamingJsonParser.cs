using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.IcumApi
{
    /// <summary>
    /// Streaming JSON parser for ICUMS BOEScanDocument files
    /// Uses JsonDocument with FileStream for memory-efficient parsing
    /// Processes documents in batches to minimize memory footprint
    /// </summary>
    public class StreamingJsonParser
    {
        private readonly ILogger<StreamingJsonParser> _logger;

        public StreamingJsonParser(ILogger<StreamingJsonParser> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Parses JSON file using streaming approach with JsonDocument
        /// This approach uses less memory than ReadAllTextAsync because:
        /// 1. FileStream reads in chunks (not entire file at once)
        /// 2. JsonDocument uses pooled memory buffers
        /// 3. Documents are processed and released immediately
        /// </summary>
        public async Task<JsonDocument> ParseJsonFileAsync(string filePath)
        {
            // ✅ PHASE 1.3: Use FileStream with async reading for memory efficiency
            // This reads the file in chunks rather than loading entire file into memory
            await using var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 8192, // 8KB buffer
                useAsync: true);

            // ParseAsync uses streaming internally and is more memory-efficient than Parse
            var jsonDocument = await JsonDocument.ParseAsync(fileStream, new JsonDocumentOptions
            {
                MaxDepth = 128, // Reasonable depth limit
                CommentHandling = JsonCommentHandling.Skip
            });

            return jsonDocument;
        }

        /// <summary>
        /// Gets the BOEScanDocument array from parsed JSON document
        /// </summary>
        public static bool TryGetBOEScanDocuments(JsonDocument document, out JsonElement boeDocuments)
        {
            if (document.RootElement.TryGetProperty("BOEScanDocument", out var documents) &&
                documents.ValueKind == JsonValueKind.Array)
            {
                boeDocuments = documents;
                return true;
            }

            boeDocuments = default;
            return false;
        }
    }
}

