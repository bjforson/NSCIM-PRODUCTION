using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Markdig;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NickScanWebApp.New.Services.Permissions;

namespace NickScanWebApp.New.Services.UserManual
{
    /// <summary>
    /// v2.15.0 — loads the user-manual markdown corpus from
    /// <c>wwwroot/user-manual/</c>, parses YAML-lite frontmatter, filters the
    /// table-of-contents by the current user's permissions, and renders
    /// bodies to HTML via Markdig.
    ///
    /// Design notes:
    /// <list type="bullet">
    ///   <item>File-system-backed, not DB. Ships with the WebApp; content
    ///   changes via git + redeploy.</item>
    ///   <item>Frontmatter is parsed by a tiny hand-rolled parser — five keys
    ///   (title / category / order / requires / updated / version), no need
    ///   for the YamlDotNet dependency.</item>
    ///   <item>Loaded once at startup into an in-memory index. Hot-reload on
    ///   file change would be a nice-to-have but not in scope for v1.</item>
    ///   <item>Per-section gating inside a single doc via HTML comments
    ///   <c>&lt;!-- requires: PagesImageAnalysisAudit --&gt; ... &lt;!-- /requires --&gt;</c>
    ///   lets one doc serve multiple roles without duplicating content.</item>
    /// </list>
    /// </summary>
    public class UserManualService
    {
        private readonly ILogger<UserManualService> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly MarkdownPipeline _pipeline;
        private readonly object _loadLock = new();
        private IReadOnlyList<UserManualDoc>? _docs;

        public UserManualService(ILogger<UserManualService> logger, IWebHostEnvironment env)
        {
            _logger = logger;
            _env = env;
            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()     // tables, task lists, fenced code, autolinks
                .UseSoftlineBreakAsHardlineBreak()
                .UseEmojiAndSmiley()
                .Build();
        }

        /// <summary>Lazy one-shot load of the manual corpus.</summary>
        public IReadOnlyList<UserManualDoc> GetAllDocs()
        {
            if (_docs != null) return _docs;
            lock (_loadLock)
            {
                if (_docs != null) return _docs;
                _docs = LoadFromDisk();
            }
            return _docs;
        }

        /// <summary>
        /// ToC filtered to docs whose <see cref="UserManualDoc.RequiresPermissions"/>
        /// the current user satisfies. Empty requires → visible to any
        /// authenticated user.
        /// </summary>
        public IReadOnlyList<UserManualDoc> GetVisibleDocs(PermissionGuard guard, PermissionId[]? rolePreviewOverride = null)
        {
            bool HasPermission(string permId)
            {
                var resolved = ResolvePermissionAlias(permId);
                if (rolePreviewOverride != null)
                {
                    // Admin preview mode: pretend the user has exactly the
                    // selected permissions. Everything else returns false.
                    return rolePreviewOverride.Any(p => string.Equals(p.Value, resolved, StringComparison.OrdinalIgnoreCase));
                }
                return guard.Can(new PermissionId(resolved), context: "UserManual");
            }

            var visible = new List<UserManualDoc>();
            foreach (var doc in GetAllDocs())
            {
                if (doc.RequiresPermissions.Length == 0)
                {
                    visible.Add(doc);
                    continue;
                }
                // OR semantics: the doc is visible if the user has ANY of the
                // listed permissions. Analysts with ImageAnalysisView see docs
                // tagged [ImageAnalysisView, ImageAnalysisAudit] even though
                // they don't have the Audit bit.
                if (doc.RequiresPermissions.Any(HasPermission))
                {
                    visible.Add(doc);
                }
            }
            return visible;
        }

