# Plugin foundation

The plugin feature is intentionally disabled in the 0.0.3 line. The current
foundation only describes and inspects packages; it does not scan the plugin
directory, load assemblies, or execute third-party code.

## `.zsp` package shape

```text
example.zsp
├─ manifest.json
└─ Example.Plugin.dll
```

Example manifest:

```json
{
  "manifestVersion": 1,
  "id": "example.plugin",
  "name": "Example Plugin",
  "description": "A sample plugin.",
  "version": "1.0.0",
  "entry": {
    "assembly": "Example.Plugin.dll",
    "type": "Example.Plugin.EntryPoint"
  },
  "requires": {
    "pluginApi": ">=1.0.0 <2.0.0",
    "appVersion": ">=0.0.3"
  }
}
```

`PluginPackageService.Inspect` validates the manifest, entry assembly, version
constraints, HTTPS update URL, duplicate paths, archive traversal, file count,
and uncompressed size. `VerifySha256` can verify a package downloaded from a
plugin update endpoint.

## Planned order

1. Add a disabled-by-default plugin settings page that only displays inspection
   results.
2. Add install staging and quarantine, still without loading plugin code.
3. Add a permission model and an isolated host process.
4. Enable runtime loading only after the host APIs and rollback behavior have
   been tested end to end.
