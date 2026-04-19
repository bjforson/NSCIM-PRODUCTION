using NickComms.Gateway.Models;
using NickComms.Gateway.Services;

namespace NickComms.Gateway.Endpoints;

public static class EmailEndpoints
{
    public static RouteGroupBuilder MapEmailEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/email")
            .WithTags("Email")
            .RequireAuthorization();

        group.MapPost("/send", async (SendEmailRequest request, IEmailService emailService, HttpContext ctx) =>
        {
            var clientApp = ctx.User.FindFirst("AppName")?.Value ?? "unknown";
            try
            {
                var result = await emailService.SendSingleAsync(request, clientApp, ctx.RequestAborted);
                return Results.Accepted($"/api/email/{result.Id}/status", result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("SendEmail")
        .WithDescription("Queue a single email for delivery");

        group.MapPost("/bulk", async (BulkEmailRequest request, IEmailService emailService, HttpContext ctx) =>
        {
            var clientApp = ctx.User.FindFirst("AppName")?.Value ?? "unknown";
            try
            {
                var result = await emailService.SendBulkAsync(request, clientApp, ctx.RequestAborted);
                return Results.Accepted(null, result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("SendBulkEmail")
        .WithDescription("Queue bulk emails for delivery");

        group.MapGet("/{id:guid}/status", async (Guid id, IEmailService emailService, CancellationToken ct) =>
        {
            var result = await emailService.GetStatusAsync(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetEmailStatus")
        .WithDescription("Check email delivery status");

        return group;
    }
}
