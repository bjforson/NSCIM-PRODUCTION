using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NickComms.Gateway.Configuration;
using NickComms.Gateway.Data;
using NickComms.Gateway.Entities;
using NickComms.Gateway.Models;
using NickComms.Gateway.Services;

namespace NickComms.Gateway.Endpoints;

public static class OtpEndpoints
{
    public static RouteGroupBuilder MapOtpEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/otp")
            .WithTags("OTP")
            .RequireAuthorization();

        group.MapPost("/send", async (
            SendOtpRequest request,
            IHubtelClient hubtel,
            CommsDbContext db,
            IOptions<HubtelOptions> hubtelOpts,
            IOptions<SmsGatewayOptions> gatewayOpts,
            HttpContext ctx) =>
        {
            var clientApp = ctx.User.FindFirst("AppName")?.Value ?? "unknown";
            var opts = hubtelOpts.Value;
            var gw = gatewayOpts.Value;

            var result = await hubtel.SendOtpAsync(opts.DefaultSenderId, request.PhoneNumber, gw.DefaultCountryCode, ctx.RequestAborted);
            if (result?.Data == null)
                return Results.Problem("Failed to send OTP via Hubtel", statusCode: 502);

            var session = new OtpSession
            {
                PhoneNumber = request.PhoneNumber,
                HubtelRequestId = result.Data.RequestId,
                Prefix = result.Data.Prefix,
                ClientApp = clientApp
            };

            db.OtpSessions.Add(session);
            await db.SaveChangesAsync(ctx.RequestAborted);

            return Results.Ok(new OtpSendResponse
            {
                Message = "OTP sent successfully",
                Prefix = result.Data.Prefix
            });
        })
        .WithName("SendOtp")
        .WithDescription("Send OTP to a phone number via Hubtel");

        group.MapPost("/verify", async (
            VerifyOtpRequest request,
            IHubtelClient hubtel,
            CommsDbContext db,
            HttpContext ctx) =>
        {
            // Find the latest unverified session for this phone
            var session = await db.OtpSessions
                .Where(o => o.PhoneNumber == request.PhoneNumber && !o.Verified)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync(ctx.RequestAborted);

            if (session == null)
            {
                return Results.Ok(new OtpVerifyResponse
                {
                    Verified = false,
                    Message = "No active OTP session found for this phone number"
                });
            }

            var verified = await hubtel.VerifyOtpAsync(session.HubtelRequestId, session.Prefix, request.Code, ctx.RequestAborted);

            if (verified)
            {
                session.Verified = true;
                await db.SaveChangesAsync(ctx.RequestAborted);
            }

            return Results.Ok(new OtpVerifyResponse
            {
                Verified = verified,
                Message = verified ? "OTP verified successfully" : "Invalid OTP code"
            });
        })
        .WithName("VerifyOtp")
        .WithDescription("Verify an OTP code");

        group.MapPost("/resend", async (
            ResendOtpRequest request,
            IHubtelClient hubtel,
            CommsDbContext db,
            HttpContext ctx) =>
        {
            var session = await db.OtpSessions
                .Where(o => o.PhoneNumber == request.PhoneNumber && !o.Verified)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync(ctx.RequestAborted);

            if (session == null)
                return Results.NotFound(new { message = "No active OTP session found for this phone number" });

            var result = await hubtel.ResendOtpAsync(session.HubtelRequestId, ctx.RequestAborted);
            if (result?.Data == null)
                return Results.Problem("Failed to resend OTP via Hubtel", statusCode: 502);

            // Update session with new prefix if changed
            session.Prefix = result.Data.Prefix;
            await db.SaveChangesAsync(ctx.RequestAborted);

            return Results.Ok(new OtpResendResponse
            {
                Message = "OTP resent successfully",
                Prefix = result.Data.Prefix
            });
        })
        .WithName("ResendOtp")
        .WithDescription("Resend a previously issued OTP");

        return group;
    }
}
