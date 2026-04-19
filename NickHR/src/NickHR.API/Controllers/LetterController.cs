using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickHR.Core.DTOs;
using NickHR.Core.Entities.System;
using NickHR.Services.Letter;

namespace NickHR.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "SuperAdmin,HRManager,HROfficer")]
public class LetterController : ControllerBase
{
    private readonly LetterService _letterService;

    public LetterController(LetterService letterService)
    {
        _letterService = letterService;
    }

    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates()
    {
        var templates = await _letterService.GetTemplatesAsync();
        return Ok(ApiResponse<object>.Ok(templates));
    }

    [HttpPost("templates")]
    public async Task<IActionResult> CreateTemplate([FromBody] LetterTemplate template)
    {
        var result = await _letterService.CreateTemplateAsync(template);
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpGet("preview/{templateId}/{employeeId}")]
    public async Task<IActionResult> Preview(int templateId, int employeeId)
    {
        try
        {
            var html = await _letterService.GeneratePreviewAsync(templateId, employeeId);
            return Ok(ApiResponse<object>.Ok(new { html }));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail(ex.Message));
        }
    }

    [HttpPost("generate/{templateId}/{employeeId}")]
    public async Task<IActionResult> Generate(int templateId, int employeeId, [FromQuery] int? generatedById)
    {
        try
        {
            var pdf = await _letterService.GeneratePdfAsync(templateId, employeeId, generatedById);
            return File(pdf, "application/pdf", $"Letter_{templateId}_{employeeId}.pdf");
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail(ex.Message));
        }
    }

    [HttpGet("generated")]
    public async Task<IActionResult> GetGenerated([FromQuery] int? employeeId)
    {
        var letters = await _letterService.GetGeneratedLettersAsync(employeeId);
        return Ok(ApiResponse<object>.Ok(letters));
    }

    [HttpPost("seed-templates")]
    public async Task<IActionResult> SeedTemplates()
    {
        await _letterService.SeedDefaultTemplatesAsync();
        return Ok(ApiResponse<object>.Ok(null, "Default templates seeded."));
    }
}
