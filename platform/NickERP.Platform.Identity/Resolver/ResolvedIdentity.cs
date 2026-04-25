namespace NickERP.Platform.Identity.Resolver;

/// <summary>
/// What every NickERP service sees after asking the identity layer
/// "who is this caller, and what may they do?". Returned from
/// <see cref="IIdentityResolver"/>.
/// </summary>
public sealed class ResolvedIdentity
{
    /// <summary>Canonical id of the human user. Null for service-token callers.</summary>
    public Guid? UserId { get; init; }

    /// <summary>Canonical id of the service-token identity. Null for human callers.</summary>
    public Guid? ServiceTokenId { get; init; }

    /// <summary>Lower-cased email for human callers. Null for service callers.</summary>
    public string? Email { get; init; }

    /// <summary>Friendly label for either kind of caller — used in audit log messages.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Whether this caller is a human user vs a machine/service.</summary>
    public IdentityKind Kind { get; init; }

    /// <summary>Tenant the caller belongs to — propagates onto every DB query downstream.</summary>
    public long TenantId { get; init; } = 1;

    /// <summary>Effective scope set: union of UserScope grants minus revoked/expired.</summary>
    public IReadOnlySet<string> Scopes { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns true when this identity carries the named scope.</summary>
    public bool HasScope(string code) => Scopes.Contains(code);

    /// <summary>Returns true when this identity carries any of the named scopes.</summary>
    public bool HasAnyScope(IEnumerable<string> codes) => codes.Any(c => Scopes.Contains(c));
}

public enum IdentityKind
{
    Human = 0,
    ServiceToken = 1,
    Dev = 2
}
