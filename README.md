# Smoothing Expanded

Source repository for the RimWorld 1.6 mod by Lordlony. It contains the C#
source, XML definitions, localisations, optional integrations, and validation
tooling needed to develop the mod. Compiled DLLs, Steam publication state, and
Workshop artwork remain in the local release package and are intentionally not
versioned here.

A RimWorld 1.6 mod that:

- adds an adjustable wall/floor smoothing-speed multiplier;
- optionally makes smoothing effectively instant;
- optionally assigns an adjustable value to vanilla smoothed walls;
- optionally assigns an adjustable value to vanilla smoothed floors; and
- optionally adds chunk-built smooth walls and floors for all five Core stone types;
- adds matching Vacstone construction automatically when Odyssey is active;
- leaves vanilla constructed walls, vanilla floors and unsmoothed rock unchanged.

Version 0.49.0. Created by Lordlony, with development assistance and code review
from OpenAI Codex.

## Installation

Copy the `SmoothingExpanded` folder into RimWorld's local `Mods` folder,
then enable **Smoothing Expanded** after Core and any DLC. If Harmony is
active, place this mod after Harmony.

Harmony is optional. Without it, smoothing speed and smoothed-wall wealth remain
available; the floor-wealth option is disabled with a tooltip explaining why.
The mod has no HugsLib, XML Extensions or DLC dependency. Adjust the
multiplier under **Options > Mod settings > Smoothing Expanded**. The
slider ranges from 0.25× to 10× and defaults to 2×.

## Technical notes

The speed setting changes `SmoothingSpeed/defaultBaseValue` at runtime.
Construction skill, manipulation, sight and global work speed continue to
modify the resulting speed normally. Settings persist in RimWorld's normal mod
configuration and take effect immediately when changed.

Instant smoothing is a separate cheat-style option and defaults to off.

After all explicit and runtime-generated definitions exist, the mod follows
every rough wall's `smoothedThing` reference and every explicit rough floor's
`smoothedTerrain` reference. Core's runtime-generated smooth floors do not
retain that reverse relationship, so they are additionally recognized by the
generated smooth naming pattern plus the absence of any construction cost.
It then sets those exact resulting surfaces to zero market value without
affecting constructed smooth-named flooring.

Both wealth overrides default to off. The wall option changes the detected wall
definitions directly and restores their startup values when disabled; its Harmony
fallback also covers ordinary market-value queries. The experimental floor option
updates only the detected smoothed-floor entries in RimWorld's cached terrain
market-value table before a normal recount. RimWorld then calculates its floor,
building and total wealth normally. This is also the table read directly by the
Visible Wealth mod. Disabling the option restores each entry from its current
terrain definition on the next recount.
When an option is unchecked its adjustment is skipped completely. The Harmony
integration assembly is not loaded at all when Harmony is absent.

The floor option runs last where Harmony ordering permits, but another mod can
still recalculate or replace wealth afterwards. It is therefore recommended off
for maximum compatibility with wealth, raid-point and difficulty mods.

The settings are grouped into collapsible speed, vanilla-surface, chunk-built,
and uninstall-safety sections. **Reset all settings to defaults** restores both
smoothing speeds to 2×, disables instant smoothing and all wealth/beauty
overrides, and disables chunk construction.

Definition-level speed and beauty updates are applied only when their relevant
settings change, rather than during every settings-window draw. Optional Harmony
features are detected separately: an unavailable floor-speed patch no longer
misreports floor-wealth support, or vice versa.

## Optional chunk construction

When enabled, five Core walls and five Core floors are exposed for sandstone,
granite, limestone, slate and marble. With Odyssey active, a Vacstone wall and
floor are added conditionally without making the DLC a dependency. Every tile
costs one matching raw chunk, requires no research, and uses RimWorld's normal
material-return rules. Chunk-built walls are deconstructible and chunk-built
floors are removable with Remove Floor. Constructed walls use the live structural
properties and work of the equivalent stone-block wall, weigh as much as the
matching chunk, and are not airtight. Constructed floors use normal stone-tile
work and balance values. Beauty and wealth follow the corresponding vanilla or
stone construction by default. Optional sliders can override wall and floor
values separately; changing floor wealth requires Harmony because RimWorld
caches terrain values. Wealth sliders use 0.5 increments from 0 to 100. Beauty
sliders use whole-number increments from -10 to 20.

