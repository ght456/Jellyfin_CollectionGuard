# Collection Guard for Jellyfin

Jellyfin hides a **library** from users who have no access to it – but **collections and playlists** stay visible,
even when every item in them comes from a library the user may not see. Collections can also end up nested in
each other or mix restricted and unrestricted items (e.g. a kids' collection that suddenly contains collections
from a private library). **Collection Guard** fixes that permanently:

| | |
|---|---|
| **Untangle** | removes collections nested in collections; splits collections that mix restricted and unrestricted items (the smaller side is removed – only the link, never the media) |
| **Empty containers** | deletes empty collections and playlists (optional) |
| **Tag** | tags every collection/playlist with the libraries its items come from (`lib:Movies`, `lib:Private` …) |
| **Lock users** | every user except the allowed ones loses access to restricted libraries (administrators included) and gets the tags of all libraries they may not see as *blocked tags* – so collections from those libraries disappear for them |

Playlists that mix restricted and unrestricted items are reported, not changed (they belong to users).

Runs **after every library scan**, **on startup**, **every 6 hours** and – optionally – **60 s after a collection
is created or changed**. A fresh install starts in **dry-run mode**: it only reports what it would do.

## Install

1. Dashboard → Plugins → **Repositories** → add
   `https://raw.githubusercontent.com/ght456/Jellyfin_CollectionGuard/main/manifest.json`
2. Catalog → **Collection Guard** → install → restart Jellyfin.
3. Dashboard → Plugins → Collection Guard: tick the **restricted libraries** and the **users allowed** to see them,
   press **Run now**, read the report. When it looks right, switch off **dry run**.

Supported: Jellyfin 10.10, 10.11 and 12.x.

### Manual install

Download `manual-install_CollectionGuard_<version>_jellyfin-<your version>.zip` from the latest release, extract it
**into** `<jellyfin data>/plugins/` (it already contains the folder `CollectionGuard_<version>`) and restart Jellyfin.
Windows: `C:\ProgramData\Jellyfin\Server\plugins\`.

## Build yourself

```
dotnet build Jellyfin.Plugin.CollectionGuard/Jellyfin.Plugin.CollectionGuard.csproj -c Release -p:JellyfinVersion=12.1.0
```
(10.10.x → .NET 8, 10.11.x → .NET 9, 12.x → .NET 10). Copy `Jellyfin.Plugin.CollectionGuard.dll` into
`<jellyfin data>/plugins/CollectionGuard_1.0.0.0/` and restart Jellyfin.

Releases: push a tag `v1.0.0` – GitHub Actions builds for every supported Jellyfin version, creates the release
and updates `manifest.json`.

---

## Deutsch (kurz)

Jellyfin versteckt Bibliotheken, aber nicht die **Sammlungen und Playlists** daraus. Collection Guard entflechtet
verschachtelte und gemischte Sammlungen, entfernt leere, markiert Sammlungen mit ihren Bibliotheken und sperrt
eingeschränkte Bibliotheken für alle außer den freigegebenen Benutzern – automatisch nach jedem Scan.
Einstellungen und Bericht auf Deutsch, wenn der Browser/Server auf Deutsch steht. Installation siehe oben.

## License

GPL-3.0 – like Jellyfin itself.
