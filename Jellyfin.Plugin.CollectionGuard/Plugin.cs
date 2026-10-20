using Jellyfin.Plugin.CollectionGuard.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.CollectionGuard;

/// <summary>
/// Collection Guard: keeps restricted libraries out of collections and playlists that other users can see,
/// and keeps user access to restricted libraries in line with the configuration.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Collection Guard";

    public override Guid Id => Guid.Parse("e604e2a0-d9f5-4560-b021-83415105b21c");

    public override string Description =>
        "Keeps restricted libraries (e.g. private libraries or content not meant for children) out of shared collections and playlists, removes empty collections and locks restricted libraries for everyone but allowed users.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "CollectionGuard",
                DisplayName = "Collection Guard",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            }
        };
    }
}
