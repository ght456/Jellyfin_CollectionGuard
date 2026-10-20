using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller;

namespace Jellyfin.Plugin.CollectionGuard;

/// <summary>Registers the plugin services.</summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<GuardService>();
        serviceCollection.AddHostedService<RealtimeWatcher>();
    }
}

/// <summary>Scheduled task (Dashboard → Scheduled Tasks → Collection Guard).</summary>
public class GuardTask : IScheduledTask
{
    private readonly GuardService _svc;

    public GuardTask(GuardService svc) => _svc = svc;

    public string Name => "Collection Guard: check collections";

    public string Key => "CollectionGuardCheck";

    public string Description => "Untangle collections, remove empty ones, lock restricted libraries for users who may not see them.";

    public string Category => "Collection Guard";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _svc.RunAsync("geplante Aufgabe", "scheduled task", progress, cancellationToken);

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
#if JF11
            new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(6).Ticks },
            new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger }
#else
            new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerInterval, IntervalTicks = TimeSpan.FromHours(6).Ticks },
            new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerStartup }
#endif
        };
    }
}

/// <summary>Runs after every library scan.</summary>
public class AfterScan : ILibraryPostScanTask
{
    private readonly GuardService _svc;

    public AfterScan(GuardService svc) => _svc = svc;

    public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        => _svc.RunAsync("nach Bibliotheks-Scan", "after library scan", progress, cancellationToken);
}

/// <summary>Reacts 60 s after a collection is created or items are added.</summary>
public class RealtimeWatcher : IHostedService, IDisposable
{
    private readonly ICollectionManager _collections;
    private readonly GuardService _svc;
    private readonly ILogger<RealtimeWatcher> _log;
    private Timer? _timer;

    public RealtimeWatcher(ICollectionManager collections, GuardService svc, ILogger<RealtimeWatcher> log)
    {
        _collections = collections;
        _svc = svc;
        _log = log;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _collections.CollectionCreated += OnChange;
        _collections.ItemsAddedToCollection += OnChange;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _collections.CollectionCreated -= OnChange;
        _collections.ItemsAddedToCollection -= OnChange;
        _timer?.Dispose();
        return Task.CompletedTask;
    }

    private void OnChange(object? sender, EventArgs e)
    {
        if (Plugin.Instance?.Configuration.Realtime != true)
        {
            return;
        }

        _timer?.Dispose();
        _timer = new Timer(
            _ => _ = _svc.RunAsync("Sammlung geändert", "collection changed", null, CancellationToken.None),
            null,
            TimeSpan.FromSeconds(60),
            Timeout.InfiniteTimeSpan);
        _log.LogDebug("Collection Guard: collection changed – check in 60 s");
    }

    public void Dispose() => _timer?.Dispose();
}
