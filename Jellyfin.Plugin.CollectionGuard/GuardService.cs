using System.Globalization;
using System.Text;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CollectionGuard;

/// <summary>Does the work: untangle, remove empty containers, tag, lock users.</summary>
public class GuardService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ILibraryManager _library;
    private readonly ICollectionManager _collections;
    private readonly IUserManager _users;
    private readonly ILogger<GuardService> _log;
    private bool _de;

    public GuardService(ILibraryManager library, ICollectionManager collections, IUserManager users,
        ILogger<GuardService> log)
    {
        _library = library;
        _collections = collections;
        _users = users;
        _log = log;
    }

    private string T(string de, string en) => _de ? de : en;

    public async Task RunAsync(string reasonDe, string reasonEn, IProgress<double>? progress, CancellationToken ct)
    {
        if (!await Gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            _log.LogInformation("Collection Guard is already running – skipped ({Reason})", reasonEn);
            return;
        }

        try
        {
            var lang = Plugin.Instance?.Configuration.Language ?? "auto";
            _de = lang == "de" || (lang == "auto" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de");
            await RunInnerAsync(T(reasonDe, reasonEn), progress, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Collection Guard failed");
            Save($"{DateTime.Now:yyyy-MM-dd HH:mm} – {T("FEHLER", "ERROR")}: {ex.Message}");
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task RunInnerAsync(string reason, IProgress<double>? progress, CancellationToken ct)
    {
        var cfg = Plugin.Instance!.Configuration;
        var dry = cfg.DryRun;
        var prefix = string.IsNullOrWhiteSpace(cfg.TagPrefix) ? "lib:" : cfg.TagPrefix;
        var rep = new StringBuilder();
        rep.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm} – {T("Anlass", "Trigger")}: {reason}" +
                       (dry ? T(" – TESTMODUS, nichts geändert", " – DRY RUN, nothing changed") : string.Empty));

        var libs = _library.GetVirtualFolders()
            .Where(v => !IsContainerLib(v.CollectionType?.ToString()))
            .Select(v => (Id: Guid.TryParse(v.ItemId, out var g) ? g : Guid.Empty, Name: v.Name ?? string.Empty))
            .Where(v => v.Id != Guid.Empty && v.Name.Length > 0)
            .ToList();
        var restricted = new HashSet<string>(cfg.RestrictedLibraries ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        rep.AppendLine(T("Eingeschränkte Bibliotheken: ", "Restricted libraries: ") +
                       (restricted.Count > 0 ? string.Join(", ", restricted) : T("(keine gewählt)", "(none selected)")));

        var libCache = new Dictionary<Guid, string[]>();
        string[] LibsOf(BaseItem item)
        {
            if (!libCache.TryGetValue(item.Id, out var names))
            {
                names = _library.GetCollectionFolders(item).Select(f => f.Name ?? string.Empty)
                    .Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                libCache[item.Id] = names;
            }

            return names;
        }

        bool IsRestricted(BaseItem item) => LibsOf(item).Any(restricted.Contains);
        bool IsOpen(BaseItem item) => LibsOf(item).Length > 0 && !IsRestricted(item);

        // 1) Untangle
        int untangled = 0, removedLinks = 0;
        if (cfg.Untangle)
        {
            foreach (var bs in GetAll<BoxSet>(BaseItemKind.BoxSet))
            {
                ct.ThrowIfCancellationRequested();
                var kids = bs.GetLinkedChildren().Where(k => k != null).ToList();
                var drop = kids.Where(IsContainer).ToList();
                var films = kids.Where(k => !IsContainer(k)).ToList();
                var r = films.Where(IsRestricted).ToList();
                var o = films.Where(IsOpen).ToList();
                if (r.Count > 0 && o.Count > 0)
                {
                    drop.AddRange(r.Count <= o.Count ? r : o);
                }

                if (drop.Count == 0)
                {
                    continue;
                }

                untangled++;
                removedLinks += drop.Count;
                rep.AppendLine($"  {T("Entflochten", "Untangled")}: „{bs.Name}“ – {T("raus", "removed")}: " +
                               string.Join(", ", drop.Take(5).Select(d => d.Name)) +
                               (drop.Count > 5 ? $" … (+{drop.Count - 5})" : string.Empty));
                if (!dry)
                {
                    await _collections.RemoveFromCollectionAsync(bs.Id, drop.Select(d => d.Id)).ConfigureAwait(false);
                }
            }
        }

        // Playlists belong to users: mixed ones are only reported
        foreach (var pl in GetAll<Playlist>(BaseItemKind.Playlist))
        {
            var films = pl.GetLinkedChildren().Where(k => k != null && !IsContainer(k)).ToList();
            if (films.Any(IsRestricted) && films.Any(IsOpen))
            {
                rep.AppendLine("  ! " + T($"Playlist „{pl.Name}“ mischt eingeschränkte und freie Inhalte – bitte von Hand trennen",
                                            $"Playlist “{pl.Name}” mixes restricted and unrestricted items – please split it manually"));
            }
        }

        progress?.Report(30);

        // 2) Empty containers
        int deleted = 0;
        if (cfg.DeleteEmpty)
        {
            foreach (var item in Containers())
            {
                ct.ThrowIfCancellationRequested();
                if (Leafs(item, new HashSet<Guid>()).Count > 0)
                {
                    continue;
                }

                deleted++;
                rep.AppendLine($"  {T("Leer, entfernt", "Empty, removed")}: " +
                               $"{(item is Playlist ? "Playlist" : T("Sammlung", "Collection"))} „{item.Name}“");
                if (!dry)
                {
                    _library.DeleteItem(item, new DeleteOptions { DeleteFileLocation = true });
                }
            }
        }

        progress?.Report(55);

        // 3) Tag containers with their source libraries
        int tagged = 0;
        if (cfg.LockUsers)
        {
            foreach (var item in Containers())
            {
                ct.ThrowIfCancellationRequested();
                var leafs = Leafs(item, new HashSet<Guid>());
                if (leafs.Count == 0)
                {
                    continue; // empty: never remove an existing lock
                }

                var want = leafs.SelectMany(LibsOf).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(n => prefix + n).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
                var tags = item.Tags ?? Array.Empty<string>();
                var have = tags.Where(t => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
                if (want.SequenceEqual(have, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                tagged++;
                if (!dry)
                {
                    item.Tags = tags.Where(t => !t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Concat(want).ToArray();
                    await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, ct).ConfigureAwait(false);
                }
            }
        }

        progress?.Report(80);

        // 4) Users: no access to restricted libraries, blocked tags for every library they cannot see
        int usersChanged = 0;
        if (cfg.LockUsers && restricted.Count > 0)
        {
            var allowedUsers = new HashSet<string>(cfg.AllowedUsers ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var rIds = libs.Where(l => restricted.Contains(l.Name)).Select(l => l.Id).ToHashSet();
            var openIds = libs.Where(l => !rIds.Contains(l.Id)).Select(l => l.Id).ToList();
#if JF11
            var users = _users.GetUsers().ToList();
#else
            var users = _users.Users.ToList();
#endif
            foreach (var user in users)
            {
                if (allowedUsers.Contains(user.Username))
                {
                    continue;
                }

                var policy = _users.GetUserDto(user).Policy;
                var allowed = policy.EnableAllFolders
                    ? new HashSet<Guid>(openIds)
                    : new HashSet<Guid>(policy.EnabledFolders ?? Array.Empty<Guid>());
                var hadRestricted = allowed.Overlaps(rIds);
                allowed.ExceptWith(rIds);
                var blocked = libs.Where(l => !allowed.Contains(l.Id)).Select(l => prefix + l.Name);
                var oldTags = (policy.BlockedTags ?? Array.Empty<string>()).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray();
                var newTags = oldTags.Where(t => !t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Concat(blocked).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray();
                if (!policy.EnableAllFolders && !hadRestricted && newTags.SequenceEqual(oldTags, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                usersChanged++;
                rep.AppendLine($"  {T("Benutzer", "User")} {user.Username}: " +
                               (hadRestricted ? T("Zugriff auf eingeschränkte Bibliotheken entfernt; ", "access to restricted libraries removed; ") : string.Empty) +
                               (policy.EnableAllFolders ? T("„alle Bibliotheken“ → alle freien; ", "“all libraries” → all unrestricted; ") : string.Empty) +
                               T("Sperr-Schlagwörter aktualisiert", "blocked tags updated"));
                if (!dry)
                {
                    policy.EnableAllFolders = false;
                    policy.EnabledFolders = allowed.ToArray();
                    policy.BlockedTags = newTags;
                    await _users.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);
                }
            }
        }

        progress?.Report(100);
        var header = rep.ToString().Split(Environment.NewLine)[0];
        rep.Remove(0, header.Length + Environment.NewLine.Length);
        rep.Insert(0, header + Environment.NewLine + T(
            $"Ergebnis: {untangled} Sammlungen entflochten ({removedLinks} Zuordnungen), {deleted} leere entfernt, {tagged} neu markiert, {usersChanged} Benutzer angepasst",
            $"Result: {untangled} collections untangled ({removedLinks} links), {deleted} empty removed, {tagged} re-tagged, {usersChanged} users updated")
            + Environment.NewLine);
        var text = rep.ToString();
        _log.LogInformation("Collection Guard:{NewLine}{Report}", Environment.NewLine, text);
        Save(text);
    }

    private static bool IsContainerLib(string? type)
    {
        var t = (type ?? string.Empty).ToLowerInvariant();
        return t is "boxsets" or "playlists";
    }

    private static bool IsContainer(BaseItem item) => item is BoxSet or Playlist;

    private IEnumerable<Folder> Containers()
        => GetAll<BoxSet>(BaseItemKind.BoxSet).Cast<Folder>().Concat(GetAll<Playlist>(BaseItemKind.Playlist)).ToList();

    private List<T> GetAll<T>(BaseItemKind kind)
        where T : BaseItem
        => _library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { kind }, Recursive = true })
            .OfType<T>().ToList();

    /// <summary>All media of a container, following nested collections (cycle-safe).</summary>
    private static List<BaseItem> Leafs(Folder container, HashSet<Guid> seen)
    {
        var res = new List<BaseItem>();
        if (!seen.Add(container.Id) || seen.Count > 500)
        {
            return res;
        }

        foreach (var k in container.GetLinkedChildren())
        {
            if (k is Folder f && IsContainer(k))
            {
                res.AddRange(Leafs(f, seen));
            }
            else if (k != null)
            {
                res.Add(k);
            }
        }

        return res;
    }

    private static void Save(string report)
    {
        var p = Plugin.Instance;
        if (p == null)
        {
            return;
        }

        p.Configuration.LastReport = report.Length > 20000 ? report[..20000] + "\n…" : report;
        p.SaveConfiguration();
    }
}
