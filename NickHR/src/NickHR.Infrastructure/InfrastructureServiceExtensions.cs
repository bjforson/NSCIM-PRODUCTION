using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NickERP.Platform.Tenancy;
using NickHR.Core.Interfaces;
using NickHR.Infrastructure.Data;
using NickHR.Infrastructure.Repositories;

namespace NickHR.Infrastructure;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // NICKSCAN ERP — Phase 1 multi-tenancy
        services.AddNickERPTenancy();

        services.AddDbContext<NickHRDbContext>((sp, options) =>
        {
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsAssembly(typeof(NickHRDbContext).Assembly.FullName))
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            // No-op until entities implement ITenantOwned, but wires the plumbing now.
            options.AddInterceptors(sp.GetRequiredService<TenantOwnedEntityInterceptor>());
        });

        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequiredLength = 8;
            options.User.RequireUniqueEmail = true;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
        })
        .AddEntityFrameworkStores<NickHRDbContext>()
        .AddDefaultTokenProviders();

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
