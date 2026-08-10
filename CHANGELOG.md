# Smoothing Expanded changelog

## [1.0.0] - 2026-08-10

### Settings interface

- Replaced the fixed-height collapsible settings screen with compact Home,
  Smoothing, Vanilla Surfaces, Chunk Construction and Save Safety pages.
- Added selected-tab highlighting, independent dynamically sized page scrolling,
  scoped category resets, and a guarded Home-page Reset All action.
- Kept save/uninstall preparation separate from ordinary settings and made
  Harmony feature availability clearer.

### Compatibility

- Existing settings keys, defaults, validation, migrations and saves remain
  compatible. Reset All never starts save conversion, and Save Safety remains
  available independently of the current chunk-construction setting.

## [0.49.1] - 2026-08-10

### Localisation

- Added complete English fallback language trees for every RimWorld 1.6
  language, including the conditional Odyssey tree.
- Untranslated game languages now display English rather than raw translation
  keys or missing settings text.
- Preserved the existing Simplified Chinese, German, Russian and Spanish
  translations.

### Development

- Extended the validator to require all supported language folders in both the
  main and Odyssey localisation trees, in addition to XML, key and placeholder
  parity checks.

## [0.49.0] - 2026-08-08

### Save and uninstall safety

- The uninstall-preparation tool now finds chunk-built walls inside minified
  wrappers, inventories, transport containers, caravans and other nested
  `IThingHolder` containers.
- Added a final save audit. If custom walls or floors remain unresolved, the
  tool reports failure and does not reset chunk-construction settings.
- Tightened vanilla smoothed-floor resolution. Exact implied defs and explicit
  rough-to-smoothed relationships are preferred; ambiguous fallbacks are
  rejected and retried instead of silently selecting another mod's terrain.

### Compatibility and reliability

- Custom chunk-built walls and temporary vanilla-result wall proxies are now
  excluded from RimWorld's standard map-generation scatter pool.
- Independent floor speed and floor wealth now expose separate Harmony
  capability checks. A changed patch target disables only the affected option.
- Hardened Architect and render-cache reflection paths with cached lookups,
  null guards, one-time failure logging and safe fallback behaviour.
- Corrected the MinifyEverything Workshop link to item `872762753`.
- Added Core and every official DLC as optional `loadAfter` hints for RimWorld's
  built-in mod auto-sort. No DLC dependency was added.

### Performance and development

- Speed and beauty definition updates now run only when their settings change,
  rather than during every settings-window draw.
- Added reproducible Visual Studio projects for the main and optional Harmony
  assemblies, configurable local reference paths, build documentation, and an
  XML/localisation validation script.
- The validator checks all XML plus translation key and placeholder parity in
  the main and Odyssey-specific language trees.

### Compatibility

- Existing saves and constructed surfaces remain compatible. A normal RimWorld
  restart is required after updating the assemblies.
