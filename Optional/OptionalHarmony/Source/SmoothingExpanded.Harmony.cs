using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Lordlony.SmoothingExpanded
{
    // This assembly is conditionally loaded through LoadFolders.xml only when
    // Harmony is active. The main assembly deliberately has no Harmony reference,
    // allowing smoothing speed and wall wealth controls to work without it.
    [StaticConstructorOnStartup]
    internal static class OptionalHarmonyIntegration
    {
        static OptionalHarmonyIntegration()
        {
            try
            {
                new Harmony("lordlony.smoothingexpanded.optional").PatchAll();
                Log.Message("[Smoothing Expanded] Optional Harmony features enabled. " +
                    "Independent floor speed: " +
                    SmoothingExpandedMod.FloorSpeedFeatureAvailable +
                    "; floor wealth: " +
                    SmoothingExpandedMod.FloorWealthFeatureAvailable + ".");
            }
            catch (Exception exception)
            {
                SmoothingExpandedMod.FloorSpeedFeatureAvailable = false;
                SmoothingExpandedMod.FloorWealthFeatureAvailable = false;
                Log.Error("[Smoothing Expanded] Optional Harmony integration " +
                    "could not be initialized. Core features remain available. " +
                    exception);
            }
        }
    }

    // Better Architect Menu builds its special Floors screen differently from
    // RimWorld's Architect tabs: TerrainDefs are collected directly, while our
    // one-tick ThingDef proxies for vanilla-result floors are omitted. Filter
    // only the list passed into that screen and add the enabled proxies. Better
    // Architect already requires Harmony, so this compatibility stays entirely
    // inside the optional assembly and touches no global caches.
    [HarmonyPatch]
    internal static class BetterArchitectFloorCataloguePatch
    {
        private const string ConstructedPrefix =
            "SmoothingExpanded_ChunkSmoothFloor";
        private const string VanillaPrefix =
            "SmoothingExpanded_ChunkSmoothNaturalFloor";

        private static bool Prepare()
        {
            return ModsConfig.IsActive("ferny.BetterArchitect") &&
                AccessTools.TypeByName(
                    "BetterArchitect.ArchitectCategoryTab_DesignationTabOnGUI_Patch") != null;
        }

        private static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName(
                "BetterArchitect.ArchitectCategoryTab_DesignationTabOnGUI_Patch");
            return type == null
                ? null
                : AccessTools.Method(type, "DrawMaterialListForFloors");
        }

        private static void Prefix(ref List<Designator> __1)
        {
            SmoothingSettings settings = SmoothingExpandedMod.Settings;
            if (settings == null || __1 == null)
            {
                return;
            }

            bool showConstructed = settings.EnableChunkConstruction &&
                settings.ChunkSurfaceResultMode != 1;
            bool showVanilla = settings.EnableChunkConstruction &&
                settings.ChunkSurfaceResultMode != 0;

            HashSet<string> present = new HashSet<string>(
                StringComparer.Ordinal);
            for (int i = __1.Count - 1; i >= 0; i--)
            {
                Designator_Place place = __1[i] as Designator_Place;
                BuildableDef def = place == null ? null : place.PlacingDef;
                string defName = def == null ? null : def.defName;
                if (string.IsNullOrEmpty(defName))
                {
                    continue;
                }

                bool isVanilla = defName.StartsWith(
                    VanillaPrefix, StringComparison.Ordinal);
                bool isConstructed = !isVanilla && defName.StartsWith(
                    ConstructedPrefix, StringComparison.Ordinal);
                if ((isConstructed && !showConstructed) ||
                    (isVanilla && !showVanilla))
                {
                    __1.RemoveAt(i);
                    continue;
                }
                present.Add(defName);
            }

            if (!showVanilla)
            {
                return;
            }

            List<ThingDef> thingDefs =
                DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < thingDefs.Count; i++)
            {
                ThingDef proxy = thingDefs[i];
                if (proxy.defName != null &&
                    proxy.defName.StartsWith(
                        VanillaPrefix, StringComparison.Ordinal) &&
                    present.Add(proxy.defName))
                {
                    __1.Add(new Designator_BuildNaturalFloor(proxy));
                }
            }
        }
    }

    // A fallback for ordinary market-value queries. The main definition-level
    // wall change remains functional even when this optional assembly is absent.
    [HarmonyPatch]
    internal static class SmoothedSurfaceMarketValuePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(StatExtension),
                "GetStatValueAbstract",
                new Type[] { typeof(BuildableDef), typeof(StatDef), typeof(ThingDef) });
        }

        private static void Postfix(BuildableDef __0, StatDef __1, ref float __result)
        {
            if (__1 == StatDefOf.MarketValue &&
                SmoothingExpandedMod.Settings != null &&
                SmoothingExpandedMod.Settings.OverrideWallWealth &&
                __0 is ThingDef &&
                SmoothingSpeedController.IsSmoothedSurface(__0))
            {
                __result = SmoothingExpandedMod.Settings.SmoothedWallValue;
            }
        }
    }

    // Visible Wealth and RimWorld both read cachedTerrainMarketValue by TerrainDef
    // index. Update only detected smoothed-floor entries before vanilla recounts.
    [HarmonyPatch(typeof(WealthWatcher), "ForceRecount")]
    internal static class SmoothedFloorWealthRecountPatch
    {
        private static bool Prepare()
        {
            bool available = AccessTools.Method(
                typeof(WealthWatcher), "ForceRecount") != null;
            SmoothingExpandedMod.FloorWealthFeatureAvailable = available;
            if (!available)
            {
                Log.Warning("[Smoothing Expanded] Floor-wealth integration is " +
                    "unavailable because WealthWatcher.ForceRecount was not found.");
            }
            return available;
        }

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(float[] ___cachedTerrainMarketValue)
        {
            SmoothingSpeedController.ApplyFloorWealthCache(___cachedTerrainMarketValue);
            ChunkConstructionController.ApplyChunkFloorWealthCache(___cachedTerrainMarketValue);
        }
    }

    // Core uses the same SmoothingSpeed stat for walls and floors. The main
    // assembly keeps that shared stat at the wall setting so it remains useful
    // without Harmony. This narrowly adjusts only the work consumed by the
    // vanilla floor-smoothing toil, producing an independent floor multiplier.
    [HarmonyPatch]
    internal static class SeparateFloorSmoothingSpeedPatch
    {
        private static readonly FieldInfo DriverField;
        private static readonly FieldInfo WorkLeftField;

        static SeparateFloorSmoothingSpeedPatch()
        {
            Type displayClass = AccessTools.Inner(
                typeof(JobDriver_AffectFloor),
                "<>c__DisplayClass9_0");
            DriverField = displayClass == null
                ? null
                : AccessTools.Field(displayClass, "<>4__this");
            WorkLeftField = AccessTools.Field(typeof(JobDriver_AffectFloor), "workLeft");
        }

        private static MethodBase TargetMethod()
        {
            Type displayClass = AccessTools.Inner(
                typeof(JobDriver_AffectFloor),
                "<>c__DisplayClass9_0");
            return displayClass == null
                ? null
                : AccessTools.Method(displayClass, "<MakeNewToils>b__2");
        }

        private static bool Prepare()
        {
            bool available = TargetMethod() != null &&
                DriverField != null && WorkLeftField != null;
            SmoothingExpandedMod.FloorSpeedFeatureAvailable = available;
            if (!available)
            {
                Log.Warning("[Smoothing Expanded] Independent floor smoothing " +
                    "speed is unavailable because RimWorld's floor-work target " +
                    "changed. Floors will follow the wall speed.");
            }
            return available;
        }

        private static void Prefix(object __instance, ref float __state)
        {
            __state = ReadWorkLeft(__instance);
        }

        private static void Postfix(object __instance, float __state)
        {
            if (DriverField == null || WorkLeftField == null)
            {
                return;
            }

            object driver = DriverField.GetValue(__instance);
            if (!(driver is JobDriver_SmoothFloor))
            {
                return;
            }

            float after = (float)WorkLeftField.GetValue(driver);
            float vanillaWork = __state - after;
            if (vanillaWork > 0f)
            {
                WorkLeftField.SetValue(
                    driver,
                    __state - vanillaWork * SmoothingSpeedController.FloorToWallSpeedRatio);
            }
        }

        private static float ReadWorkLeft(object displayClass)
        {
            if (DriverField == null || WorkLeftField == null || displayClass == null)
            {
                return 0f;
            }
            object driver = DriverField.GetValue(displayClass);
            return driver == null ? 0f : (float)WorkLeftField.GetValue(driver);
        }
    }

}
