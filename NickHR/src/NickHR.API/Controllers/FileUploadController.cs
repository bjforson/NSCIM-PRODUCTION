using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NickHR.Core.Entities.Core;
using NickHR.Infrastructure.Data;

namespace NickHR.API.Controllers;

[ApiController]
[Route("api/files")]
[Authorize]
public class FileUploadController : ControllerBase
{
    private readonly NickHRDbContext _db;
    private readonly ILogger<FileUploadController> _logger;
    private const string UploadsRoot = @"C:/Shared/NSCIM_PRODUCTION/NickHR/uploads";
    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

    private static readonly Dictionary<string, string[]> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["photo"] = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" },
        ["document"] = new[] { ".pdf", ".doc", ".docx", ".jpg", ".jpeg", ".png" },
        ["receipt"] = new[] { ".pdf", ".jpg", ".jpeg", ".png" },
        ["certificate"] = new[] { ".pdf", ".jpg", ".jpeg", ".png" }
    };

    public FileUploadController(NickHRDbContext db, ILogger<FileUploadController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Upload a file. Category: photo, document, receipt, certificate.
    /// Returns the relative path (e.g. "photos/abc123.jpg").
    /// </summary>
    [HttpPost("upload")]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromQuery] string category = "document",
        [FromQuery] int? employeeId = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { success = false, message = "No file provided." });

        if (file.Length > MaxFileSize)
            return BadRequest(new { success = false, message = "File exceeds 10 MB limit." });

        category = category.ToLowerInvariant();
        if (!AllowedExtensions.ContainsKey(category))
            category = "document";

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions[category].Contains(ext))
            return BadRequest(new { success = false, message = $"Extension '{ext}' not allowed for category '{category}'." });

        var subDir = category switch
        {
            "photo" => "photos",
            "receipt" => "receipts",
            "certificate" => "certificates",
            _ => "documents"
        };

        var uploadDir = Path.Combine(UploadsRoot, subDir);
        Directory.CreateDirectory(uploadDir);

        var uniqueName = $"{Guid.NewGuid()}{ext}";
        var fullPath = Path.Combine(uploadDir, uniqueName);

        await using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var relativePath = $"{subDir}/{uniqueName}";
        _logger.LogInformation("File uploaded: {RelativePath} (employee={EmployeeId})", relativePath, employeeId);

        return Ok(new { success = true, path = relativePath, fileName = file.FileName, fileSize = file.Length });
    }

    /// <summary>
    /// Upload portrait photo for an employee. Stores in database.
    /// </summary>
    [HttpPost("upload-photo/{employeeId:int}")]
    public async Task<IActionResult> UploadPhoto(int employeeId, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { success = false, message = "No file provided." });

        if (file.Length > MaxFileSize)
            return BadRequest(new { success = false, message = "File exceeds 10 MB limit." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions["photo"].Contains(ext))
            return BadRequest(new { success = false, message = $"Extension '{ext}' is not a supported image format." });

        var employee = await _db.Employees.FindAsync(employeeId);
        if (employee == null)
            return NotFound(new { success = false, message = $"Employee {employeeId} not found." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        employee.PhotoData = ms.ToArray();
        employee.PhotoContentType = file.ContentType;
        employee.PhotoUrl = $"db://photo/{employeeId}";
        employee.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { success = true, photoUrl = $"/api/files/photo/{employeeId}" });
    }

    /// <summary>Serve employee photo from database.</summary>
    [HttpGet("photo/{employeeId:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPhoto(int employeeId)
    {
        var employee = await _db.Employees
            .Where(e => e.Id == employeeId && !e.IsDeleted)
            .Select(e => new { e.PhotoData, e.PhotoContentType })
            .FirstOrDefaultAsync();

        if (employee?.PhotoData == null)
            return NotFound();

        return File(employee.PhotoData, employee.PhotoContentType ?? "image/jpeg");
    }

    /// <summary>Serve document from database.</summary>
    [HttpGet("document/{documentId:int}")]
    public async Task<IActionResult> GetDocument(int documentId)
    {
        var doc = await _db.EmployeeDocuments
            .Where(d => d.Id == documentId && !d.IsDeleted)
            .Select(d => new { d.FileData, d.ContentType, d.FileName })
            .FirstOrDefaultAsync();

        if (doc?.FileData == null)
            return NotFound();

        return File(doc.FileData, doc.ContentType ?? "application/octet-stream", doc.FileName);
    }

    /// <summary>
    /// Serve a file for viewing or download.
    /// </summary>
    [HttpGet("{**filePath}")]
    public IActionResult ServeFile(string filePath)
    {
        // Prevent path traversal
        if (filePath.Contains(".."))
            return BadRequest("Invalid path.");

        var fullPath = Path.Combine(UploadsRoot, filePath.Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(fullPath))
            return NotFound();

        var contentType = GetContentType(Path.GetExtension(fullPath));
        var fileName = Path.GetFileName(fullPath);
        return PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
    }

    /// <summary>
    /// Delete a file (admin only).
    /// </summary>
    [HttpDelete("{**filePath}")]
    [Authorize(Roles = "SuperAdmin,HRManager")]
    public IActionResult DeleteFile(string filePath)
    {
        if (filePath.Contains(".."))
            return BadRequest("Invalid path.");

        var fullPath = Path.Combine(UploadsRoot, filePath.Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(fullPath))
            return NotFound();

        System.IO.File.Delete(fullPath);
        _logger.LogInformation("File deleted: {FilePath}", filePath);
        return Ok(new { success = true });
    }

    private static string GetContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream"
    };
}
