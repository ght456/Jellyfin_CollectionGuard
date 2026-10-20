using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.CollectionGuard.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Restricted libraries (by name). Only users in <see cref="AllowedUsers"/> may see them.</summary>
    public string[] RestrictedLibraries { get; set; } = Array.Empty<string>();

    /// <summary>Users (user names) allowed to see restricted libraries. Everyone else loses access – administrators included.</summary>
    public string[] AllowedUsers { get; set; } = Array.Empty<string>();

    /// <summary>Remove collections nested in collections and split collections mixing restricted and unrestricted items.</summary>
    public bool Untangle { get; set; } = true;

    /// <summary>Delete empty collections and playlists (the container only, never media).</summary>
    public bool DeleteEmpty { get; set; } = false;

    /// <summary>Tag collections/playlists with their source libraries and update users' library access + blocked tags.</summary>
    public bool LockUsers { get; set; } = true;

    /// <summary>Check 60 s after a collection was created or changed.</summary>
    public bool Realtime { get; set; } = true;

    /// <summary>Only report, change nothing. On by default so a fresh install never changes anything unseen.</summary>
    public bool DryRun { get; set; } = true;

    public string TagPrefix { get; set; } = "lib:";

    /// <summary>Report language: "auto" (server UI culture), "en" or "de".</summary>
    public string Language { get; set; } = "auto";

    public string LastReport { get; set; } = string.Empty;
}
