using Microsoft.EntityFrameworkCore;
using NickComms.Gateway.Data;
using NickComms.Gateway.Models;

namespace NickComms.Gateway.Endpoints;

public static class HistoryEndpoints
{
    public static RouteGroupBuilder MapHistoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/messages")
            .WithTags("History")
            .RequireAuthorization();

        group.MapGet("/history", async (
            CommsDbContext db,
            int page = 1,
            int pageSize = 20,
            string? channel = null,
            string? clientApp = null,
            string? recipient = null,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            CancellationToken ct = default) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var smsQuery = db.SmsMessages.AsNoTracking()
                .Select(m => new MessageHistoryItem
                {
                    Id = m.Id,
                    Channel = "sms",
                    Recipient = m.Recipient,
                    Content = m.Content,
                    Status = m.Status,
                    ClientApp = m.ClientApp,
                    BatchId = m.BatchId,
                    Rate = m.HubtelRate,
                    CreatedAt = m.CreatedAt,
                    SentAt = m.SentAt
                });

            var emailQuery = db.EmailMessages.AsNoTracking()
                .Select(m => new MessageHistoryItem
                {
                    Id = m.Id,
                    Channel = "email",
                    Recipient = m.ToEmail,
                    Content = m.Subject,
                    Status = m.Status,
                    ClientApp = m.ClientApp,
                    BatchId = m.BatchId,
                    Rate = null,
                    CreatedAt = m.CreatedAt,
                    SentAt = m.SentAt
                });

            // Filter by channel if specified
            IQueryable<MessageHistoryItem> query;
            if (channel?.ToLowerInvariant() == "sms")
                query = smsQuery;
            else if (channel?.ToLowerInvariant() == "email")
                query = emailQuery;
            else
                query = smsQuery.Union(emailQuery);

            if (!string.IsNullOrWhiteSpace(clientApp))
                query = query.Where(m => m.ClientApp == clientApp);
            if (!string.IsNullOrWhiteSpace(recipient))
                query = query.Where(m => m.Recipient == recipient);
            if (fromDate.HasValue)
                query = query.Where(m => m.CreatedAt >= fromDate.Value);
            if (toDate.HasValue)
                query = query.Where(m => m.CreatedAt <= toDate.Value);

            var totalCount = await query.CountAsync(ct);

            var items = await query
                .OrderByDescending(m => m.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return Results.Ok(new MessageHistoryResponse
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                Items = items
            });
        })
        .WithName("GetMessageHistory")
        .WithDescription("Query sent messages across all channels (SMS + Email)");

        return group;
    }
}
