using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

[assembly: InternalsVisibleTo("SmoothingExpanded.Harmony")]

namespace Lordlony.SmoothingExpanded
{
    // Attached to short-lived construction helpers. Direct typed Def references
    // keep Core/DLC remapping explicit and avoid string guessing at completion.
    public sealed class NaturalSurfaceTargetExtension : DefModExtension
    {
        public ThingDef wall;
        public string floorStone;
    }

    // Natural-result choices are built as ordinary one-chunk buildings. Once
    // construction completes, this helper queues a safe next-tick replacement
    // with the genuine vanilla wall or terrain named by the extension above.
    public sealed class NaturalSurfaceConstructionProxy : Building
    {
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            NaturalSurfaceCompletionComponent component =
                map.GetComponent<NaturalSurfaceCompletionComponent>();
            if (component != null)
            {
                component.Queue(this);
            }
        }
    }

    // Added at startup to vanilla smoothed wall definitions only while the
    // natural-result catalogue is enabled. This provides the familiar build-copy
    // command without Harmony; completed floors cannot host gizmos because they
    // are terrain rather than selectable Things.
    public sealed class CompProperties_NaturalWallBuildCopy : CompProperties
    {
        public ThingDef naturalProxy;

        public CompProperties_NaturalWallBuildCopy()
        {
            compClass = typeof(CompNaturalWallBuildCopy);
        }
    }

    public sealed class CompNaturalWallBuildCopy : ThingComp
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            CompProperties_NaturalWallBuildCopy properties =
                props as CompProperties_NaturalWallBuildCopy;
            ThingDef proxy = properties == null ? null : properties.naturalProxy;
            if (proxy == null || Find.DesignatorManager == null)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "CommandBuildCopy".Translate(),
                defaultDesc = "CommandBuildCopyDesc".Translate(),
                icon = proxy.uiIcon,
                defaultIconColor = proxy.uiIconColor,
                // Match RimWorld's ordinary Build copy command, including the
                // player's configured contextual shortcut (O by default).
                hotKey = KeyBindingDefOf.Misc11,
                action = delegate
                {
                    Find.DesignatorManager.Select(new Designator_Build(proxy));
                }
            };
        }
    }

    // Designator_Build normally derives its drawing modes from the BuildableDef,
    // but its ThingDef path does not expose the full vanilla floor-style selector.
    // Natural floors must be ThingDefs briefly so they can consume a chunk through
    // ordinary construction. This specialised designator explicitly opts them into
    // the same Q/E shape catalogue used by TerrainDef floors.
    // Fixed-material definitions do not receive the availability filtering used
    // by vanilla's stuff-based wall designator. Mirror that behaviour for our
    // one-chunk surfaces: an entry is visible only while its matching chunk is
    // present on the current map. Mods which deliberately reveal hidden
    // dropdown entries can still do so by patching the ordinary Visible path.
    public class Designator_BuildChunkSurface : Designator_Build
    {
        internal static bool RevealUnavailableForMenu;

        public Designator_BuildChunkSurface(BuildableDef buildable) : base(buildable)
        {
        }

        public override bool Visible
        {
            get
            {
                if (!base.Visible)
                {
                    return false;
                }

                Map map = Find.CurrentMap;
                if (RevealUnavailableForMenu || DebugSettings.godMode ||
                    map == null || PlacingDef == null ||
                    PlacingDef.CostList == null)
                {
                    return true;
                }

                for (int i = 0; i < PlacingDef.CostList.Count; i++)
                {
                    ThingDefCountClass cost = PlacingDef.CostList[i];
                    if (cost != null && cost.thingDef != null &&
                        map.listerThings.ThingsOfDef(cost.thingDef).Count <
                        cost.count)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

    }

    public sealed class Designator_BuildNaturalFloor : Designator_BuildChunkSurface
    {
        public Designator_BuildNaturalFloor(BuildableDef buildable) : base(buildable)
        {
        }

        public override DrawStyleCategoryDef DrawStyleCategory
        {
            get
            {
                return DefDatabase<DrawStyleCategoryDef>.GetNamedSilentFail("Floors");
            }
        }
    }

    // Vanilla keeps its generic wall command visible even when no suitable
    // material exists, then explains the problem when clicked. Fixed-material
    // dropdowns normally disappear when all of their children are hidden, so
    // retain the command and provide the same vanilla feedback instead.
    public sealed class Designator_DropdownChunkSurface : Designator_Dropdown
    {
        private static readonly MethodInfo SetupFloatMenuMethod =
            typeof(Designator_Dropdown).GetMethod(
                "SetupFloatMenu",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool setupFloatMenuFailureLogged;

        public override bool Visible
        {
            get { return Elements != null && Elements.Count > 0; }
        }

        public override void ProcessInput(Event ev)
        {
            // TD Enhancement Pack offers a secondary-click catalogue which
            // deliberately includes currently unavailable choices. Visibility
            // is evaluated while RimWorld constructs the menu, so reveal our
            // fixed-material children only for that synchronous operation.
            if (ev != null && ev.button == 1)
            {
                bool revealAll = ModsConfig.IsActive("MemeGoddess.TDPack");
                if (revealAll)
                {
                    try
                    {
                        Designator_BuildChunkSurface.RevealUnavailableForMenu = true;
                        OpenFilteredMenu(ev);
                    }
                    finally
                    {
                        Designator_BuildChunkSurface.RevealUnavailableForMenu = false;
                    }
                    return;
                }
            }

            for (int i = 0; i < Elements.Count; i++)
            {
                if (Elements[i].Visible)
                {
                    OpenFilteredMenu(ev);
                    return;
                }
            }

            Messages.Message(
                "NoStuffsToBuildWith".Translate(),
                MessageTypeDefOf.RejectInput,
                false);
        }

        private void OpenFilteredMenu(Event ev)
        {
            // Material Sub-Menu patches ProcessInput and can bypass the normal
            // child visibility checks. Invoke RimWorld's own menu builder
            // directly so fixed-chunk availability remains authoritative while
            // preserving ordinary dropdown behaviour and compatible UI patches.
            if (SetupFloatMenuMethod != null)
            {
                try
                {
                    Window menu = SetupFloatMenuMethod.Invoke(
                        this,
                        new object[] { ev }) as Window;
                    if (menu != null)
                    {
                        Find.WindowStack.Add(menu);
                    }
                    return;
                }
                catch (Exception exception)
                {
                    TargetInvocationException invocation =
                        exception as TargetInvocationException;
                    Exception cause = invocation != null && invocation.InnerException != null
                        ? invocation.InnerException
                        : exception;
                    if (!setupFloatMenuFailureLogged)
                    {
                        setupFloatMenuFailureLogged = true;
                        Log.Error(
                            "[Smoothing Expanded] Failed to open the chunk-surface " +
                            "dropdown. Falling back to the standard handler: " + cause);
                    }
                    base.ProcessInput(ev);
                    return;
                }
            }

            base.ProcessInput(ev);
        }
    }

    public sealed class NaturalSurfaceCompletionComponent : MapComponent
    {
        private readonly List<NaturalSurfaceConstructionProxy> pending =
            new List<NaturalSurfaceConstructionProxy>();
        private readonly Dictionary<NaturalSurfaceConstructionProxy, int> retryAfterTick =
            new Dictionary<NaturalSurfaceConstructionProxy, int>();
        private readonly HashSet<NaturalSurfaceConstructionProxy> resolutionErrorsLogged =
            new HashSet<NaturalSurfaceConstructionProxy>();

        public NaturalSurfaceCompletionComponent(Map map) : base(map)
        {
        }

        internal void Queue(NaturalSurfaceConstructionProxy proxy)
        {
            if (proxy != null && !pending.Contains(proxy))
            {
                pending.Add(proxy);
            }
        }

        public override void MapComponentTick()
        {
            if (pending.Count == 0)
            {
                return;
            }

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                NaturalSurfaceConstructionProxy proxy = pending[i];
                pending.RemoveAt(i);
                if (proxy == null || proxy.Destroyed || !proxy.Spawned)
                {
                    if (proxy != null)
                    {
                        retryAfterTick.Remove(proxy);
                        resolutionErrorsLogged.Remove(proxy);
                    }
                    continue;
                }

                int retryTick;
                if (retryAfterTick.TryGetValue(proxy, out retryTick) &&
                    Find.TickManager.TicksGame < retryTick)
                {
                    pending.Add(proxy);
                    continue;
                }
                retryAfterTick.Remove(proxy);

                NaturalSurfaceTargetExtension target =
                    proxy.def.GetModExtension<NaturalSurfaceTargetExtension>();
                if (target == null)
                {
                    Log.Error("[Smoothing Expanded] Natural construction helper " +
                        proxy.def.defName + " has no target extension.");
                    continue;
                }

                IntVec3 position = proxy.Position;
                Faction faction = proxy.Faction;
                TerrainDef naturalFloor = string.IsNullOrEmpty(target.floorStone)
                    ? null
                    : ChunkConstructionController.FindNaturalSmoothFloor(target.floorStone);
                if (naturalFloor != null)
                {
                    resolutionErrorsLogged.Remove(proxy);
                    map.terrainGrid.SetTerrain(position, naturalFloor);
                    proxy.Destroy(DestroyMode.Vanish);
                    map.mapDrawer.MapMeshDirty(position, MapMeshFlagDefOf.Terrain);
                }
                else if (target.wall != null)
                {
                    resolutionErrorsLogged.Remove(proxy);
                    proxy.Destroy(DestroyMode.Vanish);
                    Thing wall = ThingMaker.MakeThing(target.wall);
                    if (faction != null)
                    {
                        wall.SetFactionDirect(faction);
                    }
                    GenSpawn.Spawn(wall, position, map);
                }
                else if (!string.IsNullOrEmpty(target.floorStone))
                {
                    if (resolutionErrorsLogged.Add(proxy))
                    {
                        Log.Error("[Smoothing Expanded] Could not safely resolve the " +
                            "vanilla smoothed floor for " + target.floorStone +
                            ". The completed construction helper was retained and " +
                            "will retry instead of being discarded.");
                    }
                    retryAfterTick[proxy] = Find.TickManager.TicksGame + 250;
                    pending.Add(proxy);
                }
            }
        }
    }

    public sealed class SmoothingSettings : ModSettings
    {
        public float SpeedMultiplier = 2f;
        public bool InstantSmoothing = false;
        public float FloorSpeedMultiplier = 2f;
        public bool InstantFloorSmoothing = false;
        public bool OverrideWallWealth = false;
        public float SmoothedWallValue = 0f;
        public bool OverrideFloorWealth = false;
        public float SmoothedFloorValue = 0f;
        // The existing "remove beauty" keys now mean "override beauty". This
        // preserves old enabled settings as an override of zero.
        public bool RemoveNaturalWallBeauty = false;
        public float SmoothedWallBeauty = 0f;
        public bool RemoveNaturalFloorBeauty = false;
        public float SmoothedFloorBeauty = 0f;
        public bool EnableChunkConstruction = false;
        // 0 = constructed/deconstructible, 1 = vanilla natural result,
        // 2 = expose both catalogues. Constructed remains the balanced default.
        public int ChunkSurfaceResultMode = 0;
        public bool ChunkWallsHaveWealth = true;
        public float ChunkWallWealthValue = 0f;
        public bool ChunkFloorsHaveWealth = true;
        public float ChunkFloorWealthValue = 0f;
        public bool ChunkWallsHaveBeauty = true;
        public float ChunkWallBeautyValue = 0f;
        public bool ChunkFloorsHaveBeauty = true;
        public float ChunkFloorBeautyValue = 0f;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref SpeedMultiplier, "speedMultiplier", 2f);
            Scribe_Values.Look(ref InstantSmoothing, "instantSmoothing", false);
            // Existing settings migrate naturally: before these keys existed,
            // floors used the same multiplier and instant choice as walls.
            Scribe_Values.Look(ref FloorSpeedMultiplier, "floorSpeedMultiplier", SpeedMultiplier);
            Scribe_Values.Look(ref InstantFloorSmoothing, "instantFloorSmoothing", InstantSmoothing);
            Scribe_Values.Look(ref OverrideWallWealth, "overrideWallWealth", false);
            Scribe_Values.Look(ref SmoothedWallValue, "smoothedWallValue", 0f);
            Scribe_Values.Look(ref OverrideFloorWealth, "overrideFloorWealth", false);
            Scribe_Values.Look(ref SmoothedFloorValue, "smoothedFloorValue", 0f);
            Scribe_Values.Look(ref RemoveNaturalWallBeauty, "removeNaturalWallBeauty", false);
            Scribe_Values.Look(ref SmoothedWallBeauty, "smoothedWallBeauty", 0f);
            Scribe_Values.Look(ref RemoveNaturalFloorBeauty, "removeNaturalFloorBeauty", false);
            Scribe_Values.Look(ref SmoothedFloorBeauty, "smoothedFloorBeauty", 0f);
            Scribe_Values.Look(ref EnableChunkConstruction, "enableChunkConstruction", false);
            Scribe_Values.Look(ref ChunkSurfaceResultMode, "chunkSurfaceResultMode", 0);
            bool legacyChunkWealth = true;
            Scribe_Values.Look(ref legacyChunkWealth, "chunkConstructionHasWealth", true);
            Scribe_Values.Look(ref ChunkWallsHaveWealth, "chunkWallsHaveWealth", legacyChunkWealth);
            Scribe_Values.Look(ref ChunkWallWealthValue, "chunkWallWealthValue", 0f);
            Scribe_Values.Look(ref ChunkFloorsHaveWealth, "chunkFloorsHaveWealth", legacyChunkWealth);
            Scribe_Values.Look(ref ChunkFloorWealthValue, "chunkFloorWealthValue", 0f);
            Scribe_Values.Look(ref ChunkWallsHaveBeauty, "chunkWallsHaveBeauty", true);
            Scribe_Values.Look(ref ChunkWallBeautyValue, "chunkWallBeautyValue", 0f);
            Scribe_Values.Look(ref ChunkFloorsHaveBeauty, "chunkFloorsHaveBeauty", true);
            Scribe_Values.Look(ref ChunkFloorBeautyValue, "chunkFloorBeautyValue", 0f);
            SpeedMultiplier = Math.Max(0.25f, Math.Min(10f, SpeedMultiplier));
            FloorSpeedMultiplier = Math.Max(0.25f, Math.Min(10f, FloorSpeedMultiplier));
            SmoothedWallValue = Math.Max(0f, Math.Min(100f, SmoothedWallValue));
            SmoothedFloorValue = Math.Max(0f, Math.Min(100f, SmoothedFloorValue));
            SmoothedWallBeauty = Math.Max(-10f, Math.Min(20f, SmoothedWallBeauty));
            SmoothedFloorBeauty = Math.Max(-10f, Math.Min(20f, SmoothedFloorBeauty));
            ChunkWallWealthValue = Math.Max(0f, Math.Min(100f, ChunkWallWealthValue));
            ChunkFloorWealthValue = Math.Max(0f, Math.Min(100f, ChunkFloorWealthValue));
            ChunkWallBeautyValue = Math.Max(-10f, Math.Min(20f, ChunkWallBeautyValue));
            ChunkFloorBeautyValue = Math.Max(-10f, Math.Min(20f, ChunkFloorBeautyValue));
            ChunkSurfaceResultMode = Math.Max(0, Math.Min(2, ChunkSurfaceResultMode));
            base.ExposeData();
        }
    }

    public sealed class SmoothingExpandedMod : Mod
    {
        internal static SmoothingSettings Settings;
        internal static bool FloorSpeedFeatureAvailable;
        internal static bool FloorWealthFeatureAvailable;
        private int settingsPage;
        // Each page owns its scroll state so returning to a detailed category
        // does not lose the player's place.
        private readonly Vector2[] settingsPageScrollPositions = new Vector2[5];
        private readonly float[] settingsPageContentHeights = new float[5];

        public SmoothingExpandedMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SmoothingSettings>();
        }

        public override string SettingsCategory()
        {
            return "SmoothingExpanded.SettingsCategory".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            float previousSpeedMultiplier = Settings.SpeedMultiplier;
            bool previousInstantSmoothing = Settings.InstantSmoothing;
            float previousFloorSpeedMultiplier = Settings.FloorSpeedMultiplier;
            bool previousInstantFloorSmoothing = Settings.InstantFloorSmoothing;
            bool previousNaturalWallBeauty = Settings.RemoveNaturalWallBeauty;
            float previousNaturalWallBeautyValue = Settings.SmoothedWallBeauty;
            bool previousNaturalFloorBeauty = Settings.RemoveNaturalFloorBeauty;
            float previousNaturalFloorBeautyValue = Settings.SmoothedFloorBeauty;

            DrawPageNavigation(new Rect(inRect.x + 4f, inRect.y,
                inRect.width - 8f, 30f));
            Rect pageRect = new Rect(inRect.x, inRect.y + 36f, inRect.width,
                inRect.height - 36f);
            switch (settingsPage)
            {
                case 0: DrawHomePage(pageRect); break;
                case 1: DrawSmoothingPage(pageRect); break;
                case 2: DrawVanillaSurfacesPage(pageRect); break;
                case 3: DrawChunkConstructionPage(pageRect); break;
                default: DrawSaveSafetyPage(pageRect); break;
            }

            if (previousSpeedMultiplier != Settings.SpeedMultiplier ||
                previousInstantSmoothing != Settings.InstantSmoothing ||
                previousFloorSpeedMultiplier != Settings.FloorSpeedMultiplier ||
                previousInstantFloorSmoothing != Settings.InstantFloorSmoothing)
            {
                SmoothingSpeedController.Apply();
            }
            if (previousNaturalWallBeauty != Settings.RemoveNaturalWallBeauty ||
                previousNaturalWallBeautyValue != Settings.SmoothedWallBeauty ||
                previousNaturalFloorBeauty != Settings.RemoveNaturalFloorBeauty ||
                previousNaturalFloorBeautyValue != Settings.SmoothedFloorBeauty)
            {
                SmoothingSpeedController.ApplyBeautyOverrides();
            }
        }

        private void DrawPageNavigation(Rect inRect)
        {
            string[] labels =
            {
                "SmoothingExpanded.Page.Home".Translate(),
                "SmoothingExpanded.Page.Smoothing".Translate(),
                "SmoothingExpanded.Page.Vanilla".Translate(),
                "SmoothingExpanded.Page.Chunk".Translate(),
                "SmoothingExpanded.Page.SaveSafety".Translate()
            };
            float width = inRect.width / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                Rect button = new Rect(inRect.x + width * i, inRect.y,
                    width - 3f, 30f);
                if (DrawTabButton(button, labels[i], settingsPage == i))
                {
                    settingsPage = i;
                }
            }
        }

        private static bool DrawTabButton(Rect rect, string label, bool selected)
        {
            // Match Configurable Special Trees: use RimWorld's selected and
            // unselected option assets so UI replacers style both states.
            Widgets.DrawOptionBackground(rect, selected);
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect.ContractedBy(4f), label);
            Text.Anchor = oldAnchor;
            return !selected && Widgets.ButtonInvisible(rect);
        }

        private void DrawHomePage(Rect pageRect)
        {
            BeginPage(pageRect, 0, delegate(Listing_Standard listing)
            {
                listing.Label("SmoothingExpanded.HomeIntroduction".Translate());
                listing.GapLine();
                listing.Label("SmoothingExpanded.HomeCapabilityHeader".Translate());
                listing.Label((FloorSpeedFeatureAvailable
                    ? "SmoothingExpanded.CapabilityFloorSpeedAvailable"
                    : "SmoothingExpanded.CapabilityFloorSpeedUnavailable").Translate());
                listing.Label((FloorWealthFeatureAvailable
                    ? "SmoothingExpanded.CapabilityFloorWealthAvailable"
                    : "SmoothingExpanded.CapabilityFloorWealthUnavailable").Translate());
                listing.Label((Current.Game != null && Find.Maps != null && Find.Maps.Count > 0
                    ? "SmoothingExpanded.CapabilitySaveLoaded"
                    : "SmoothingExpanded.CapabilityNoSaveLoaded").Translate());
                listing.GapLine();
                bool oldEnabled = GUI.enabled;
                GUI.enabled = oldEnabled && AnySettingsDifferFromDefaults();
                if (listing.ButtonText("SmoothingExpanded.ResetAll".Translate()))
                {
                    RequestResetAllSettingsToDefaults();
                }
                GUI.enabled = oldEnabled;
            });
        }

        private void DrawSmoothingPage(Rect pageRect)
        {
            BeginPage(pageRect, 1, delegate(Listing_Standard listing)
            {
                listing.Label("SmoothingExpanded.WallSpeed".Translate(
                    Settings.InstantSmoothing ? "SmoothingExpanded.InstantValue".Translate()
                    : Settings.SpeedMultiplier.ToString("0.00") + "x"));
                DrawSpeedControls(listing, ref Settings.SpeedMultiplier,
                    ref Settings.InstantSmoothing, true);
                listing.Label("SmoothingExpanded.SpeedExplanation".Translate());
                listing.GapLine();
                listing.Label((FloorSpeedFeatureAvailable
                    ? "SmoothingExpanded.CapabilityFloorSpeedAvailable"
                    : "SmoothingExpanded.CapabilityFloorSpeedUnavailable").Translate());
                listing.Label("SmoothingExpanded.FloorSpeed".Translate(
                    Settings.InstantFloorSmoothing ? "SmoothingExpanded.InstantValue".Translate()
                    : Settings.FloorSpeedMultiplier.ToString("0.00") + "x"));
                bool oldEnabled = GUI.enabled;
                GUI.enabled = oldEnabled && FloorSpeedFeatureAvailable;
                DrawSpeedControls(listing, ref Settings.FloorSpeedMultiplier,
                    ref Settings.InstantFloorSmoothing, false);
                GUI.enabled = oldEnabled;
                if (!FloorSpeedFeatureAvailable)
                {
                    listing.Label("SmoothingExpanded.RequiresHarmonyTooltip".Translate());
                }
                listing.GapLine();
                if (listing.ButtonText("SmoothingExpanded.ResetSmoothing".Translate()))
                {
                    ResetSmoothingSettingsToDefaults();
                }
            });
        }

        private static void DrawSpeedControls(Listing_Standard listing,
            ref float multiplier, ref bool instant, bool wall)
        {
            Rect row = listing.GetRect(30f);
            Rect slider = new Rect(row.x, row.y, row.width * 0.70f, row.height);
            Rect check = new Rect(row.x + row.width * 0.72f, row.y,
                row.width * 0.28f, row.height);
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && !instant;
            multiplier = Widgets.HorizontalSlider(slider, multiplier, 0.25f, 10f, true);
            GUI.enabled = oldEnabled;
            Widgets.CheckboxLabeled(check, "SmoothingExpanded.Instant".Translate(), ref instant);
            TooltipHandler.TipRegion(check, (wall
                ? "SmoothingExpanded.InstantWallTooltip"
                : "SmoothingExpanded.InstantFloorTooltip").Translate());
            multiplier = (float)Math.Round(multiplier * 4f) / 4f;
        }

        private void DrawVanillaSurfacesPage(Rect pageRect)
        {
            BeginPage(pageRect, 2, delegate(Listing_Standard listing)
            {
                listing.Label("SmoothingExpanded.NaturalWealthHeader".Translate());
                listing.CheckboxLabeled("SmoothingExpanded.OverrideNaturalWallWealth".Translate(),
                    ref Settings.OverrideWallWealth,
                    "SmoothingExpanded.OverrideNaturalWallWealthTooltip".Translate());
                if (Settings.OverrideWallWealth)
                {
                    listing.Label("SmoothingExpanded.NaturalWallValue".Translate(Settings.SmoothedWallValue.ToString("0.0")));
                    Settings.SmoothedWallValue = RoundHalf(listing.Slider(Settings.SmoothedWallValue, 0f, 100f));
                }
                listing.Label((FloorWealthFeatureAvailable
                    ? "SmoothingExpanded.CapabilityFloorWealthAvailable"
                    : "SmoothingExpanded.CapabilityFloorWealthUnavailable").Translate());
                bool oldEnabled = GUI.enabled;
                GUI.enabled = oldEnabled && FloorWealthFeatureAvailable;
                listing.CheckboxLabeled("SmoothingExpanded.OverrideNaturalFloorWealth".Translate(),
                    ref Settings.OverrideFloorWealth,
                    "SmoothingExpanded.OverrideNaturalFloorWealthTooltip".Translate());
                GUI.enabled = oldEnabled;
                if (FloorWealthFeatureAvailable && Settings.OverrideFloorWealth)
                {
                    listing.Label("SmoothingExpanded.NaturalFloorValue".Translate(Settings.SmoothedFloorValue.ToString("0.0")));
                    Settings.SmoothedFloorValue = RoundHalf(listing.Slider(Settings.SmoothedFloorValue, 0f, 100f));
                    listing.Label("SmoothingExpanded.FloorWealthWarning".Translate());
                }
                listing.GapLine();
                listing.Label("SmoothingExpanded.NaturalBeautyHeader".Translate());
                DrawBeautyControls(listing, "SmoothingExpanded.RemoveNaturalWallBeauty",
                    "SmoothingExpanded.RemoveNaturalWallBeautyTooltip",
                    "SmoothingExpanded.NaturalWallBeautyValue",
                    ref Settings.RemoveNaturalWallBeauty, ref Settings.SmoothedWallBeauty);
                DrawBeautyControls(listing, "SmoothingExpanded.RemoveNaturalFloorBeauty",
                    "SmoothingExpanded.RemoveNaturalFloorBeautyTooltip",
                    "SmoothingExpanded.NaturalFloorBeautyValue",
                    ref Settings.RemoveNaturalFloorBeauty, ref Settings.SmoothedFloorBeauty);
                listing.GapLine();
                if (listing.ButtonText("SmoothingExpanded.ResetVanillaSurfaces".Translate()))
                {
                    ResetVanillaSurfaceSettingsToDefaults();
                }
            });
        }

        private static void DrawBeautyControls(Listing_Standard listing, string label,
            string tooltip, string valueLabel, ref bool enabled, ref float value)
        {
            listing.CheckboxLabeled(label.Translate(), ref enabled, tooltip.Translate());
            if (enabled)
            {
                listing.Label(valueLabel.Translate(value.ToString("0")));
                value = (float)Math.Round(listing.Slider(value, -10f, 20f));
            }
        }

        private void DrawChunkConstructionPage(Rect pageRect)
        {
            BeginPage(pageRect, 3, delegate(Listing_Standard listing)
            {
                bool wasEnabled = Settings.EnableChunkConstruction;
                listing.CheckboxLabeled("SmoothingExpanded.EnableChunkConstruction".Translate(),
                    ref Settings.EnableChunkConstruction,
                    "SmoothingExpanded.EnableChunkConstructionTooltip".Translate());
                if (wasEnabled && !Settings.EnableChunkConstruction)
                {
                    ConfirmChunkDisable();
                }
                listing.Label("SmoothingExpanded.UninstallWarning".Translate());
                if (Settings.EnableChunkConstruction)
                {
                    DrawChunkOptions(listing);
                }
                listing.GapLine();
                if (listing.ButtonText("SmoothingExpanded.ResetChunkSettings".Translate()))
                {
                    if (Settings.EnableChunkConstruction) { ConfirmChunkReset(); }
                    else { ResetChunkSettingsToDefaults(); }
                }
            });
        }

        private void DrawChunkOptions(Listing_Standard listing)
        {
            listing.Label("SmoothingExpanded.ChunkResultHeader".Translate());
            if (listing.RadioButton("SmoothingExpanded.ChunkResultConstructed".Translate(), Settings.ChunkSurfaceResultMode == 0)) Settings.ChunkSurfaceResultMode = 0;
            if (listing.RadioButton("SmoothingExpanded.ChunkResultNatural".Translate(), Settings.ChunkSurfaceResultMode == 1)) Settings.ChunkSurfaceResultMode = 1;
            if (listing.RadioButton("SmoothingExpanded.ChunkResultBoth".Translate(), Settings.ChunkSurfaceResultMode == 2)) Settings.ChunkSurfaceResultMode = 2;
            listing.Label("SmoothingExpanded.NaturalResultExplanation".Translate());
            if (Settings.ChunkSurfaceResultMode == 1) return;
            listing.GapLine();
            listing.Label("SmoothingExpanded.ChunkWealthHeader".Translate());
            DrawChunkOverride(listing, "SmoothingExpanded.ChunkWallWealth", "SmoothingExpanded.ChunkWallWealthTooltip", "SmoothingExpanded.ChunkWallWealthValue", ref Settings.ChunkWallsHaveWealth, ref Settings.ChunkWallWealthValue, 0f, 100f, true);
            listing.Label((FloorWealthFeatureAvailable ? "SmoothingExpanded.CapabilityFloorWealthAvailable" : "SmoothingExpanded.CapabilityFloorWealthUnavailable").Translate());
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && FloorWealthFeatureAvailable;
            DrawChunkOverride(listing, "SmoothingExpanded.ChunkFloorWealth", "SmoothingExpanded.ChunkFloorWealthTooltip", "SmoothingExpanded.ChunkFloorWealthValue", ref Settings.ChunkFloorsHaveWealth, ref Settings.ChunkFloorWealthValue, 0f, 100f, true);
            GUI.enabled = oldEnabled;
            listing.GapLine();
            listing.Label("SmoothingExpanded.ChunkBeautyHeader".Translate());
            DrawChunkOverride(listing, "SmoothingExpanded.ChunkWallBeauty", "SmoothingExpanded.ChunkWallBeautyTooltip", "SmoothingExpanded.ChunkWallBeautyValue", ref Settings.ChunkWallsHaveBeauty, ref Settings.ChunkWallBeautyValue, -10f, 20f, false);
            DrawChunkOverride(listing, "SmoothingExpanded.ChunkFloorBeauty", "SmoothingExpanded.ChunkFloorBeautyTooltip", "SmoothingExpanded.ChunkFloorBeautyValue", ref Settings.ChunkFloorsHaveBeauty, ref Settings.ChunkFloorBeautyValue, -10f, 20f, false);
            listing.GapLine();
            listing.Label("SmoothingExpanded.ChunkConstructionExplanation".Translate());
        }

        private static void DrawChunkOverride(Listing_Standard listing, string label,
            string tooltip, string valueLabel, ref bool inherited, ref float value,
            float min, float max, bool halves)
        {
            bool overrideValue = !inherited;
            listing.CheckboxLabeled(label.Translate(), ref overrideValue, tooltip.Translate());
            inherited = !overrideValue;
            if (overrideValue)
            {
                listing.Label(valueLabel.Translate(value.ToString(halves ? "0.0" : "0")));
                value = listing.Slider(value, min, max);
                value = halves ? RoundHalf(value) : (float)Math.Round(value);
            }
        }

        private void DrawSaveSafetyPage(Rect pageRect)
        {
            BeginPage(pageRect, 4, delegate(Listing_Standard listing)
            {
                // Keep irreversible save conversion apart from ordinary tuning.
                listing.Label("SmoothingExpanded.SaveSafetyIntroduction".Translate());
                listing.Label("SmoothingExpanded.UninstallExplanation".Translate());
                bool hasLoadedMaps = Current.Game != null && Find.Maps != null && Find.Maps.Count > 0;
                listing.Label((hasLoadedMaps ? "SmoothingExpanded.CapabilitySaveLoaded" : "SmoothingExpanded.CapabilityNoSaveLoaded").Translate());
                bool oldEnabled = GUI.enabled;
                GUI.enabled = oldEnabled && hasLoadedMaps;
                float buttonTop = listing.CurHeight;
                if (listing.ButtonText("SmoothingExpanded.PrepareUninstall".Translate()))
                {
                    ChunkConstructionController.ShowConversionConfirmation();
                }
                GUI.enabled = oldEnabled;
                if (!hasLoadedMaps)
                {
                    TooltipHandler.TipRegion(new Rect(pageRect.x, buttonTop, pageRect.width, 30f),
                        "SmoothingExpanded.LoadSaveTooltip".Translate());
                }
            });
        }

        private void BeginPage(Rect pageRect, int page, Action<Listing_Standard> draw)
        {
            // Match CST's viewport convention: an estimate handles controls
            // revealed during this event, while the last measured layout keeps
            // translated text and future frames exact.
            float contentHeight = Mathf.Max(settingsPageContentHeights[page],
                GetPageEstimatedContentHeight(page, pageRect.width - 18f));
            float maxScroll = Math.Max(0f, contentHeight - pageRect.height);
            settingsPageScrollPositions[page].y = Mathf.Clamp(
                settingsPageScrollPositions[page].y, 0f, maxScroll);
            Rect viewRect = new Rect(0f, 0f, pageRect.width - 18f,
                Math.Max(pageRect.height, contentHeight));
            Widgets.BeginScrollView(pageRect, ref settingsPageScrollPositions[page], viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);
            draw(listing);
            // Use the previously measured height for this event, then retain the
            // height actually consumed by the listing for the next one. This
            // keeps optional controls and translated text out of a fixed canvas.
            settingsPageContentHeights[page] = listing.CurHeight + 8f;
            settingsPageScrollPositions[page].y = Mathf.Clamp(
                settingsPageScrollPositions[page].y, 0f,
                Math.Max(0f, settingsPageContentHeights[page] - pageRect.height));
            listing.End();
            Widgets.EndScrollView();
        }

        private static float GetPageEstimatedContentHeight(int page, float width)
        {
            if (page != 3)
            {
                return 0f;
            }

            // Chunk options can become visible from a click in this same IMGUI
            // event. Reserve only their current rows so BeginScrollView knows
            // immediately when it needs a scrollbar; the measured height above
            // remains the authority once the page has been drawn.
            float height = 30f + EstimateLabelHeight(
                "SmoothingExpanded.UninstallWarning".Translate(), width);
            if (!Settings.EnableChunkConstruction)
            {
                return height + 42f;
            }

            height += EstimateLabelHeight(
                "SmoothingExpanded.ChunkResultHeader".Translate(), width);
            height += 3f * 30f;
            height += EstimateLabelHeight(
                "SmoothingExpanded.NaturalResultExplanation".Translate(), width);
            if (Settings.ChunkSurfaceResultMode == 1)
            {
                return height + 42f;
            }

            height += 12f + EstimateLabelHeight(
                "SmoothingExpanded.ChunkWealthHeader".Translate(), width);
            height += EstimateChunkOverrideHeight(
                "SmoothingExpanded.ChunkWallWealth".Translate(), width,
                !Settings.ChunkWallsHaveWealth);
            height += EstimateLabelHeight((FloorWealthFeatureAvailable
                ? "SmoothingExpanded.CapabilityFloorWealthAvailable"
                : "SmoothingExpanded.CapabilityFloorWealthUnavailable").Translate(), width);
            height += EstimateChunkOverrideHeight(
                "SmoothingExpanded.ChunkFloorWealth".Translate(), width,
                !Settings.ChunkFloorsHaveWealth);
            height += 12f + EstimateLabelHeight(
                "SmoothingExpanded.ChunkBeautyHeader".Translate(), width);
            height += EstimateChunkOverrideHeight(
                "SmoothingExpanded.ChunkWallBeauty".Translate(), width,
                !Settings.ChunkWallsHaveBeauty);
            height += EstimateChunkOverrideHeight(
                "SmoothingExpanded.ChunkFloorBeauty".Translate(), width,
                !Settings.ChunkFloorsHaveBeauty);
            height += 12f + EstimateLabelHeight(
                "SmoothingExpanded.ChunkConstructionExplanation".Translate(), width);
            return height + 42f;
        }

        private static float EstimateChunkOverrideHeight(string label, float width,
            bool showsValue)
        {
            return Mathf.Max(30f, EstimateLabelHeight(label, width)) +
                (showsValue ? Text.LineHeight + 30f : 0f);
        }

        private static float EstimateLabelHeight(string label, float width)
        {
            return Mathf.Max(Text.LineHeight, Text.CalcHeight(label, width)) + 2f;
        }

        private void ConfirmChunkDisable()
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "SmoothingExpanded.DisableChunkWarning".Translate(), delegate { },
                delegate { Settings.EnableChunkConstruction = true; }, false,
                "SmoothingExpanded.DisableChunkTitle".Translate()));
        }

        private void ConfirmChunkReset()
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "SmoothingExpanded.ResetChunkWarning".Translate(),
                ResetChunkSettingsToDefaults, false,
                "SmoothingExpanded.ResetChunkTitle".Translate()));
        }

        private void RequestResetAllSettingsToDefaults()
        {
            if (Settings.EnableChunkConstruction)
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "SmoothingExpanded.ResetWarning".Translate(),
                    ResetAllSettingsToDefaults, false,
                    "SmoothingExpanded.ResetTitle".Translate()));
            }
            else
            {
                ResetAllSettingsToDefaults();
            }
        }

        private static float RoundHalf(float value)
        {
            return (float)Math.Round(value * 2f) / 2f;
        }

        private static void ResetSmoothingSettingsToDefaults()
        {
            Settings.SpeedMultiplier = 2f;
            Settings.InstantSmoothing = false;
            Settings.FloorSpeedMultiplier = 2f;
            Settings.InstantFloorSmoothing = false;
        }

        private static void ResetVanillaSurfaceSettingsToDefaults()
        {
            Settings.OverrideWallWealth = false;
            Settings.SmoothedWallValue = 0f;
            Settings.OverrideFloorWealth = false;
            Settings.SmoothedFloorValue = 0f;
            Settings.RemoveNaturalWallBeauty = false;
            Settings.SmoothedWallBeauty = 0f;
            Settings.RemoveNaturalFloorBeauty = false;
            Settings.SmoothedFloorBeauty = 0f;
            SmoothingSpeedController.ApplyWallWealthOverride();
            SmoothingSpeedController.ApplyBeautyOverrides();
            if (Find.CurrentMap != null) Find.CurrentMap.wealthWatcher.ForceRecount(true);
        }

        private static void ResetAllSettingsToDefaults()
        {
            ResetSmoothingSettingsToDefaults();
            ResetVanillaSurfaceSettingsToDefaults();
            ResetChunkSettingsToDefaults();
        }

        private static bool AnySettingsDifferFromDefaults()
        {
            return Settings.SpeedMultiplier != 2f || Settings.InstantSmoothing ||
                Settings.FloorSpeedMultiplier != 2f || Settings.InstantFloorSmoothing ||
                Settings.OverrideWallWealth || Settings.SmoothedWallValue != 0f ||
                Settings.OverrideFloorWealth || Settings.SmoothedFloorValue != 0f ||
                Settings.RemoveNaturalWallBeauty || Settings.SmoothedWallBeauty != 0f ||
                Settings.RemoveNaturalFloorBeauty || Settings.SmoothedFloorBeauty != 0f ||
                Settings.EnableChunkConstruction || Settings.ChunkSurfaceResultMode != 0 ||
                !Settings.ChunkWallsHaveWealth || Settings.ChunkWallWealthValue != 0f ||
                !Settings.ChunkFloorsHaveWealth || Settings.ChunkFloorWealthValue != 0f ||
                !Settings.ChunkWallsHaveBeauty || Settings.ChunkWallBeautyValue != 0f ||
                !Settings.ChunkFloorsHaveBeauty || Settings.ChunkFloorBeautyValue != 0f;
        }

        internal static void ResetChunkSettingsToDefaults()
        {
            if (Settings == null)
            {
                return;
            }
            Settings.EnableChunkConstruction = false;
            Settings.ChunkSurfaceResultMode = 0;
            Settings.ChunkWallsHaveWealth = true;
            Settings.ChunkWallWealthValue = 0f;
            Settings.ChunkFloorsHaveWealth = true;
            Settings.ChunkFloorWealthValue = 0f;
            Settings.ChunkWallsHaveBeauty = true;
            Settings.ChunkWallBeautyValue = 0f;
            Settings.ChunkFloorsHaveBeauty = true;
            Settings.ChunkFloorBeautyValue = 0f;
            Settings.Write();
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            SmoothingSpeedController.Apply();
            SmoothingSpeedController.ApplyWallWealthOverride();
            SmoothingSpeedController.ApplyBeautyOverrides();
            // Refresh the vanilla cache immediately when either opt-in wealth
            // override is enabled, disabled, or given a new value.
            if (Find.CurrentMap != null)
            {
                Find.CurrentMap.wealthWatcher.ForceRecount(true);
            }
        }
    }

    [StaticConstructorOnStartup]
    internal static class SmoothingSpeedController
    {
        private static readonly HashSet<ThingDef> SmoothedWalls = new HashSet<ThingDef>();
        private static readonly HashSet<TerrainDef> SmoothedFloors = new HashSet<TerrainDef>();
        private static readonly Dictionary<ThingDef, float> OriginalWallValues = new Dictionary<ThingDef, float>();
        private static readonly Dictionary<ThingDef, float> OriginalWallBeauty = new Dictionary<ThingDef, float>();
        private static readonly Dictionary<TerrainDef, float> OriginalFloorBeauty = new Dictionary<TerrainDef, float>();
        private static readonly HashSet<float[]> OverriddenFloorCaches = new HashSet<float[]>();

        static SmoothingSpeedController()
        {
            Apply();
            DetectSmoothedSurfaces();
            ApplyWallWealthOverride();
            ApplyBeautyOverrides();
        }

        internal static void Apply()
        {
            if (SmoothingExpandedMod.Settings == null)
            {
                return;
            }

            StatDef smoothingSpeed = DefDatabase<StatDef>.GetNamedSilentFail("SmoothingSpeed");
            if (smoothingSpeed != null)
            {
                smoothingSpeed.defaultBaseValue = SmoothingExpandedMod.Settings.InstantSmoothing
                    ? 100000f
                    : SmoothingExpandedMod.Settings.SpeedMultiplier;
            }
        }

        internal static float FloorToWallSpeedRatio
        {
            get
            {
                SmoothingSettings settings = SmoothingExpandedMod.Settings;
                if (settings == null)
                {
                    return 1f;
                }

                float wallSpeed = settings.InstantSmoothing
                    ? 100000f
                    : settings.SpeedMultiplier;
                float floorSpeed = settings.InstantFloorSmoothing
                    ? 100000f
                    : settings.FloorSpeedMultiplier;
                return floorSpeed / Math.Max(0.01f, wallSpeed);
            }
        }

        internal static void DetectSmoothedSurfaces()
        {
            // Follow actual smoothing relationships instead of guessing from
            // defNames. This runs after implied defs have been generated, so it
            // includes Core's generated smooth-stone floors as well as DLC and
            // compatible modded surfaces.
            SmoothedWalls.Clear();
            List<ThingDef> things = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < things.Count; i++)
            {
                ThingDef source = things[i];
                if (source.building != null && source.building.smoothedThing != null)
                {
                    SmoothedWalls.Add(source.building.smoothedThing);
                }
            }

            SmoothedFloors.Clear();
            List<TerrainDef> terrains = DefDatabase<TerrainDef>.AllDefsListForReading;
            for (int i = 0; i < terrains.Count; i++)
            {
                TerrainDef source = terrains[i];
                if (source.smoothedTerrain != null)
                {
                    SmoothedFloors.Add(source.smoothedTerrain);
                }

                // Core's implied stone-floor defs are generated without a
                // reverse smoothedTerrain link. They use a stable smooth DefName
                // and have no construction cost. Never inspect translated labels:
                // doing so would make detection depend on the active language.
                // The cost check prevents
                // constructed floors with "smooth" in their name from being
                // treated as naturally smoothed terrain.
                string defName = source.defName ?? string.Empty;
                bool smoothName =
                    defName.EndsWith("_Smooth", StringComparison.OrdinalIgnoreCase) ||
                    defName.StartsWith("Smooth", StringComparison.OrdinalIgnoreCase) ||
                    defName.IndexOf("Smoothed", StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasNoConstructionCost = source.costList == null || source.costList.Count == 0;
                if (smoothName && hasNoConstructionCost)
                {
                    SmoothedFloors.Add(source);
                }
            }

            Log.Message("[Smoothing Expanded] Detected " +
                SmoothedWalls.Count + " smoothed wall defs and " +
                SmoothedFloors.Count + " smoothed floor defs. Wealth overrides are opt-in.");
        }

        internal static void ApplyWallWealthOverride()
        {
            SmoothingSettings settings = SmoothingExpandedMod.Settings;
            StatDef marketValue = DefDatabase<StatDef>.GetNamedSilentFail("MarketValue");
            if (settings == null || marketValue == null)
            {
                return;
            }

            foreach (ThingDef wall in SmoothedWalls)
            {
                float originalValue;
                if (!OriginalWallValues.TryGetValue(wall, out originalValue))
                {
                    // Preserve the effective value supplied by Core or another mod so
                    // disabling our option can restore what was present at startup.
                    originalValue = wall.GetStatValueAbstract(marketValue);
                    OriginalWallValues.Add(wall, originalValue);
                }

                wall.SetStatBaseValue(
                    marketValue,
                    settings.OverrideWallWealth ? settings.SmoothedWallValue : originalValue);
                wall.ResolveReferences();
            }
        }

        internal static void ApplyBeautyOverrides()
        {
            SmoothingSettings settings = SmoothingExpandedMod.Settings;
            StatDef beauty = DefDatabase<StatDef>.GetNamedSilentFail("Beauty");
            if (settings == null || beauty == null)
            {
                return;
            }

            foreach (ThingDef wall in SmoothedWalls)
            {
                float original;
                if (!OriginalWallBeauty.TryGetValue(wall, out original))
                {
                    original = wall.GetStatValueAbstract(beauty);
                    OriginalWallBeauty.Add(wall, original);
                }
                wall.SetStatBaseValue(
                    beauty,
                    settings.RemoveNaturalWallBeauty
                        ? settings.SmoothedWallBeauty
                        : original);
                wall.ResolveReferences();
            }

            foreach (TerrainDef floor in SmoothedFloors)
            {
                float original;
                if (!OriginalFloorBeauty.TryGetValue(floor, out original))
                {
                    original = floor.GetStatValueAbstract(beauty);
                    OriginalFloorBeauty.Add(floor, original);
                }
                floor.SetStatBaseValue(
                    beauty,
                    settings.RemoveNaturalFloorBeauty
                        ? settings.SmoothedFloorBeauty
                        : original);
                floor.ResolveReferences();
            }
        }

        internal static float OriginalBeautyFor(ThingDef wall, StatDef beauty)
        {
            float original;
            return wall != null && OriginalWallBeauty.TryGetValue(wall, out original)
                ? original
                : (wall == null ? 0f : wall.GetStatValueAbstract(beauty));
        }

        internal static float OriginalBeautyFor(TerrainDef floor, StatDef beauty)
        {
            float original;
            return floor != null && OriginalFloorBeauty.TryGetValue(floor, out original)
                ? original
                : (floor == null ? 0f : floor.GetStatValueAbstract(beauty));
        }

        internal static bool IsSmoothedSurface(BuildableDef def)
        {
            ThingDef wall = def as ThingDef;
            if (wall != null && SmoothedWalls.Contains(wall))
            {
                return true;
            }

            TerrainDef floor = def as TerrainDef;
            return floor != null && SmoothedFloors.Contains(floor);
        }

        internal static bool IsSmoothedFloor(TerrainDef def)
        {
            return def != null && SmoothedFloors.Contains(def);
        }

        internal static void ApplyFloorWealthCache(float[] cachedTerrainMarketValue)
        {
            SmoothingSettings settings = SmoothingExpandedMod.Settings;
            if (settings == null || cachedTerrainMarketValue == null)
            {
                return;
            }

            if (settings.OverrideFloorWealth)
            {
                OverriddenFloorCaches.Add(cachedTerrainMarketValue);
            }
            else if (!OverriddenFloorCaches.Remove(cachedTerrainMarketValue))
            {
                // Default-off remains a true no-op. Restoration runs only once
                // for a cache that this mod previously changed.
                return;
            }

            foreach (TerrainDef floor in SmoothedFloors)
            {
                if (floor.index >= 0 && floor.index < cachedTerrainMarketValue.Length)
                {
                    // When disabled, rebuild the entry from the current definition
                    // rather than retaining our previous override. This also respects
                    // definition changes made by Core or other active mods.
                    cachedTerrainMarketValue[floor.index] = settings.OverrideFloorWealth
                        ? settings.SmoothedFloorValue
                        : floor.GetStatValueAbstract(StatDefOf.MarketValue);
                }
            }
        }
    }

    [StaticConstructorOnStartup]
    internal static class ChunkConstructionController
    {
        private static readonly MethodInfo ResolveDesignatorsMethod =
            typeof(DesignationCategoryDef).GetMethod(
                "ResolveDesignators",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ActiveDesignatorField =
            typeof(Designator_Dropdown).GetField(
                "activeDesignator",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Dictionary<Type, List<FieldInfo>> RenderCacheFields =
            new Dictionary<Type, List<FieldInfo>>();
        private static bool renderCacheFailureLogged;
        private static bool resolveDesignatorsUnavailableLogged;
        private sealed class StoneBinding
        {
            internal readonly string Stone;
            internal readonly string Blocks;
            internal readonly string SmoothWall;
            internal readonly string Tile;
            internal readonly string BuiltWall;
            internal readonly string BuiltFloor;
            internal readonly string NaturalWallProxy;
            internal readonly string NaturalFloorProxy;

            internal StoneBinding(string stone)
            {
                Stone = stone;
                Blocks = "Blocks" + stone;
                SmoothWall = "Smoothed" + stone;
                Tile = "Tile" + stone;
                BuiltWall = "SmoothingExpanded_ChunkSmoothWall" + stone;
                BuiltFloor = "SmoothingExpanded_ChunkSmoothFloor" + stone;
                NaturalWallProxy = "SmoothingExpanded_ChunkSmoothNaturalWall" + stone;
                NaturalFloorProxy = "SmoothingExpanded_ChunkSmoothNaturalFloor" + stone;
            }
        }

        private static readonly StoneBinding[] Bindings =
        {
            new StoneBinding("Sandstone"),
            new StoneBinding("Granite"),
            new StoneBinding("Limestone"),
            new StoneBinding("Slate"),
            new StoneBinding("Marble"),
            // Odyssey-only definitions use MayRequire in XML. Keeping the
            // binding here is safe without the DLC because every lookup is
            // silent and simply returns null when Vacstone does not exist.
            new StoneBinding("Vacstone")
        };

        static ChunkConstructionController()
        {
            // A catalogue/configuration failure must never poison the type and
            // cascade into conversion, Harmony wealth hooks, or map generation.
            try
            {
                ApplyStartupConfiguration();
            }
            catch (Exception exception)
            {
                Log.Error("[Smoothing Expanded] Chunk catalogue setup failed; " +
                    "the rest of the mod will remain available. " + exception);
            }
        }

        private static void ApplyStartupConfiguration()
        {
            SmoothingSettings settings = SmoothingExpandedMod.Settings;
            ThingDef normalWall = DefDatabase<ThingDef>.GetNamedSilentFail("Wall");
            DesignationCategoryDef structure = DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("Structure");
            if (settings == null || normalWall == null)
            {
                return;
            }

            StatDef marketValue = DefDatabase<StatDef>.GetNamedSilentFail("MarketValue");
            StatDef workToBuild = DefDatabase<StatDef>.GetNamedSilentFail("WorkToBuild");
            StatDef beauty = DefDatabase<StatDef>.GetNamedSilentFail("Beauty");
            StatDef mass = DefDatabase<StatDef>.GetNamedSilentFail("Mass");

            int configuredWalls = 0;
            int configuredFloors = 0;
            bool showConstructed = settings.EnableChunkConstruction &&
                settings.ChunkSurfaceResultMode != 1;
            bool showNatural = settings.EnableChunkConstruction &&
                settings.ChunkSurfaceResultMode != 0;
            // Follow the category currently assigned to vanilla Wall. This
            // preserves vanilla Structure, but also cooperates with architect
            // organisers such as Better Architect Menu which relocate Wall.
            DesignationCategoryDef wallCategory = normalWall.designationCategory ?? structure;
            for (int i = 0; i < Bindings.Length; i++)
            {
                StoneBinding binding = Bindings[i];
                ThingDef chunk = DefDatabase<ThingDef>.GetNamedSilentFail("Chunk" + binding.Stone);
                ThingDef blocks = DefDatabase<ThingDef>.GetNamedSilentFail(binding.Blocks);
                ThingDef smoothWall = DefDatabase<ThingDef>.GetNamedSilentFail(binding.SmoothWall);
                ThingDef builtWall = DefDatabase<ThingDef>.GetNamedSilentFail(binding.BuiltWall);
                TerrainDef normalTile = DefDatabase<TerrainDef>.GetNamedSilentFail(binding.Tile);
                TerrainDef smoothFloor = FindNaturalSmoothFloor(binding.Stone);
                TerrainDef builtFloor = DefDatabase<TerrainDef>.GetNamedSilentFail(binding.BuiltFloor);

                if (blocks != null && smoothWall != null && builtWall != null)
                {
                    builtWall.designationCategory = showConstructed ? wallCategory : null;
                    builtWall.uiIconPath = smoothWall.uiIconPath;
                    builtWall.uiIconColor = smoothWall.graphicData == null
                        ? Color.white
                        : smoothWall.graphicData.color;
                    builtWall.uiIconColorTwo = smoothWall.graphicData == null
                        ? Color.white
                        : smoothWall.graphicData.colorTwo;
                    if (builtWall.graphicData != null && smoothWall.graphicData != null)
                    {
                        builtWall.graphicData.texPath = smoothWall.graphicData.texPath;
                        builtWall.graphicData.graphicClass = smoothWall.graphicData.graphicClass;
                        builtWall.graphicData.shaderType = smoothWall.graphicData.shaderType;
                        builtWall.graphicData.drawSize = smoothWall.graphicData.drawSize;
                        builtWall.graphicData.linkType = smoothWall.graphicData.linkType;
                        builtWall.graphicData.color = smoothWall.graphicData.color;
                        builtWall.graphicData.colorTwo = smoothWall.graphicData.colorTwo;
                        builtWall.graphicData.linkFlags = LinkFlags.Wall | LinkFlags.Rock;
                    }
                    ClearRenderCaches(builtWall.graphicData);
                    ClearRenderCaches(builtWall);

                    // Vanilla and XML-patched wall commands (doors, vents,
                    // over-wall coolers, etc.) live in this list. Minification
                    // gizmos do not, so they are intentionally not inherited.
                    MergeRelatedBuildCommands(normalWall, smoothWall);
                    MergeRelatedBuildCommands(normalWall, builtWall);

                    // Start from the live normal Wall definition and its matching
                    // stone-block Stuff. This includes stat changes made by other
                    // mods rather than duplicating a fragile vanilla snapshot.
                    CopyStuffAdjustedWallStats(normalWall, blocks, builtWall);
                    builtWall.terrainAffordanceNeeded =
                        ThingUtility.GetTerrainAffordanceNeed(normalWall, blocks);
                    builtWall.useStuffTerrainAffordance = false;

                    // The entire raw chunk becomes this fixed-material wall, so
                    // use the chunk's live mass rather than the lighter block-wall
                    // mass. Mods which alter chunk mass are respected as well.
                    if (mass != null && chunk != null)
                    {
                        builtWall.SetStatBaseValue(mass, chunk.GetStatValueAbstract(mass));
                    }
                    if (beauty != null)
                    {
                        builtWall.SetStatBaseValue(
                            beauty,
                            settings.ChunkWallsHaveBeauty
                                ? SmoothingSpeedController.OriginalBeautyFor(smoothWall, beauty)
                                : settings.ChunkWallBeautyValue);
                    }
                    if (marketValue != null)
                    {
                        float value = settings.ChunkWallsHaveWealth
                            ? normalWall.GetStatValueAbstract(marketValue, blocks)
                            : settings.ChunkWallWealthValue;
                        builtWall.SetStatBaseValue(marketValue, value);
                    }
                    builtWall.ResolveReferences();
                    configuredWalls++;
                }

                if (normalTile != null && builtFloor != null)
                {
                    builtFloor.designationCategory = showConstructed
                        ? normalTile.designationCategory
                        : null;
                    if (smoothFloor != null)
                    {
                        builtFloor.texturePath = smoothFloor.texturePath;
                        // Stone-colour mods commonly patch the named vanilla
                        // Tile* defs but not Core's generated natural-smoothed
                        // terrains or unknown third-party defs. The matching
                        // stone tile is therefore the authoritative live tint,
                        // while the natural smooth terrain remains the source
                        // for texture and rendering-related properties.
                        builtFloor.color = normalTile.color;
                        builtFloor.uiIconColor = normalTile.uiIconColor;
                        builtFloor.uiIconColorTwo = normalTile.uiIconColorTwo;
                        // RimWorld's FadeRough renderer connects neighbours only
                        // when their actual TerrainDef references are identical.
                        // Our independently deconstructible terrain and vanilla's
                        // natural smooth terrain must remain separate defs, so
                        // equal-precedence fades overlap unpredictably even when
                        // every visual field matches. Hard edges are the stable,
                        // non-invasive fallback and keep graphics inside the cell.
                        builtFloor.edgeType = TerrainDef.TerrainEdgeType.Hard;
                        builtFloor.renderPrecedence = smoothFloor.renderPrecedence + 1;
                        builtFloor.isPaintable = smoothFloor.isPaintable;
                        builtFloor.pollutedTexturePath = smoothFloor.pollutedTexturePath;
                        builtFloor.pollutionOverlayTexturePath = smoothFloor.pollutionOverlayTexturePath;
                        builtFloor.pollutionColor = smoothFloor.pollutionColor;
                        if (beauty != null)
                        {
                            builtFloor.SetStatBaseValue(
                                beauty,
                                settings.ChunkFloorsHaveBeauty
                                    ? SmoothingSpeedController.OriginalBeautyFor(smoothFloor, beauty)
                                    : settings.ChunkFloorBeautyValue);
                        }
                    }
                    ClearRenderCaches(builtFloor);
                    if (marketValue != null)
                    {
                        float value = settings.ChunkFloorsHaveWealth
                            ? normalTile.GetStatValueAbstract(marketValue)
                            : settings.ChunkFloorWealthValue;
                        builtFloor.SetStatBaseValue(marketValue, value);
                    }
                    builtFloor.ResolveReferences();
                    configuredFloors++;
                }

                ThingDef naturalWallProxy =
                    DefDatabase<ThingDef>.GetNamedSilentFail(binding.NaturalWallProxy);
                ThingDef naturalFloorProxy =
                    DefDatabase<ThingDef>.GetNamedSilentFail(binding.NaturalFloorProxy);

                if (blocks != null && smoothWall != null && naturalWallProxy != null)
                {
                    naturalWallProxy.designationCategory = showNatural ? wallCategory : null;
                    naturalWallProxy.uiIconPath = smoothWall.uiIconPath;
                    naturalWallProxy.uiIconColor = smoothWall.graphicData == null
                        ? Color.white
                        : smoothWall.graphicData.color;
                    naturalWallProxy.uiIconColorTwo = smoothWall.graphicData == null
                        ? Color.white
                        : smoothWall.graphicData.colorTwo;
                    if (naturalWallProxy.graphicData != null && smoothWall.graphicData != null)
                    {
                        naturalWallProxy.graphicData.texPath = smoothWall.graphicData.texPath;
                        naturalWallProxy.graphicData.graphicClass = smoothWall.graphicData.graphicClass;
                        naturalWallProxy.graphicData.shaderType = smoothWall.graphicData.shaderType;
                        naturalWallProxy.graphicData.drawSize = smoothWall.graphicData.drawSize;
                        naturalWallProxy.graphicData.linkType = smoothWall.graphicData.linkType;
                        naturalWallProxy.graphicData.color = smoothWall.graphicData.color;
                        naturalWallProxy.graphicData.colorTwo = smoothWall.graphicData.colorTwo;
                    }
                    ClearRenderCaches(naturalWallProxy.graphicData);
                    ClearRenderCaches(naturalWallProxy);
                    MergeRelatedBuildCommands(normalWall, naturalWallProxy);
                    CopyStuffAdjustedStat(normalWall, blocks, naturalWallProxy, workToBuild);
                    naturalWallProxy.ResolveReferences();

                    // Natural smooth walls are vanilla Things, so attach a tiny
                    // Core-only comp to expose Build copy for the matching
                    // one-chunk natural construction proxy. Avoid duplicates if
                    // another definition pass occurs during development reloads.
                    if (showNatural)
                    {
                        if (smoothWall.comps == null)
                        {
                            smoothWall.comps = new List<CompProperties>();
                        }
                        bool hasBuildCopy = false;
                        for (int compIndex = 0; compIndex < smoothWall.comps.Count; compIndex++)
                        {
                            if (smoothWall.comps[compIndex] is CompProperties_NaturalWallBuildCopy)
                            {
                                hasBuildCopy = true;
                                break;
                            }
                        }
                        if (!hasBuildCopy)
                        {
                            smoothWall.comps.Add(new CompProperties_NaturalWallBuildCopy
                            {
                                naturalProxy = naturalWallProxy
                            });
                        }
                    }
                }

                if (normalTile != null && smoothFloor != null && naturalFloorProxy != null)
                {
                    naturalFloorProxy.designationCategory = showNatural
                        ? normalTile.designationCategory
                        : null;
                    naturalFloorProxy.uiIconPath = smoothFloor.texturePath;
                    naturalFloorProxy.uiIconColor = normalTile.color;
                    if (naturalFloorProxy.graphicData != null)
                    {
                        naturalFloorProxy.graphicData.texPath = smoothFloor.texturePath;
                        naturalFloorProxy.graphicData.color = normalTile.color;
                    }
                    ClearRenderCaches(naturalFloorProxy);
                    if (workToBuild != null)
                    {
                        naturalFloorProxy.SetStatBaseValue(
                            workToBuild,
                            normalTile.GetStatValueAbstract(workToBuild));
                    }
                    naturalFloorProxy.ResolveReferences();
                }
            }

            EnableMinifyEverythingCompatibility();

            Log.Message("[Smoothing Expanded] Chunk construction " +
                (settings.EnableChunkConstruction ? "enabled" : "hidden") +
                "; configured " + configuredWalls + " walls and " + configuredFloors +
                " floors. Changes to these startup options require a restart.");

            // Architect categories cache their designators during definition
            // resolution. Rebuild the two affected caches after showing/hiding
            // our definitions so disabled really means no extra buttons.
            RebuildDesignators(structure);
            if (wallCategory != structure)
            {
                RebuildDesignators(wallCategory);
            }
            DesignationCategoryDef floors =
                DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("Floors");
            RebuildDesignators(floors);
            ReplaceChunkSurfaceDesignators(wallCategory);
            if (wallCategory != structure)
            {
                ReplaceChunkSurfaceDesignators(structure);
            }
            ReplaceChunkSurfaceDesignators(floors);
            SortChunkSmoothDropdowns(wallCategory);
            if (wallCategory != structure)
            {
                SortChunkSmoothDropdowns(structure);
            }
            SortChunkSmoothDropdowns(floors);
            // Keep the live category field explicit after rebuilding. Visible
            // Wealth groups buildings solely by this field, while RimWorld's
            // Architect UI uses the already rebuilt category designators.
            for (int i = 0; i < Bindings.Length; i++)
            {
                ThingDef builtWall = DefDatabase<ThingDef>.GetNamedSilentFail(Bindings[i].BuiltWall);
                if (builtWall != null)
                {
                    builtWall.designationCategory = showConstructed ? wallCategory : null;
                }
                ThingDef naturalWall = DefDatabase<ThingDef>.GetNamedSilentFail(Bindings[i].NaturalWallProxy);
                if (naturalWall != null)
                {
                    naturalWall.designationCategory = showNatural ? wallCategory : null;
                }
            }
        }

        private static void EnableMinifyEverythingCompatibility()
        {
            // MinifyEverything 1.6 excludes every ThingDef whose defName starts
            // with "Smooth". Our stable, save-facing names begin with
            // "SmoothingExpanded_", so they are unintentionally filtered out.
            // Use its public runtime API instead of renaming our defs (which
            // would break existing saves), and keep the integration completely
            // inert when that optional mod is absent.
            Type minifyType = null;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length && minifyType == null; i++)
            {
                minifyType = assemblies[i].GetType(
                    "MinifyEverything.MinifyEverything", false);
            }
            if (minifyType == null)
            {
                return;
            }

            MethodInfo addMinifiedFor = minifyType.GetMethod(
                "AddMinifiedFor",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(ThingDef), typeof(bool) },
                null);
            if (addMinifiedFor == null)
            {
                Log.Warning("[Smoothing Expanded] MinifyEverything is active, " +
                    "but its AddMinifiedFor API was not found. Chunk-built " +
                    "walls will remain non-minifiable.");
                return;
            }

            System.Collections.IList disabledDefs = null;
            Type minifyModType = minifyType.Assembly.GetType(
                "MinifyEverything.MinifyMod", false);
            if (minifyModType != null)
            {
                FieldInfo instanceField = minifyModType.GetField(
                    "instance", BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static);
                object minifyMod = instanceField == null
                    ? null
                    : instanceField.GetValue(null);
                PropertyInfo settingsProperty = minifyModType.GetProperty(
                    "Settings", BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance);
                object minifySettings = minifyMod == null || settingsProperty == null
                    ? null
                    : settingsProperty.GetValue(minifyMod, null);
                FieldInfo disabledField = minifySettings == null
                    ? null
                    : minifySettings.GetType().GetField(
                        "disabledDefList",
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance);
                disabledDefs = disabledField == null
                    ? null
                    : disabledField.GetValue(minifySettings) as System.Collections.IList;
            }

            int added = 0;
            try
            {
                for (int i = 0; i < Bindings.Length; i++)
                {
                    ThingDef wall = DefDatabase<ThingDef>.GetNamedSilentFail(
                        Bindings[i].BuiltWall);
                    if (wall == null || wall.minifiedDef != null ||
                        (disabledDefs != null && disabledDefs.Contains(wall)))
                    {
                        continue;
                    }

                    addMinifiedFor.Invoke(null, new object[] { wall, true });
                    added++;
                }
            }
            catch (Exception exception)
            {
                Log.Warning("[Smoothing Expanded] MinifyEverything compatibility " +
                    "could not finish; remaining chunk-built walls will stay " +
                    "non-minifiable. " + exception);
                return;
            }

            if (added > 0)
            {
                Log.Message("[Smoothing Expanded] MinifyEverything compatibility " +
                    "enabled for " + added + " chunk-built walls.");
            }
        }

        private static void MergeRelatedBuildCommands(ThingDef source, ThingDef target)
        {
            if (source == null || source.building == null || target == null ||
                target.building == null || source.building.relatedBuildCommands == null)
            {
                return;
            }

            if (target.building.relatedBuildCommands == null)
            {
                target.building.relatedBuildCommands = new List<ThingDef>();
            }

            for (int i = 0; i < source.building.relatedBuildCommands.Count; i++)
            {
                ThingDef command = source.building.relatedBuildCommands[i];
                if (command != null && !target.building.relatedBuildCommands.Contains(command))
                {
                    target.building.relatedBuildCommands.Add(command);
                }
            }
        }

        private static void ClearRenderCaches(object definition)
        {
            if (definition == null)
            {
                return;
            }
            // Graphic/material getters may be touched while Architect
            // designators are resolved, before this compatibility pass copies
            // the final texture-pack values. Clear only known render-cache
            // fields so the next draw rebuilds them from the updated def.
            string[] cacheNames =
            {
                "graphicInt", "cachedGraphic", "cachedGraphicFull",
                "cachedMat", "cachedMaterial", "cachedMats"
            };

            Type definitionType = definition.GetType();
            List<FieldInfo> fields;
            if (!RenderCacheFields.TryGetValue(definitionType, out fields))
            {
                fields = new List<FieldInfo>();
                Type type = definitionType;
                while (type != null)
                {
                    for (int i = 0; i < cacheNames.Length; i++)
                    {
                        FieldInfo field = type.GetField(
                            cacheNames[i],
                            BindingFlags.Instance | BindingFlags.Public |
                            BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                        if (field != null && !field.FieldType.IsValueType)
                        {
                            fields.Add(field);
                        }
                    }
                    type = type.BaseType;
                }
                RenderCacheFields.Add(definitionType, fields);
            }
            for (int i = 0; i < fields.Count; i++)
            {
                try
                {
                    fields[i].SetValue(definition, null);
                }
                catch (Exception exception)
                {
                    if (!renderCacheFailureLogged)
                    {
                        renderCacheFailureLogged = true;
                        Log.Warning("[Smoothing Expanded] A render-cache field " +
                            "could not be cleared. Remaining compatibility setup " +
                            "will continue. " + exception);
                    }
                }
            }
        }

        private static void RebuildDesignators(DesignationCategoryDef category)
        {
            if (category == null)
            {
                return;
            }

            if (ResolveDesignatorsMethod == null)
            {
                if (!resolveDesignatorsUnavailableLogged)
                {
                    resolveDesignatorsUnavailableLogged = true;
                    Log.Warning("[Smoothing Expanded] RimWorld's Architect " +
                        "catalogue resolver was not found. Existing catalogue " +
                        "entries will be left unchanged.");
                }
                return;
            }

            if (ResolveDesignatorsMethod != null)
            {
                try
                {
                    ResolveDesignatorsMethod.Invoke(category, null);
                    category.DirtyCache();
                }
                catch (Exception exception)
                {
                    Log.Error("[Smoothing Expanded] Could not rebuild the " +
                        category.defName + " Architect catalogue. " + exception);
                }
            }
        }

        private static void ReplaceChunkSurfaceDesignators(DesignationCategoryDef category)
        {
            if (category == null || category.AllResolvedDesignators == null)
            {
                return;
            }

            for (int i = 0; i < category.AllResolvedDesignators.Count; i++)
            {
                Designator_Dropdown dropdown =
                    category.AllResolvedDesignators[i] as Designator_Dropdown;
                if (dropdown == null || dropdown is Designator_DropdownChunkSurface ||
                    !IsChunkSmoothDropdown(dropdown))
                {
                    continue;
                }

                for (int elementIndex = 0; elementIndex < dropdown.Elements.Count; elementIndex++)
                {
                    Designator_Build build = dropdown.Elements[elementIndex] as Designator_Build;
                    BuildableDef buildable = build == null ? null : build.PlacingDef;
                    if (buildable != null && buildable.defName.StartsWith(
                        "SmoothingExpanded_ChunkSmooth", StringComparison.Ordinal))
                    {
                        Designator replacement = buildable.defName.StartsWith(
                            "SmoothingExpanded_ChunkSmoothNaturalFloor", StringComparison.Ordinal)
                            ? (Designator)new Designator_BuildNaturalFloor(buildable)
                            : new Designator_BuildChunkSurface(buildable);
                        dropdown.Elements[elementIndex] = replacement;
                        // ResolveDesignators may already have cached this element
                        // as the dropdown's left-click choice. Update that pointer
                        // as well, otherwise only a later right-click choice would
                        // receive the floor shape catalogue.
                        if (ActiveDesignatorField != null &&
                            ActiveDesignatorField.GetValue(dropdown) == build)
                        {
                            dropdown.SetActiveDesignator(replacement, false);
                        }
                    }
                }

                // Use a specialised container so the command behaves like
                // vanilla's stuff-based wall command when none of its fixed
                // one-chunk children are currently affordable.
                Designator_DropdownChunkSurface replacementDropdown =
                    new Designator_DropdownChunkSurface();
                for (int elementIndex = 0;
                    elementIndex < dropdown.Elements.Count;
                    elementIndex++)
                {
                    replacementDropdown.Add(dropdown.Elements[elementIndex]);
                }
                category.AllResolvedDesignators[i] = replacementDropdown;
            }
        }

        private static void SortChunkSmoothDropdowns(DesignationCategoryDef category)
        {
            if (category == null || category.AllResolvedDesignators == null)
            {
                return;
            }

            for (int i = 0; i < category.AllResolvedDesignators.Count; i++)
            {
                Designator_Dropdown dropdown =
                    category.AllResolvedDesignators[i] as Designator_Dropdown;
                if (dropdown == null ||
                    !IsChunkSmoothDropdown(dropdown))
                {
                    continue;
                }

                dropdown.Elements.Sort(delegate(Designator left, Designator right)
                {
                    Designator_Place leftPlace = left as Designator_Place;
                    Designator_Place rightPlace = right as Designator_Place;
                    string leftLabel = leftPlace == null || leftPlace.PlacingDef == null
                        ? string.Empty
                        : leftPlace.PlacingDef.LabelCap.ToString();
                    string rightLabel = rightPlace == null || rightPlace.PlacingDef == null
                        ? string.Empty
                        : rightPlace.PlacingDef.LabelCap.ToString();
                    return string.Compare(
                        leftLabel, rightLabel, StringComparison.CurrentCultureIgnoreCase);
                });
            }
        }

        private static bool IsChunkSmoothDropdown(Designator_Dropdown dropdown)
        {
            for (int i = 0; i < dropdown.Elements.Count; i++)
            {
                Designator_Place place = dropdown.Elements[i] as Designator_Place;
                BuildableDef buildable = place == null ? null : place.PlacingDef;
                if (buildable != null && buildable.defName != null &&
                    buildable.defName.StartsWith(
                        "SmoothingExpanded_ChunkSmooth", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static void CopyStuffAdjustedStat(
            ThingDef source,
            ThingDef stuff,
            ThingDef destination,
            StatDef stat)
        {
            if (stat != null)
            {
                destination.SetStatBaseValue(stat, source.GetStatValueAbstract(stat, stuff));
            }
        }

        private static void CopyStuffAdjustedWallStats(
            ThingDef source,
            ThingDef stuff,
            ThingDef destination)
        {
            if (source == null || stuff == null || destination == null)
            {
                return;
            }

            // Copy only stats explicitly supplied by the live Wall definition.
            // GetStatValueAbstract applies the matching stone Stuff's factors to
            // those wall-relevant stats. Copying the Stuff's entire modifier list
            // would incorrectly expose furniture/weapon-only entries such as
            // RestEffectiveness and MeleeWeapon_CooldownMultiplier on a wall.
            // Market value and beauty are deliberately overwritten afterwards
            // by this mod's opt-in settings.
            HashSet<StatDef> stats = new HashSet<StatDef>();
            AddStatModifiers(stats, source.statBases);

            foreach (StatDef stat in stats)
            {
                if (stat != null)
                {
                    destination.SetStatBaseValue(
                        stat,
                        source.GetStatValueAbstract(stat, stuff));
                }
            }
        }

        private static void AddStatModifiers(
            HashSet<StatDef> destination,
            List<StatModifier> modifiers)
        {
            if (modifiers == null)
            {
                return;
            }

            for (int i = 0; i < modifiers.Count; i++)
            {
                StatModifier modifier = modifiers[i];
                if (modifier != null && modifier.stat != null)
                {
                    destination.Add(modifier.stat);
                }
            }
        }

        internal static void ApplyChunkFloorWealthCache(float[] cachedTerrainMarketValue)
        {
            SmoothingSettings settings = SmoothingExpandedMod.Settings;
            if (settings == null || settings.ChunkFloorsHaveWealth || cachedTerrainMarketValue == null)
            {
                return;
            }

            for (int i = 0; i < Bindings.Length; i++)
            {
                TerrainDef floor = DefDatabase<TerrainDef>.GetNamedSilentFail(Bindings[i].BuiltFloor);
                if (floor != null && floor.index >= 0 && floor.index < cachedTerrainMarketValue.Length)
                {
                    cachedTerrainMarketValue[floor.index] =
                        settings.ChunkFloorWealthValue;
                }
            }
        }

        internal static void ShowConversionConfirmation()
        {
            if (Current.Game == null || Find.Maps == null || Find.Maps.Count == 0)
            {
                Messages.Message(
                    "SmoothingExpanded.ConversionNeedsSave".Translate(),
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "SmoothingExpanded.ConversionWarning".Translate(),
                ConvertAllLoadedMaps,
                true,
                "SmoothingExpanded.ConversionTitle".Translate()));
        }

        private static void ConvertAllLoadedMaps()
        {
            int convertedFloors = 0;
            int convertedWalls = 0;
            int cancelledConstruction = 0;
            int unresolvedCustomSurfaces = 0;
            List<Map> maps = Find.Maps;

            for (int mapIndex = 0; mapIndex < maps.Count; mapIndex++)
            {
                Map map = maps[mapIndex];

                // Blueprints and frames still reference our definitions even
                // though no finished custom surface exists yet. Leaving them in
                // the save would produce missing-def errors after uninstall.
                // DestroyMode.Cancel follows Core's cancellation path and returns
                // any resources already delivered to a construction frame.
                List<Thing> unfinishedToCancel = new List<Thing>();
                List<Thing> mapThings = map.listerThings.AllThings;
                for (int thingIndex = 0; thingIndex < mapThings.Count; thingIndex++)
                {
                    Thing thing = mapThings[thingIndex];
                    BuildableDef target = thing.def == null
                        ? null
                        : thing.def.entityDefToBuild;
                    if (IsChunkSurfaceBuildTarget(target))
                    {
                        unfinishedToCancel.Add(thing);
                    }
                }
                for (int unfinishedIndex = 0;
                    unfinishedIndex < unfinishedToCancel.Count;
                    unfinishedIndex++)
                {
                    Thing unfinished = unfinishedToCancel[unfinishedIndex];
                    if (unfinished != null && unfinished.Spawned)
                    {
                        unfinished.Destroy(DestroyMode.Cancel);
                        cancelledConstruction++;
                    }
                }

                // Terrain has no Thing instance to replace. Inspired by Core's
                // terrain grid operations, replace only our exact defNames so
                // unrelated constructed or modded flooring is never touched.
                for (int cellIndex = 0; cellIndex < map.cellIndices.NumGridCells; cellIndex++)
                {
                    IntVec3 cell = map.cellIndices.IndexToCell(cellIndex);
                    TerrainDef current = map.terrainGrid.TerrainAt(cell);
                    StoneBinding binding = BindingForBuiltFloor(current);
                    if (binding == null)
                    {
                        continue;
                    }

                    TerrainDef naturalSmooth = FindNaturalSmoothFloor(binding.Stone);
                    if (naturalSmooth != null)
                    {
                        // Terrain paint lives in a separate grid. SetTerrain can
                        // clear it, so preserve and explicitly restore the Core
                        // ColorDef after replacing the custom TerrainDef.
                        ColorDef paint = map.terrainGrid.ColorAt(cell);
                        map.terrainGrid.SetTerrain(cell, naturalSmooth);
                        if (paint != null)
                        {
                            map.terrainGrid.SetTerrainColor(cell, paint);
                        }
                        convertedFloors++;
                    }
                }

                // Copy the list before replacement because destroying and
                // spawning walls mutates the map's live thing collection.
                List<Thing> wallsToConvert = new List<Thing>();
                List<Thing> allThings = map.listerThings.AllThings;
                for (int thingIndex = 0; thingIndex < allThings.Count; thingIndex++)
                {
                    Thing thing = allThings[thingIndex];
                    if (BindingForBuiltWall(thing.def) != null)
                    {
                        wallsToConvert.Add(thing);
                    }
                }

                for (int wallIndex = 0; wallIndex < wallsToConvert.Count; wallIndex++)
                {
                    Thing oldWall = wallsToConvert[wallIndex];
                    StoneBinding binding = BindingForBuiltWall(oldWall.def);
                    ThingDef naturalSmoothWall = binding == null
                        ? null
                        : DefDatabase<ThingDef>.GetNamedSilentFail(binding.SmoothWall);
                    if (binding == null || naturalSmoothWall == null || !oldWall.Spawned)
                    {
                        continue;
                    }

                    IntVec3 position = oldWall.Position;
                    Rot4 rotation = oldWall.Rotation;
                    Faction faction = oldWall.Faction;
                    float hitPointFraction = oldWall.MaxHitPoints > 0
                        ? (float)oldWall.HitPoints / oldWall.MaxHitPoints
                        : 1f;
                    CompColorable oldColor = oldWall.TryGetComp<CompColorable>();
                    bool transferColor = oldColor != null && oldColor.Active;
                    Color color = transferColor ? oldColor.Color : Color.white;

                    oldWall.Destroy(DestroyMode.Vanish);
                    Thing newWall = ThingMaker.MakeThing(naturalSmoothWall);
                    newWall.SetFactionDirect(faction);
                    GenSpawn.Spawn(newWall, position, map, rotation, WipeMode.Vanish);
                    newWall.HitPoints = Mathf.Clamp(
                        Mathf.RoundToInt(newWall.MaxHitPoints * hitPointFraction),
                        1,
                        newWall.MaxHitPoints);
                    CompColorable newColor = newWall.TryGetComp<CompColorable>();
                    if (transferColor && newColor != null)
                    {
                        newColor.SetColor(color);
                    }
                    convertedWalls++;
                }

                // Minified walls, inventories, transport containers and other
                // nested ThingOwners are not present as their inner objects in
                // ListerThings. Walk every map holder so packed custom walls are
                // rewritten to vanilla defs before the mod is removed.
                HashSet<IThingHolder> visitedMapHolders = new HashSet<IThingHolder>();
                List<Thing> holderRoots = new List<Thing>(map.listerThings.AllThings);
                for (int rootIndex = 0; rootIndex < holderRoots.Count; rootIndex++)
                {
                    IThingHolder holder = holderRoots[rootIndex] as IThingHolder;
                    if (holder != null)
                    {
                        convertedWalls += ConvertWallsInHolder(
                            holder, visitedMapHolders);
                    }
                }

                map.wealthWatcher.ForceRecount(true);
                map.mapDrawer.RegenerateEverythingNow();
            }

            // Caravans and other world objects can also own minified buildings.
            // They are outside every loaded map and therefore need a separate
            // recursive pass.
            HashSet<IThingHolder> visitedWorldHolders = new HashSet<IThingHolder>();
            if (Find.WorldObjects != null)
            {
                List<RimWorld.Planet.WorldObject> worldObjects =
                    Find.WorldObjects.AllWorldObjects;
                for (int worldIndex = 0; worldIndex < worldObjects.Count; worldIndex++)
                {
                    IThingHolder holder = worldObjects[worldIndex] as IThingHolder;
                    if (holder != null)
                    {
                        convertedWalls += ConvertWallsInHolder(
                            holder, visitedWorldHolders);
                    }
                }
            }

            unresolvedCustomSurfaces = CountRemainingCustomSurfaces(maps);

            if (unresolvedCustomSurfaces > 0)
            {
                string warning = "SmoothingExpanded.ConversionIncomplete".Translate(
                    unresolvedCustomSurfaces);
                Messages.Message(warning, MessageTypeDefOf.RejectInput, false);
                Log.Error("[Smoothing Expanded] Uninstall preparation converted " +
                    convertedFloors + " floor tile(s) and " + convertedWalls +
                    " wall(s), and cancelled " + cancelledConstruction +
                    " unfinished construction(s), but could not safely resolve " +
                    unresolvedCustomSurfaces + " custom surface(s). Settings were " +
                    "not reset. " + warning);
                return;
            }

            string report = "SmoothingExpanded.ConversionReport".Translate(
                convertedFloors, convertedWalls, maps.Count, cancelledConstruction);
            Messages.Message(report, MessageTypeDefOf.TaskCompletion, false);
            Log.Message("[Smoothing Expanded] " + report);
            // Preparation finishes by returning the whole chunk-construction
            // category to its safe defaults. Architect visibility changes take
            // effect after the next restart, just like manual category changes.
            SmoothingExpandedMod.ResetChunkSettingsToDefaults();
        }

        private static int ConvertWallsInHolder(
            IThingHolder holder,
            HashSet<IThingHolder> visited)
        {
            if (holder == null || !visited.Add(holder))
            {
                return 0;
            }

            int converted = 0;
            ThingOwner directlyHeld = holder.GetDirectlyHeldThings();
            if (directlyHeld != null)
            {
                List<Thing> snapshot = new List<Thing>();
                for (int i = 0; i < directlyHeld.Count; i++)
                {
                    snapshot.Add(directlyHeld[i]);
                }

                for (int i = 0; i < snapshot.Count; i++)
                {
                    Thing outer = snapshot[i];
                    Thing inner = outer == null ? null : outer.GetInnerIfMinified();
                    if (inner != null && BindingForBuiltWall(inner.def) != null &&
                        ConvertUnspawnedWallDefinition(inner))
                    {
                        converted++;
                    }

                    IThingHolder child = outer as IThingHolder;
                    if (child != null)
                    {
                        converted += ConvertWallsInHolder(child, visited);
                    }
                }
            }

            List<IThingHolder> children = new List<IThingHolder>();
            holder.GetChildHolders(children);
            for (int i = 0; i < children.Count; i++)
            {
                converted += ConvertWallsInHolder(children[i], visited);
            }
            return converted;
        }

        private static bool ConvertUnspawnedWallDefinition(Thing wall)
        {
            StoneBinding binding = wall == null ? null : BindingForBuiltWall(wall.def);
            ThingDef target = binding == null
                ? null
                : DefDatabase<ThingDef>.GetNamedSilentFail(binding.SmoothWall);
            if (wall == null || target == null || wall.Spawned)
            {
                return false;
            }

            float hitPointFraction = wall.MaxHitPoints > 0
                ? (float)wall.HitPoints / wall.MaxHitPoints
                : 1f;
            wall.def = target;
            wall.HitPoints = Mathf.Clamp(
                Mathf.RoundToInt(wall.MaxHitPoints * hitPointFraction),
                1,
                wall.MaxHitPoints);
            return true;
        }

        private static int CountRemainingCustomSurfaces(List<Map> maps)
        {
            int remaining = 0;
            HashSet<Thing> seenThings = new HashSet<Thing>();
            for (int mapIndex = 0; mapIndex < maps.Count; mapIndex++)
            {
                Map map = maps[mapIndex];
                for (int cellIndex = 0; cellIndex < map.cellIndices.NumGridCells; cellIndex++)
                {
                    if (BindingForBuiltFloor(map.terrainGrid.TerrainAt(
                        map.cellIndices.IndexToCell(cellIndex))) != null)
                    {
                        remaining++;
                    }
                }
                remaining += CountCustomWallsInThings(
                    map.listerThings.AllThings, seenThings);
            }

            if (Find.WorldObjects != null)
            {
                List<RimWorld.Planet.WorldObject> worldObjects =
                    Find.WorldObjects.AllWorldObjects;
                List<Thing> worldThings = new List<Thing>();
                HashSet<IThingHolder> visited = new HashSet<IThingHolder>();
                for (int i = 0; i < worldObjects.Count; i++)
                {
                    CollectHeldThings(worldObjects[i] as IThingHolder,
                        worldThings, visited);
                }
                remaining += CountCustomWallsInThings(worldThings, seenThings);
            }
            return remaining;
        }

        private static int CountCustomWallsInThings(
            IList<Thing> things,
            HashSet<Thing> seen)
        {
            int count = 0;
            List<Thing> held = new List<Thing>();
            HashSet<IThingHolder> visited = new HashSet<IThingHolder>();
            for (int i = 0; i < things.Count; i++)
            {
                Thing outer = things[i];
                Thing inner = outer == null ? null : outer.GetInnerIfMinified();
                if (inner != null && seen.Add(inner) &&
                    BindingForBuiltWall(inner.def) != null)
                {
                    count++;
                }
                CollectHeldThings(outer as IThingHolder, held, visited);
            }
            for (int i = 0; i < held.Count; i++)
            {
                Thing outer = held[i];
                Thing inner = outer == null ? null : outer.GetInnerIfMinified();
                if (inner != null && seen.Add(inner) &&
                    BindingForBuiltWall(inner.def) != null)
                {
                    count++;
                }
            }
            return count;
        }

        private static void CollectHeldThings(
            IThingHolder holder,
            List<Thing> things,
            HashSet<IThingHolder> visited)
        {
            if (holder == null || !visited.Add(holder))
            {
                return;
            }
            ThingOwner directlyHeld = holder.GetDirectlyHeldThings();
            if (directlyHeld != null)
            {
                for (int i = 0; i < directlyHeld.Count; i++)
                {
                    Thing thing = directlyHeld[i];
                    things.Add(thing);
                    CollectHeldThings(thing as IThingHolder, things, visited);
                }
            }
            List<IThingHolder> children = new List<IThingHolder>();
            holder.GetChildHolders(children);
            for (int i = 0; i < children.Count; i++)
            {
                CollectHeldThings(children[i], things, visited);
            }
        }

        private static bool IsChunkSurfaceBuildTarget(BuildableDef target)
        {
            return target != null &&
                target.defName != null &&
                target.defName.StartsWith("SmoothingExpanded_ChunkSmooth", StringComparison.Ordinal);
        }

        private static StoneBinding BindingForBuiltFloor(TerrainDef terrain)
        {
            if (terrain == null)
            {
                return null;
            }
            for (int i = 0; i < Bindings.Length; i++)
            {
                if (terrain.defName == Bindings[i].BuiltFloor)
                {
                    return Bindings[i];
                }
            }
            return null;
        }

        private static StoneBinding BindingForBuiltWall(ThingDef wall)
        {
            if (wall == null)
            {
                return null;
            }
            for (int i = 0; i < Bindings.Length; i++)
            {
                if (wall.defName == Bindings[i].BuiltWall)
                {
                    return Bindings[i];
                }
            }
            return null;
        }

        internal static TerrainDef FindNaturalSmoothFloor(string stone)
        {
            if (string.IsNullOrEmpty(stone))
            {
                return null;
            }

            // Core and Odyssey implied smooth-stone terrains use this exact,
            // language-independent convention (for example Sandstone_Smooth).
            // Prefer it so a similarly named terrain from another mod can never
            // win merely because it happened to load first.
            TerrainDef exact = DefDatabase<TerrainDef>.GetNamedSilentFail(
                stone + "_Smooth");
            if (IsSafeNaturalSmoothFloor(exact))
            {
                return exact;
            }

            List<TerrainDef> terrains = DefDatabase<TerrainDef>.AllDefsListForReading;
            // Next follow RimWorld's explicit rough-to-smoothed relationship.
            for (int i = 0; i < terrains.Count; i++)
            {
                TerrainDef source = terrains[i];
                string sourceName = source.defName ?? string.Empty;
                if (source.smoothedTerrain != null &&
                    sourceName.IndexOf(stone, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    IsSafeNaturalSmoothFloor(source.smoothedTerrain))
                {
                    return source.smoothedTerrain;
                }
            }

            TerrainDef fallback = null;
            for (int i = 0; i < terrains.Count; i++)
            {
                TerrainDef terrain = terrains[i];
                string defName = terrain.defName ?? string.Empty;
                bool matchesStone = defName.IndexOf(stone, StringComparison.OrdinalIgnoreCase) >= 0;
                bool isSmooth = defName.IndexOf("smooth", StringComparison.OrdinalIgnoreCase) >= 0;
                if (matchesStone && isSmooth && IsSafeNaturalSmoothFloor(terrain))
                {
                    if (fallback != null && fallback != terrain)
                    {
                        Log.Error("[Smoothing Expanded] Multiple fallback smoothed-floor " +
                            "targets matched " + stone + ": " + fallback.defName +
                            " and " + terrain.defName + ". Refusing an ambiguous conversion.");
                        return null;
                    }
                    fallback = terrain;
                }
            }
            return fallback;
        }

        private static bool IsSafeNaturalSmoothFloor(TerrainDef terrain)
        {
            return terrain != null &&
                (terrain.costList == null || terrain.costList.Count == 0) &&
                (terrain.defName == null || !terrain.defName.StartsWith(
                    "SmoothingExpanded_", StringComparison.Ordinal));
        }
    }

}
