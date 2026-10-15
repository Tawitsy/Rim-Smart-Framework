using HarmonyLib;
using UnityEngine;
using Verse;

namespace SmartAIFramework
{
    public class SmartAIMod : Mod
    {
        public const string HarmonyId = "Tawitsy.RimSmartFramework";

        public static SmartAISettings Settings { get; private set; }

        public SmartAIMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SmartAISettings>();

            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(typeof(SmartAIMod).Assembly);

            SmartAIFramework.Core.SmartAILog.Initialize(content.RootDir);
            SmartAIFramework.Patches.VehicleFrameworkCompatPatch.TryApply(harmony);
            SmartAIFramework.Patches.CombatExtendedCompatPatch.TryApply(harmony);

            var patchedMethods = harmony.GetPatchedMethods();
            int patchCount = 0;
            foreach (var _ in patchedMethods) patchCount++;

            SmartAIFramework.Core.SmartAILog.Log("RimSmartFramework", $"Rim Smart Framework initialized successfully with {patchCount} patches.");
        }

        public override string SettingsCategory() => "Rim Smart Framework";

        public override void DoSettingsWindowContents(Rect inRect) => Settings.DoWindowContents(inRect);
    }
}