        /// <summary>Look up one doc by slug (case-insensitive). Returns null
        /// when the slug doesn't exist OR the caller isn't permitted.</summary>
        public UserManualDoc? GetDoc(string slug, PermissionGuard guard, PermissionId[]? rolePreviewOverride = null)
        {
            var doc = GetAllDocs().FirstOrDefault(
                d => string.Equals(d.Slug, slug, StringComparison.OrdinalIgnoreCase));
            if (doc == null) return null;

            // Permission check first — don't leak existence to unprivileged users.
            if (doc.RequiresPermissions.Length > 0)
            {
                bool HasPermission(string permId)
                {
                    var resolved = ResolvePermissionAlias(permId);
                    return rolePreviewOverride != null
                        ? rolePreviewOverride.Any(p => string.Equals(p.Value, resolved, StringComparison.OrdinalIgnoreCase))
                        : guard.Can(new PermissionId(resolved), context: "UserManual");
                }
                if (!doc.RequiresPermissions.Any(HasPermission))
                {
                    return null;
                }
            }
            return doc;
        }

        /// <summary>
        /// Render the doc's body Markdown to HTML, with per-section gating
        /// applied. Sections wrapped in
        /// <c>&lt;!-- requires: PermissionA,PermissionB --&gt; ... &lt;!-- /requires --&gt;</c>
        /// are stripped when the user doesn't satisfy any listed permission.
        /// </summary>
        public string RenderBody(UserManualDoc doc, PermissionGuard guard, PermissionId[]? rolePreviewOverride = null)
        {
            bool HasPermission(string permId)
            {
                var resolved = ResolvePermissionAlias(permId);
                return rolePreviewOverride != null
                    ? rolePreviewOverride.Any(p => string.Equals(p.Value, resolved, StringComparison.OrdinalIgnoreCase))
                    : guard.Can(new PermissionId(resolved), context: "UserManual");
            }

            var filteredBody = FilterSectionsByPermissions(doc.Body, HasPermission);
            return Markdown.ToHtml(filteredBody, _pipeline);
        }

        // ── Internals ────────────────────────────────────────────────────

        private IReadOnlyList<UserManualDoc> LoadFromDisk()
        {
            var manualDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "user-manual");
            if (!Directory.Exists(manualDir))
            {
                _logger.LogWarning("[UserManual] No user-manual directory at {Path}", manualDir);
                return Array.Empty<UserManualDoc>();
            }

            var docs = new List<UserManualDoc>();
            foreach (var file in Directory.EnumerateFiles(manualDir, "*.md", SearchOption.AllDirectories))
            {
                try
                {
                    var text = File.ReadAllText(file);
                    var (frontmatter, body) = SplitFrontmatter(text);
                    var meta = ParseFrontmatter(frontmatter);

                    var slug = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    docs.Add(new UserManualDoc
                    {
                        Slug         = slug,
                        Title        = meta.GetValueOrDefault("title") ?? slug,
                        Category     = meta.GetValueOrDefault("category") ?? "Misc",
                        Order        = int.TryParse(meta.GetValueOrDefault("order"), out var o) ? o : 999,
                        RequiresPermissions = ParseList(meta.GetValueOrDefault("requires")),
                        UpdatedIso   = meta.GetValueOrDefault("updated") ?? "",
                        Version      = meta.GetValueOrDefault("version") ?? "",
                        Body         = body,
                        SourcePath   = file,
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[UserManual] Failed to load {File}", file);
                }
            }

            var sorted = docs
                .OrderBy(d => d.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Order)
                .ThenBy(d => d.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _logger.LogInformation("[UserManual] Loaded {Count} docs from {Dir}", sorted.Count, manualDir);
            return sorted;
        }

        private static (string frontmatter, string body) SplitFrontmatter(string text)
        {
            // File shape:
            //   ---\n
            //   key: value\n
            //   ...
            //   ---\n
            //   # ... Markdown body ...
            // Any file without a leading --- is treated as all-body.
            if (!text.StartsWith("---"))
            {
                return ("", text);
            }
            // Find closing --- on its own line.
            var match = Regex.Match(text, @"^---\s*\n(?<fm>.+?)\n---\s*\n(?<body>.*)$",
                                    RegexOptions.Singleline);
            if (!match.Success)
            {
                return ("", text);
            }
            return (match.Groups["fm"].Value, match.Groups["body"].Value);
        }

        private static Dictionary<string, string> ParseFrontmatter(string frontmatter)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(frontmatter)) return map;

            foreach (var rawLine in frontmatter.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var key = line.Substring(0, colon).Trim();
                var val = line.Substring(colon + 1).Trim();
                // Strip optional quotes, strip brackets for list-style values (we re-parse in ParseList).
                if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
                {
                    val = val.Substring(1, val.Length - 2);
                }
                map[key] = val;
            }
            return map;
        }

