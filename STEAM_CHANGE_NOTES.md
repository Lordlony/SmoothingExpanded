version 0.49.0

Save and uninstall safety

- Prepare loaded save for uninstall now converts minified or transported
  chunk-built walls, including walls in inventories, containers and caravans.
- Added a final audit which prevents a false success report and keeps settings
  enabled if unresolved custom surfaces remain.
- Vanilla-result floors now prefer exact vanilla definitions and explicit
  smoothing relationships. Ambiguous modded matches are rejected and retried.

Fixes and compatibility

- Prevented custom wall definitions from entering RimWorld's standard
  map-generation scatter pool.
- Added separate Harmony capability checks for independent floor speed and
  floor wealth.
- Hardened Architect-menu and render-cache reflection with safe fallbacks and
  one-time failure reporting.
- Corrected the MinifyEverything Workshop link.
- Added optional Core/DLC load-order hints for RimWorld's built-in auto-sort.
  No DLC dependency was added.

Performance and maintenance

- Definition updates now run only when relevant settings actually change.
- Added reproducible Visual Studio build projects, build instructions, and
  automated XML/localisation validation.

Existing saves remain compatible. Restart RimWorld after updating.