Because every surface costs one chunk, vanilla's percentage and random rounding
can return either one chunk or none. Dedicated mods such as Configurable
Deconstruct Percentage may control the return; Smoothing Expanded deliberately
does not override them.

The result mode defaults to **Chunk-built: deconstructible walls and removable
floors**. It may instead show only **Vanilla: mineable walls and permanent
floors**, or show both catalogues. Vanilla-result choices use a short-lived construction helper to consume the
chunk and perform normal work, then replace themselves on the following tick
with the genuine vanilla smoothed wall or terrain. These results have vanilla
durability and blending, work with mods as vanilla assets, and are inherently
safe if Smoothing Expanded is removed. They cannot return the consumed chunk.

When Unsmooth Surface is active, a conditional compatibility patch uses that
mod's published extension to turn each chunk-built surface into its matching
vanilla rough terrain or natural rock wall. This creates a vanilla asset rather
than refunding a chunk, matching the meaning of Unsmooth Surface's work order.

The new surfaces reuse Core's smooth-rock wall atlas, smooth-stone floor texture,
and stone colors. Constructed smooth floors also copy the natural smooth floor's
Beauty, paintability and pollution-overlay behaviour while retaining
normal tile work and other balance values. Walls always use Core's wall/rock
neighbour connections. The wall choices share one standard Structure dropdown
and the floors share their own standard Floors dropdown. Left-click availability
follows matching chunks on the current map. TD Enhancement Pack - Continued may
provide a right-click complete catalogue. Catalogue and constructed-wealth
settings are applied during
definition startup and therefore require a RimWorld restart. Definitions remain
loaded while hidden so existing constructions remain safe in saves.
Custom construction walls and their short-lived vanilla-result proxies are
explicitly excluded from RimWorld's ordinary map-generation scatter pool.

Constructed floors use exact one-cell edges. RimWorld's natural FadeRough
renderer connects terrain by TerrainDef identity rather than matching appearance;
because a deconstructible constructed floor and its natural counterpart must be
different definitions, enabling natural fades causes their transition meshes to
overwrite neighbouring graphics unpredictably. Avoiding that would require an
invasive global rendering/equality patch, so the stable vanilla hard-edge mode is
used instead.

## Save conversion and uninstalling

Disabling chunk construction is safe because its definitions remain loaded.
Removing the entire mod while chunk-built surfaces remain is not safe: missing
custom floors can become soil and custom walls can disappear.

Load the save and use **Prepare loaded save for uninstall** in the mod
settings first. After confirmation, the tool processes every loaded map, changes
chunk-built floors and walls into their matching vanilla smoothed counterparts,
and recursively checks packed walls in map containers, pawn inventories,
caravans and other world-level holders. It preserves wall health, faction and
paint where possible, preserves floor paint, and cancels unfinished blueprints
and frames. A final audit refuses to report success or reset the construction
settings while unresolved custom surfaces remain. Save only after a successful
conversion report. The conversion may also be used without uninstalling the mod.

Vanilla-result floor resolution first uses the exact Core/Odyssey implied def,
then RimWorld's explicit rough-to-smoothed relationship. An ambiguous modded
fallback is rejected; the completed helper remains in place and retries instead
of silently converting to the wrong terrain.

Existing smoothed surfaces should update their market value after definitions
are reloaded. Back up an important save before changing its active mod list.

## Localisation

English, Simplified Chinese, German, Russian and Spanish keyed strings and
DefInjected labels/descriptions are included under `Languages`. The non-English
localisations are AI translations and welcome corrections from native speakers.
Translators can copy the English folder, rename it to RimWorld's language folder
name, and translate the text values without changing keys or XML element names.
Odyssey-only Vacstone translations are kept under
`Optional/OptionalOdyssey/Languages` so Core-only games do not receive missing-definition
translation warnings.

## Building and validation

Buildable Visual Studio project files are included for both assemblies. See
`BUILDING.md` for the reproducible command and configurable RimWorld/Harmony
reference paths. `Tools/Validate.ps1` parses every XML file and checks translation
key and formatting-placeholder parity, including the optional Odyssey tree.
