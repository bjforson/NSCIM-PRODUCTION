using NickComms.Gateway.Models;
using NickComms.Gateway.Services;

namespace NickComms.Gateway.Endpoints;

public static class SmsEndpoints
{
    public static RouteGroupBuilder MapSmsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sms")
            .WithTags("SMS")
            .RequireAuthorization();

        group.MapPost("/send", async (SendSmsRequest request, ISmsService smsService, HttpContext ctx) =>
        {
            var clientApp = ctx.User.FindFirst("AppName")?.Value ?? "unknown";
            var result = await smsService.SendSingleAsync(request, clientApp, ctx.RequestAborted);
            return Results.Accepted($"/api/sms/{result.Id}/status", result);
        })
        .WithName("SendSms")
        .WithDescription("Queue a single SMS for delivery");

        group.MapPost("/bulk", async (BulkSmsRequest request, ISmsService smsService, HttpContext ctx) =>
        {
            var clientApp = ctx.User.FindFirst("AppName")?.Value ?? "unknown";
            var result = await smsService.SendBulkAsync(request, clientApp, ctx.RequestAborted);
            return Results.Accepted(null, result);
        })
        .WithName("SendBulkSms")
        .WithDescription("Queue bulk SMS for delivery");

        group.MapGet("/{id:guid}/status", async (Guid id, ISmsService smsService, CancellationToken ct) =>
        {
            var result = await smsService.GetStatusAsync(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetSmsStatus")
        .WithDescription("Check SMS delivery status");

        return group;
    }
}
