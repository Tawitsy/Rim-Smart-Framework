using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SmartAIFramework
{
    public class SmartAISettings : ModSettings
    {
        public bool moduleCombatEnabled = true;
        public bool moduleSurvivalEnabled = true;
        public bool moduleWorkEnabled = true;
        public bool moduleStructureEnabled = true;
        public bool moduleMindEnabled = true;

        public bool combatFiringDiscipline = true;
        public bool combatMuzzleDiscipline = true;
        public bool combatSmartCover = true;
        public bool combatRangedKiting = true;
        public bool allowDraftedKitingAssist = true;
        public bool combatExplosiveSafety = true;
        public bool allowDraftedForceFire = true;
        public bool smartCastPositionPenalties = true;
        public bool combatNearMissReaction = true;
        public float coverSearchRadius = 6f;

        public bool smartTaskDelegation = true;
        public bool localVisualCourtesy = true;
        public bool roomCentricWorkFocus = true;
        public bool antiSwarmingRedundancy = true;
        public bool commonSenseSanitation = true;
        public bool commonSenseFifoFood = true;
        public bool commonSenseHazardJobs = true;
        public bool superheatedFirefightingSafety = true;
        public bool environmentalHazardProtection = true;
        public bool preTripNeedsPreparation = true;
        public bool opportunisticHaulingOnRoute = true;
        public bool comfortSeating = true;
        public int jobSuppressionDurationSeconds = 25;
        public int jobSuppressionDurationTicks => jobSuppressionDurationSeconds * 60;
        public float hazardSensitivity = 25f;

        public bool buddyEmpathyAssistance = true;
        public bool selfPreservationMedicalRest = true;
        public bool emergencyBleedOutInterruption = true;
        public bool predatorSurvivalAlert = true;
        public bool sleepingCourtesy = true;
        public int crisisHorizonTicks = 3600;

        public bool structureDoorDiscipline = true;
        public bool structureAntiTrapping = true;
        public float acousticRadiusMultiplier = 1f;

        public bool mindTantrumSparesExplosives = true;

        public HashSet<string> disabledMentalBreaks = new HashSet<string>();

        public bool observeOnly = false;
        public bool debugLogging = false;

        public const int DefaultThreatExpiryTicks = 600;
        public const float DefaultThreatMinReach = 8f;
        public const float DefaultThreatMaxReach = 40f;
        public const int DefaultRegionCacheTicks = 60;
        public const int DefaultThreatWaitTicks = 120;
        public const int DefaultHeavyEvaluationsPerTick = 3;

        public int advThreatExpiryTicks = DefaultThreatExpiryTicks;
        public float advThreatMinReach = DefaultThreatMinReach;
        public float advThreatMaxReach = DefaultThreatMaxReach;
        public int advRegionCacheTicks = DefaultRegionCacheTicks;
        public int advThreatWaitTicks = DefaultThreatWaitTicks;
        public int advHeavyEvaluationsPerTick = DefaultHeavyEvaluationsPerTick;
        public bool devShowThreatOverlay = false;

        public void ResetAdvanced()
        {
            advThreatExpiryTicks = DefaultThreatExpiryTicks;
            advThreatMinReach = DefaultThreatMinReach;
            advThreatMaxReach = DefaultThreatMaxReach;
            advRegionCacheTicks = DefaultRegionCacheTicks;
            advThreatWaitTicks = DefaultThreatWaitTicks;
            advHeavyEvaluationsPerTick = DefaultHeavyEvaluationsPerTick;
        }

        public void ClampAdvanced()
        {
            advThreatExpiryTicks = Mathf.Clamp(advThreatExpiryTicks, 120, 1800);
            advThreatMinReach = Mathf.Clamp(advThreatMinReach, 4f, 20f);
            advThreatMaxReach = Mathf.Clamp(advThreatMaxReach, 20f, 60f);
            if (advThreatMaxReach < advThreatMinReach) advThreatMaxReach = advThreatMinReach;
            advRegionCacheTicks = Mathf.Clamp(advRegionCacheTicks, 15, 250);
            advThreatWaitTicks = Mathf.Clamp(advThreatWaitTicks, 30, 120);
            advHeavyEvaluationsPerTick = Mathf.Clamp(advHeavyEvaluationsPerTick, 1, 10);
        }

        private static Vector2 scrollPosition = Vector2.zero;

        private static float contentHeight = 1680f;
        private static List<MentalBreakDef> listedBreaks;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref moduleCombatEnabled, "moduleCombatEnabled", true);
            Scribe_Values.Look(ref moduleSurvivalEnabled, "moduleSurvivalEnabled", true);
            Scribe_Values.Look(ref moduleWorkEnabled, "moduleWorkEnabled", true);
            Scribe_Values.Look(ref moduleStructureEnabled, "moduleStructureEnabled", true);
            Scribe_Values.Look(ref combatFiringDiscipline, "combatFiringDiscipline", true);
            Scribe_Values.Look(ref combatMuzzleDiscipline, "combatMuzzleDiscipline", true);
            Scribe_Values.Look(ref combatSmartCover, "combatSmartCover", true);
            Scribe_Values.Look(ref combatRangedKiting, "combatRangedKiting", true);
            Scribe_Values.Look(ref allowDraftedKitingAssist, "allowDraftedKitingAssist", true);
            Scribe_Values.Look(ref combatExplosiveSafety, "combatExplosiveSafety", true);
            Scribe_Values.Look(ref allowDraftedForceFire, "allowDraftedForceFire", true);
            Scribe_Values.Look(ref smartCastPositionPenalties, "smartCastPositionPenalties", true);
            Scribe_Values.Look(ref combatNearMissReaction, "combatNearMissReaction", true);
            Scribe_Values.Look(ref coverSearchRadius, "coverSearchRadius", 6f);
            Scribe_Values.Look(ref smartTaskDelegation, "smartTaskDelegation", true);
            Scribe_Values.Look(ref localVisualCourtesy, "localVisualCourtesy", true);
            Scribe_Values.Look(ref roomCentricWorkFocus, "roomCentricWorkFocus", true);
            Scribe_Values.Look(ref antiSwarmingRedundancy, "antiSwarmingRedundancy", true);
            Scribe_Values.Look(ref commonSenseSanitation, "commonSenseSanitation", true);
            Scribe_Values.Look(ref commonSenseFifoFood, "commonSenseFifoFood", true);
            Scribe_Values.Look(ref commonSenseHazardJobs, "commonSenseHazardJobs", true);
            Scribe_Values.Look(ref superheatedFirefightingSafety, "superheatedFirefightingSafety", true);
            Scribe_Values.Look(ref environmentalHazardProtection, "environmentalHazardProtection", true);
            Scribe_Values.Look(ref preTripNeedsPreparation, "preTripNeedsPreparation", true);
            Scribe_Values.Look(ref opportunisticHaulingOnRoute, "opportunisticHaulingOnRoute", true);
            Scribe_Values.Look(ref comfortSeating, "comfortSeating", true);
            Scribe_Values.Look(ref jobSuppressionDurationSeconds, "jobSuppressionDurationSeconds", 25);
            Scribe_Values.Look(ref hazardSensitivity, "hazardSensitivity", 25f);

            Scribe_Values.Look(ref buddyEmpathyAssistance, "buddyEmpathyAssistance", true);
            Scribe_Values.Look(ref selfPreservationMedicalRest, "selfPreservationMedicalRest", true);
            Scribe_Values.Look(ref emergencyBleedOutInterruption, "emergencyBleedOutInterruption", true);
            Scribe_Values.Look(ref predatorSurvivalAlert, "predatorSurvivalAlert", true);
            Scribe_Values.Look(ref sleepingCourtesy, "sleepingCourtesy", true);
            Scribe_Values.Look(ref crisisHorizonTicks, "crisisHorizonTicks", 3600);

            Scribe_Values.Look(ref structureDoorDiscipline, "structureDoorDiscipline", true);
            Scribe_Values.Look(ref structureAntiTrapping, "structureAntiTrapping", true);
            Scribe_Values.Look(ref acousticRadiusMultiplier, "acousticRadiusMultiplier", 1f);

            Scribe_Values.Look(ref moduleMindEnabled, "moduleMindEnabled", true);
            Scribe_Values.Look(ref mindTantrumSparesExplosives, "mindTantrumSparesExplosives", true);
            Scribe_Collections.Look(ref disabledMentalBreaks, "disabledMentalBreaks", LookMode.Value);
            if (disabledMentalBreaks == null) disabledMentalBreaks = new HashSet<string>();

            Scribe_Values.Look(ref observeOnly, "observeOnly", false);
            Scribe_Values.Look(ref debugLogging, "debugLogging", false);

            Scribe_Values.Look(ref advThreatExpiryTicks, "advThreatExpiryTicks", DefaultThreatExpiryTicks);
            Scribe_Values.Look(ref advThreatMinReach, "advThreatMinReach", DefaultThreatMinReach);
            Scribe_Values.Look(ref advThreatMaxReach, "advThreatMaxReach", DefaultThreatMaxReach);
            Scribe_Values.Look(ref advRegionCacheTicks, "advRegionCacheTicks", DefaultRegionCacheTicks);
            Scribe_Values.Look(ref advThreatWaitTicks, "advThreatWaitTicks", DefaultThreatWaitTicks);
            Scribe_Values.Look(ref advHeavyEvaluationsPerTick, "advHeavyEvaluationsPerTick", DefaultHeavyEvaluationsPerTick);
            Scribe_Values.Look(ref devShowThreatOverlay, "devShowThreatOverlay", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) ClampAdvanced();
        }

        private static List<MentalBreakDef> ListedBreaks()
        {
            if (listedBreaks != null) return listedBreaks;
            listedBreaks = new List<MentalBreakDef>();
            List<MentalBreakDef> all = DefDatabase<MentalBreakDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
                if (all[i].intensity == MentalBreakIntensity.Major || all[i].intensity == MentalBreakIntensity.Extreme)
                    listedBreaks.Add(all[i]);
            listedBreaks.Sort((a, b) =>
            {
                int byIntensity = b.intensity.CompareTo(a.intensity);
                return byIntensity != 0 ? byIntensity : string.Compare(a.LabelCap.ToString(), b.LabelCap.ToString(), System.StringComparison.OrdinalIgnoreCase);
            });
            return listedBreaks;
        }

        private void DoMentalBreakList(Listing_Standard list)
        {
            if (disabledMentalBreaks == null) disabledMentalBreaks = new HashSet<string>();
            List<MentalBreakDef> breaks = ListedBreaks();
            MentalBreakIntensity? current = null;
            for (int i = 0; i < breaks.Count; i++)
            {
                MentalBreakDef def = breaks[i];
                if (current != def.intensity)
                {
                    current = def.intensity;
                    list.Gap(4f);
                    GUI.color = new Color(0.85f, 0.85f, 0.85f);
                    list.Label(def.intensity == MentalBreakIntensity.Extreme ? "Extreme" : "Major");
                    GUI.color = Color.white;
                }

                string label = def.LabelCap.ToString();
                ModContentPack pack = def.modContentPack;
                if (pack != null && !pack.IsCoreMod) label += " (" + pack.Name + ")";

                bool allowed = !disabledMentalBreaks.Contains(def.defName);
                bool before = allowed;
                list.CheckboxLabeled(label, ref allowed, "Allowed: the colonists can get this break (vanilla). " + def.defName);
                if (allowed != before)
                {
                    if (allowed) disabledMentalBreaks.Remove(def.defName);
                    else disabledMentalBreaks.Add(def.defName);
                }
            }
        }

        private void DoAdvancedSection(Listing_Standard list)
        {
            list.GapLine();
            Text.Font = GameFont.Medium;
            list.Label("Advanced (Dev mode)");
            Text.Font = GameFont.Small;
            GUI.color = new Color(1f, 0.8f, 0.5f);
            list.Label("For testing and tuning. The defaults are the tested values; other values can make pawns flee too much or too little, or cost more performance.");
            GUI.color = Color.white;

            list.CheckboxLabeled("Show threat overlay on the map", ref devShowThreatOverlay,
                "Draws each registered shooter's reach as a ring (red: active, gray: no longer a threat). Dev mode only.");

            list.Label($"Threat memory: {advThreatExpiryTicks} ticks ({advThreatExpiryTicks / 60f:F1}s)");
            advThreatExpiryTicks = (int)list.Slider(advThreatExpiryTicks, 120f, 1800f);
            list.Label($"Shooter reach, minimum: {advThreatMinReach:F0} cells");
            advThreatMinReach = Mathf.Round(list.Slider(advThreatMinReach, 4f, 20f));
            list.Label($"Shooter reach, maximum: {advThreatMaxReach:F0} cells");
            advThreatMaxReach = Mathf.Round(list.Slider(advThreatMaxReach, 20f, 60f));
            list.Label($"Region danger cache: {advRegionCacheTicks} ticks");
            advRegionCacheTicks = (int)list.Slider(advRegionCacheTicks, 15f, 250f);
            list.Label($"Wait instead of routine work in reach: {advThreatWaitTicks} ticks");
            advThreatWaitTicks = (int)list.Slider(advThreatWaitTicks, 30f, 120f);
            list.Label($"Heavy evaluations per tick: {advHeavyEvaluationsPerTick}");
            advHeavyEvaluationsPerTick = (int)list.Slider(advHeavyEvaluationsPerTick, 1f, 10f);
            ClampAdvanced();

            if (list.ButtonText("Reset advanced values to defaults"))
                ResetAdvanced();
        }

        public void DoWindowContents(Rect inRect)
        {
            Rect outRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 24f, contentHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            var list = new Listing_Standard();
            list.Begin(viewRect);

            Text.Font = GameFont.Medium;
            list.Label("Rim Smart Framework");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            list.Label("A lightweight framework for pawn common sense, survival, and combat discipline.");
            GUI.color = Color.white;
            list.Gap(4f);

            Rect supportBox = list.GetRect(40f);
            Widgets.DrawMenuSection(supportBox);
            Rect btnKofi = supportBox.ContractedBy(6f);
            if (Widgets.ButtonText(btnKofi, "Support on Ko-fi"))
            {
                Application.OpenURL("https://ko-fi.com/tawitsy");
            }
            TooltipHandler.TipRegion(btnKofi, "Open Ko-fi to support the author (https://ko-fi.com/tawitsy)");
            list.GapLine();

            Text.Font = GameFont.Medium;
            list.Label("Modules (restart required)");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            list.Label("A disabled module patches nothing. Changes take effect after restarting the game.");
            GUI.color = Color.white;
            list.CheckboxLabeled("Combat module", ref moduleCombatEnabled,
                "Firing discipline, muzzle discipline, cover, kiting, explosive safety, near-miss reactions. Requires a restart.");
            list.CheckboxLabeled("Survival module", ref moduleSurvivalEnabled,
                "Early rescue, bleed-out triage, superheated fire safety. Requires a restart.");
            list.CheckboxLabeled("Work module", ref moduleWorkEnabled,
                "Work delegation, hazard refusal, food and seating, opportunistic hauling, sanitation. Requires a restart.");
            list.CheckboxLabeled("Structure module", ref moduleStructureEnabled,
                "Door discipline, builder wall safety, sleeping courtesy when wandering. Requires a restart.");
            list.CheckboxLabeled("Mind module", ref moduleMindEnabled,
                "Tantrums spare explosives, per-break disable list. Requires a restart.");
            list.GapLine();

            Text.Font = GameFont.Medium;
            list.Label("Combat & Targeting");
            Text.Font = GameFont.Small;
            list.CheckboxLabeled("Avoid friendly fire", ref combatFiringDiscipline,
                "Pawns hold fire or sidestep if an ally is directly blocking their line of sight.");
            list.CheckboxLabeled("Tactical fallback for ranged pawns", ref combatRangedKiting,
                "Ranged pawns step back 2-4 tiles when rushed by melee enemies instead of standing still.");
            list.CheckboxLabeled("Assist drafted pawns with tactical fallback", ref allowDraftedKitingAssist,
                "Allow drafted ranged pawns to make micro-repositions away from melee enemies if not actively commanded to hold ground.");
            list.CheckboxLabeled("Explosive safety (Allies & Self)", ref combatExplosiveSafety,
                "Pawns will not fire rockets, grenades, or mortars if allies, themselves, or high-value base structures are caught in the blast radius.");
            list.CheckboxLabeled("Smart shooting positions", ref smartCastPositionPenalties,
                "Pawns avoid standing in active friendly fire lanes or in open enemy sniper sights.");
            list.CheckboxLabeled("React to near-misses", ref combatNearMissReaction,
                "Pawns caught in the open seek nearby cover when shot at.");
            list.CheckboxLabeled("Allow forced fire when drafted", ref allowDraftedForceFire,
                "When you manually order a drafted pawn to attack a target, they will fire even if an ally is near the line.");
            list.CheckboxLabeled("Muzzle discipline", ref combatMuzzleDiscipline,
                "Moving pawns route behind shooters rather than crossing directly in front of aiming guns.");
            list.CheckboxLabeled("Smart cover response", ref combatSmartCover,
                "Pawns seek directional cover or retreat toward base when under threat.");
            list.Label($"Cover search radius: {coverSearchRadius:F0} tiles");
            coverSearchRadius = list.Slider(coverSearchRadius, 3f, 12f);
            list.GapLine();

            Text.Font = GameFont.Medium;
            list.Label("Work & Common Sense (Smart Delegation)");
            Text.Font = GameFont.Small;
            list.CheckboxLabeled("Smart task delegation", ref smartTaskDelegation,
                "Collaborative coordination prevents distant colonists from running across the map to snatch jobs right in front of local colonists.");
            list.CheckboxLabeled("Human sight & proximity priority", ref localVisualCourtesy,
                "Colonists who can see and stand next to an item/task get priority; distant colonists yield to them (local visual precedence).");
            list.CheckboxLabeled("Room-centric work continuity", ref roomCentricWorkFocus,
                "Colonists prioritize finishing tasks in their current room/building before hiking outside.");
            list.CheckboxLabeled("Anti-swarming task discipline", ref antiSwarmingRedundancy,
                "Prevents multiple distant colonists from all rushing towards the exact same small task.");
            list.CheckboxLabeled("Clean work areas first", ref commonSenseSanitation,
                "Before cooking in a kitchen or treating in a hospital, pawns spend a short cleaning pass (a few seconds, based on cleaning speed) on filth next to the bench or bed (skipped when Common Sense already does this).");
            list.CheckboxLabeled("Avoid dangerous work", ref commonSenseHazardJobs,
                "During a fight or toxic fallout, pawns do not pick non-emergency chores (hauling, cleaning, repairs, construction) located in dangerous spots.");
            list.CheckboxLabeled("Superheated room fire safety", ref superheatedFirefightingSafety,
                "Pawns refuse to enter enclosed rooms with extreme lethal temperatures (>60 C) to fight fires with bare hands.");
            list.CheckboxLabeled("Weather & hazard protection", ref environmentalHazardProtection,
                "Refuse non-essential outdoor work during toxic fallout, blizzards, or heavy downpours.");
            list.CheckboxLabeled("Eat spoiling food first (FIFO)", ref commonSenseFifoFood,
                "Pawns prefer meals that are closer to rotting instead of taking fresh ones.");
            list.CheckboxLabeled("Comfort seating", ref comfortSeating,
                "Pawns prefer sitting on nearby chairs or benches to eat rather than standing in the dirt.");
            list.CheckboxLabeled("Pre-trip preparation", ref preTripNeedsPreparation,
                "Pawns grab food or rest before embarking on distant jobs across the map.");
            list.CheckboxLabeled("Opportunistic hauling", ref opportunisticHaulingOnRoute,
                "Pawns pick up nearby items that are on the way to their destination.");
            list.Label($"Hazard job retry delay: {jobSuppressionDurationSeconds}s");
            jobSuppressionDurationSeconds = (int)list.Slider(jobSuppressionDurationSeconds, 5f, 90f);
            list.Label($"Hazard sensitivity: {hazardSensitivity:F0} / 100");
            hazardSensitivity = list.Slider(hazardSensitivity, 10f, 60f);
            list.GapLine();

            Text.Font = GameFont.Medium;
            list.Label("Colonist Care & Privacy");
            Text.Font = GameFont.Small;
            list.CheckboxLabeled("Early rescue", ref buddyEmpathyAssistance,
                "Allies and doctors will help severely injured or bleeding colonists to bed before they collapse in the dirt.");
            list.CheckboxLabeled("Emergency medical rest", ref selfPreservationMedicalRest,
                "Bleeding pawns prioritize rushing to a hospital bed over routine tasks.");
            list.CheckboxLabeled("Immediate bleed-out triage", ref emergencyBleedOutInterruption,
                "Immediately interrupts ongoing chores and sends bleeding colonists to hospital bed or self-tending when close to collapse.");
            list.CheckboxLabeled("Predator alert", ref predatorSurvivalAlert,
                "Colonists sound an alarm and retreat when stalked by wild predators instead of continuing chores oblivious.");
            list.CheckboxLabeled("Respect sleeping colonists", ref sleepingCourtesy,
                "Pawns avoid taking shortcuts through private bedrooms while the occupant is asleep.");
            list.Label($"Blood loss crisis threshold: {crisisHorizonTicks / 2500f:F1} hours before collapse");
            crisisHorizonTicks = (int)list.Slider(crisisHorizonTicks, 1200f, 7500f);
            list.GapLine();

            Text.Font = GameFont.Medium;
            list.Label("Base & Structures");
            Text.Font = GameFont.Small;
            list.CheckboxLabeled("Door discipline", ref structureDoorDiscipline,
                "Prevents dropping or storing items on door tiles and keeps idle pawns from loitering in doorways.");
            list.CheckboxLabeled("Builder wall safety", ref structureAntiTrapping,
                "Builders stand on the open side when constructing impassable walls to avoid trapping themselves.");
            list.Label($"Sound detection range: {acousticRadiusMultiplier:F2}x");
            acousticRadiusMultiplier = list.Slider(acousticRadiusMultiplier, 0.25f, 3f);
            list.GapLine();

            Text.Font = GameFont.Medium;
            list.Label("Mind & Mental Breaks");
            Text.Font = GameFont.Small;
            list.CheckboxLabeled("Tantrums spare explosives", ref mindTantrumSparesExplosives,
                "Colonists in a tantrum do not smash explosives (shells, chemfuel, IEDs) or shelves that hold them.");
            list.Gap(4f);
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            list.Label("Uncheck a break to stop your colonists from getting it. The game then picks another break of the same strength, or a milder one if none is left. Applies to your colonists only.");
            GUI.color = Color.white;
            DoMentalBreakList(list);
            list.GapLine();

            Text.Font = GameFont.Medium;
            list.Label("General & Diagnostics");
            Text.Font = GameFont.Small;
            list.CheckboxLabeled("Observe-only mode", ref observeOnly,
                "AI evaluates decisions and logs them without modifying pawn jobs. Good for testing.");
            list.CheckboxLabeled("Debug logging", ref debugLogging,
                "Prints detailed decision logs to the Dev Console and disk.");

            if (Prefs.DevMode)
                DoAdvancedSection(list);

            contentHeight = list.CurHeight + 12f;
            list.End();
            Widgets.EndScrollView();
        }
    }
}
