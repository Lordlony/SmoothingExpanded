version 0.49.1

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

Localisation

- Added complete English fallback files for every RimWorld 1.6 language,
  including Odyssey-only text when Odyssey is active.
- Selecting an untranslated game language now displays English text instead of
  raw translation keys or broken settings labels.
- Existing Simplified Chinese, German, Russian and Spanish translations remain
  available unchanged.

Performance, packaging and source

- Definition updates now run only when relevant settings actually change.
- Added reproducible Visual Studio projects, build instructions and automated
  XML/localisation validation.
- The validator now requires every supported language fallback folder and
  continues to check XML, translation keys and formatting placeholders.
- The full development source, documentation and reuse terms are now publicly
  available at https://github.com/Lordlony/SmoothingExpanded. The Steam upload
  folder now contains only the files needed to play and publish the mod.


Existing saves remain compatible. Restart RimWorld after updating.
