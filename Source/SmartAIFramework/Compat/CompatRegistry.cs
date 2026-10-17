using System.Collections.Generic;
using System.Text;
using Verse;

namespace SmartAIFramework.Compat
{
    public enum CompatMode
    {
        TestedAlongside,
        Adapter,
        Yield
    }

    public sealed class CompatProfile
    {
        public string name;
        public string[] packageIds;
        public CompatMode mode;
        public string note;
        public string keepOwnSetting;
    }

    public static class CompatRegistry
    {
        public const string KeepOwnOnRouteHauling = "compatKeepOwnOnRouteHauling";
        public const string DubsBadHygieneId = "dubwise.dubsbadhygiene";

        private static CompatProfile P(string name, CompatMode mode, string note, params string[] packageIds)
        {
            return new CompatProfile { name = name, packageIds = packageIds, mode = mode, note = note };
        }

        private static CompatProfile Y(string name, string note, string keepOwnSetting, params string[] packageIds)
        {
            return new CompatProfile { name = name, packageIds = packageIds, mode = CompatMode.Yield, note = note, keepOwnSetting = keepOwnSetting };
        }

        public static readonly CompatProfile[] Known =
        {
            Y("While You're Up", "Rim Smart Framework's on-route hauling is off; While You're Up does it.", KeepOwnOnRouteHauling,
                "zsbk.patch16.whileyoureup", "hoodie.whileyoureup", "codeoptimist.jobsofopportunity"),
            P("Common Sense", CompatMode.Adapter, "Clean-before-work is left to Common Sense when it is enabled there.", "avilmask.commonsense"),
            P("Combat Extended", CompatMode.Adapter, "Friendly-fire checks use CE's shot line.", "ceteam.combatextended"),
            P("Perspective Shift", CompatMode.Adapter, "The pawn you control directly is never steered.", "ferny.perspectiveshift"),
            P("Dubs Bad Hygiene", CompatMode.Adapter, "A desperate bladder or thirst lets a pawn leave shelter like hunger does.", DubsBadHygieneId),
            P("CAI 5000", CompatMode.TestedAlongside, "Tested together; both run.", "krkr.rule56"),
            P("Yayo's Combat 3", CompatMode.TestedAlongside, "", "mlie.yayoscombat3"),
            P("Simple Sidearms", CompatMode.TestedAlongside, "", "petetimessix.simplesidearms"),
            P("Achtung!", CompatMode.TestedAlongside, "Forced orders are always respected.", "brrainz.achtung"),
            P("Smart Medicine", CompatMode.TestedAlongside, "", "uuugggg.smartmedicine", "memegoddess.smartmedicine"),
            P("Hospitality", CompatMode.TestedAlongside, "Early rescue only carries colonists.", "orion.hospitality"),
            P("Pick Up And Haul", CompatMode.TestedAlongside, "", "mehni.pickupandhaul"),
            P("Search and Destroy", CompatMode.TestedAlongside, "", "memegoddess.searchanddestroy"),
            P("Defensive Positions", CompatMode.TestedAlongside, "", "unlimitedhugs.defensivepositions"),
            P("RunAndGun", CompatMode.TestedAlongside, "", "roolo.runandgun", "memegoddess.runandgun"),
            P("Vanilla Expanded Framework", CompatMode.TestedAlongside, "", "oskarpotocki.vanillafactionsexpanded.core"),
            P("Vehicle Framework", CompatMode.TestedAlongside, "", "smashphil.vehicleframework"),
            P("Humanoid Alien Races", CompatMode.TestedAlongside, "", "erdelf.humanoidalienraces"),
            P("Work Tab", CompatMode.TestedAlongside, "", "fluffy.worktab"),
            P("Allow Tool", CompatMode.TestedAlongside, "", "unlimitedhugs.allowtool"),
            P("Replace Stuff", CompatMode.TestedAlongside, "", "memegoddess.replacestuff"),
            P("RimHUD", CompatMode.TestedAlongside, "", "jaxe.rimhud"),
            P("Numbers", CompatMode.TestedAlongside, "", "mehni.numbers"),
            P("Better Pawn Control", CompatMode.TestedAlongside, "", "voult.betterpawncontrol"),
            P("Colony Groups", CompatMode.TestedAlongside, "", "derekbickley.ltocolonygroupsfinal"),
            P("Better Workbench Management", CompatMode.TestedAlongside, "", "falconne.bwm"),
        };

        public static readonly List<CompatProfile> Detected = new List<CompatProfile>();

        public static bool YieldOpportunisticHauling { get; private set; }
        public static bool DubsBadHygieneActive { get; private set; }

        private static string Normalize(string id)
        {
            string s = id.ToLowerInvariant();
            if (s.EndsWith("_steam")) s = s.Substring(0, s.Length - 6);
            else if (s.EndsWith("_copy")) s = s.Substring(0, s.Length - 5);
            return s;
        }

        public static void Detect()
        {
            Detected.Clear();
            YieldOpportunisticHauling = false;
            DubsBadHygieneActive = false;

            var active = new HashSet<string>();
            foreach (ModMetaData mod in ModsConfig.ActiveModsInLoadOrder)
                active.Add(Normalize(mod.PackageIdNonUnique));

            bool keepOwn = SmartAIMod.Settings != null && SmartAIMod.Settings.compatKeepOwnOnRouteHauling;
            for (int i = 0; i < Known.Length; i++)
            {
                CompatProfile profile = Known[i];
                bool found = false;
                for (int j = 0; j < profile.packageIds.Length; j++)
                {
                    if (active.Contains(profile.packageIds[j]))
                    {
                        found = true;
                        if (profile.packageIds[j] == DubsBadHygieneId) DubsBadHygieneActive = true;
                    }
                }
                if (!found) continue;

                Detected.Add(profile);
                if (profile.mode == CompatMode.Yield && profile.keepOwnSetting == KeepOwnOnRouteHauling && !keepOwn)
                    YieldOpportunisticHauling = true;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < Detected.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Detected[i].name).Append(" (").Append(Detected[i].mode).Append(')');
            }
            Log.Message("[Rim Smart Framework] Compatibility: " + Detected.Count + " known mods detected" + (Detected.Count > 0 ? ": " + sb : "."));
        }

        public static string ModeLabel(CompatMode mode)
        {
            switch (mode)
            {
                case CompatMode.Yield: return "Yield: handed over";
                case CompatMode.Adapter: return "Adapter: connected";
                default: return "Tested alongside";
            }
        }
    }
}
