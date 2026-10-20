"""Adds the release zips of a tag to manifest.json (Jellyfin plugin repository format)."""
import datetime
import glob
import hashlib
import json
import os
import re
import sys

dist, repo, tag = sys.argv[1], sys.argv[2], sys.argv[3]
path = "manifest.json"
manifest = json.load(open(path, encoding="utf-8")) if os.path.exists(path) else []
if not manifest:
    manifest = [{
        "guid": "e604e2a0-d9f5-4560-b021-83415105b21c",
        "name": "Collection Guard",
        "description": "Keeps restricted libraries out of shared collections and playlists, removes empty "
                       "collections and locks restricted libraries for everyone but allowed users.",
        "overview": "Keep restricted libraries out of shared collections",
        "owner": repo.split("/")[0],
        "category": "General",
        "versions": [],
    }]
plugin = manifest[0]
now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
for z in sorted(glob.glob(os.path.join(dist, "collection-guard_*_jellyfin-*.zip"))):
    name = os.path.basename(z)
    m = re.match(r"collection-guard_([\d.]+)_jellyfin-([\d.]+)\.zip", name)
    version, jf = m.group(1), m.group(2)
    abi = ".".join(jf.split(".")[:2]) + ".0.0"
    md5 = hashlib.md5(open(z, "rb").read()).hexdigest()
    plugin["versions"] = [v for v in plugin["versions"] if not (v["version"] == version and v["targetAbi"] == abi)]
    plugin["versions"].insert(0, {
        "version": version,
        "changelog": f"See https://github.com/{repo}/releases/tag/{tag}",
        "targetAbi": abi,
        "sourceUrl": f"https://github.com/{repo}/releases/download/{tag}/{name}",
        "checksum": md5,
        "timestamp": now,
    })
json.dump(manifest, open(path, "w", encoding="utf-8"), indent=2, ensure_ascii=False)
print(f"manifest.json: {len(plugin['versions'])} versions")
