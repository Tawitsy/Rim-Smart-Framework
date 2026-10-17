using HarmonyLib;
using SmartAIFramework.Compat;
using SmartAIFramework.Data;
using SmartAIFramework.Modules;
using UnityEngine;
using Verse;

namespace SmartAIFramework
{
    public class SmartAIMod : Mod
    {
        public const string HarmonyId = "Tawitsy.RimSmartFramework";

        public static SmartAISettings Settings { get; private set; }
        public static Harmony Harmony { get; private set; }

        public SmartAIMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SmartAISettings>();
            SmartLog.Initialize(content.RootDir);

            CompatRegistry.Detect();

            Harmony = new Harmony(HarmonyId);
            ModuleRegistry.Apply(Harmony, typeof(SmartAIMod).Assembly);

            if (SmartLog.IsEnabled)
            {
                int patched = 0;
                foreach (var _ in Harmony.GetPatchedMethods()) patched++;
                SmartLog.Message("Startup", $"Rim Smart Framework patched {patched} methods.");
            }
        }

        public override string SettingsCategory() => "Rim Smart Framework";

        public override void DoSettingsWindowContents(Rect inRect) => Settings.DoWindowContents(inRect);
    }
}
