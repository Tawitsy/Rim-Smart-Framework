using UnityEngine;
using Verse;

namespace SmartAIFramework
{
    public class SmartAISettings : ModSettings
    {
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

        public bool observeOnly = false;
        public bool debugLogging = false;

        public string customDonationUrl = "https://ko-fi.com/tawitsy";
        private static Vector2 scrollPosition = Vector2.zero;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref combatFiringDiscipline, "combatFiringDiscipline", true);
            Scribe_Values.Look(ref combatMuzzleDiscipline, "combatMuzzleDiscipline", true);
            Scribe_Values.Look(ref combatSmartCover, "combatSmartCover", true);
            Scribe_Values.Look(ref combatRangedKiting, "combatRangedKiting", true);
            Scribe_Values.Look(ref allowDraftedKitingAssist, "allowDraftedKitingAssist", true);
            Scribe_Values.Look(ref combatExplosiveSafety, "combatExplosiveSafety", true);
            Scribe_Values.Look(ref allowDraftedForceFire, "allowDraftedForceFire", true);
            Scribe_Values.Look(ref smartCastPositionPenalties, "smartCastPositionPenalties", true);
            Scribe_Values.Look(ref combatNearMissReaction, "combatNearMissReaction", true);
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

            Scribe_Values.Look(ref observeOnly, "observeOnly", false);
            Scribe_Values.Look(ref debugLogging, "debugLogging", false);
            Scribe_Values.Look(ref customDonationUrl, "customDonationUrl", "https://ko-fi.com/tawitsy");
        }

        public void DoWindowContents(Rect inRect)
        {
            Rect outRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 24f, 1540f);

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

            Rect supportBox = list.GetRect(68f);
            Widgets.DrawMenuSection(supportBox);
            Rect innerBox = supportBox.ContractedBy(6f);

            Rect titleRect = new Rect(innerBox.x, innerBox.y, innerBox.width, 20f);
            Widgets.Label(titleRect, "Support the Author:");

            float buttonWidth = (innerBox.width - 16f) / 3f;
            Rect btnKofi = new Rect(innerBox.x, innerBox.y + 22f, buttonWidth, 28f);
            Rect btnPatreon = new Rect(innerBox.x + buttonWidth + 8f, innerBox.y + 22f, buttonWidth, 28f);
            Rect btnCustom = new Rect(innerBox.x + (buttonWidth + 8f) * 2, innerBox.y + 22f, buttonWidth, 28f);

            if (Widgets.ButtonText(btnKofi, "Buy me a Coffee"))
            {
                Application.OpenURL("https://ko-fi.com/tawitsy");
            }
            TooltipHandler.TipRegion(btnKofi, "Open Ko-fi to support the author (https://ko-fi.com/tawitsy)");

            if (Widgets.ButtonText(btnPatreon, "Patreon"))
            {
                Application.OpenURL("https://patreon.com");
            }
            TooltipHandler.TipRegion(btnPatreon, "Open Patreon for monthly support and updates.");

            string customBtnLabel = string.IsNullOrEmpty(customDonationUrl) || customDonationUrl.Contains("ko-fi.com/tawitsy") ? "Ko-fi Page" : "Custom Link";
            if (Widgets.ButtonText(btnCustom, customBtnLabel))
            {
                if (!string.IsNullOrEmpty(customDonationUrl))
                {
                    Application.OpenURL(customDonationUrl);
                }
            }
            TooltipHandler.TipRegion(btnCustom, "Opens your custom configured donation or community URL.");

            list.Gap(6f);
            list.Label("Custom donation URL:");
            customDonationUrl = list.TextEntry(customDonationUrl);
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
                "Pawns do a quick sweep around cooking tables or medical beds before cooking or operating.");
            list.CheckboxLabeled("Avoid dangerous work", ref commonSenseHazardJobs,
                "Pawns refuse non-emergency chores (hauling chunks, cleaning outside) in active hazard or combat zones.");
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
            list.Label("General & Diagnostics");
            Text.Font = GameFont.Small;
            list.CheckboxLabeled("Observe-only mode", ref observeOnly,
                "AI evaluates decisions and logs them without modifying pawn jobs. Good for testing.");
            list.CheckboxLabeled("Debug logging", ref debugLogging,
                "Prints detailed decision logs to the Dev Console and disk.");

            list.End();
            Widgets.EndScrollView();
        }
    }
}