        private static string[] ParseList(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
            // Accept formats: [A, B, C]  or  A, B, C
            var inner = value.Trim();
            if (inner.StartsWith("[") && inner.EndsWith("]"))
            {
                inner = inner.Substring(1, inner.Length - 2);
            }
            return inner
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0)
                .ToArray();
        }

        // ── Permission alias resolution ──────────────────────────────────
        //
        // Frontmatter authors (and the per-section gates) use friendly
        // names like "Pages.ImageAnalysisView" so the markdown stays
        // readable. The real permission strings the backend checks are
        // kebab-ish ("pages.imageanalysis.view"). We reflect over
        // PermissionIds once at class-init to build the mapping, so
        // adding a new permission never requires editing this file.

        private static readonly Lazy<IReadOnlyDictionary<string, string>> _aliasMap =
            new(BuildAliasMap);

        private static IReadOnlyDictionary<string, string> BuildAliasMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Top-level PermissionIds.* properties (legacy shortcuts).
            foreach (var prop in typeof(PermissionIds).GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (prop.PropertyType != typeof(PermissionId)) continue;
                var value = (PermissionId)prop.GetValue(null)!;
                map[prop.Name] = value.Value;
            }

            // Nested classes (Pages, Images, Containers) — the canonical
            // authoring surface. "Pages.ImageAnalysisView" resolves here.
            foreach (var nested in typeof(PermissionIds).GetNestedTypes(BindingFlags.Public))
            {
                foreach (var prop in nested.GetProperties(BindingFlags.Public | BindingFlags.Static))
                {
                    if (prop.PropertyType != typeof(PermissionId)) continue;
                    var value = (PermissionId)prop.GetValue(null)!;
                    map[$"{nested.Name}.{prop.Name}"] = value.Value;
                }
            }
            return map;
        }

        /// <summary>
        /// Resolve a friendly permission alias ("Pages.ImageAnalysisView")
        /// into the real permission string ("pages.imageanalysis.view").
        /// Unknown aliases pass through unchanged, so raw permission
        /// strings in the frontmatter also work.
        /// </summary>
        private static string ResolvePermissionAlias(string alias) =>
            _aliasMap.Value.TryGetValue(alias, out var real) ? real : alias;

        /// <summary>
        /// Strip sections gated by <c>&lt;!-- requires: X,Y --&gt; ... &lt;!-- /requires --&gt;</c>
        /// comments when the current user satisfies none of the listed permissions.
        /// Comments are HTML, survive Markdown rendering without being shown.
        /// </summary>
        private static string FilterSectionsByPermissions(string body, Func<string, bool> hasPermission)
        {
            // Regex matches the whole gated block including opening/closing comments.
            // Non-greedy so multiple gated sections in a single doc work independently.
            var pattern = @"<!--\s*requires:\s*(?<perms>[^-]+?)\s*-->(?<content>.*?)<!--\s*/requires\s*-->";
            return Regex.Replace(body, pattern, m =>
            {
                var perms = m.Groups["perms"].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(p => p.Length > 0)
                    .ToArray();
                if (perms.Length == 0) return m.Groups["content"].Value;
                return perms.Any(hasPermission) ? m.Groups["content"].Value : "";
            }, RegexOptions.Singleline);
        }
    }

    /// <summary>Parsed + metadata-indexed user-manual doc.</summary>
    public sealed class UserManualDoc
    {
        public required string Slug { get; init; }
        public required string Title { get; init; }
        public required string Category { get; init; }
        public int Order { get; init; }
        public required string[] RequiresPermissions { get; init; }
        public string UpdatedIso { get; init; } = "";
        public string Version { get; init; } = "";
        public required string Body { get; init; }
        public required string SourcePath { get; init; }
    }
}
