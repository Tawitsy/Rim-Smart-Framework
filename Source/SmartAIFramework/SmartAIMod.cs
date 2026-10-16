using HarmonyLib;
using SmartAIFramework.Data;
using SmartAIFramework.Patches;
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
            SmartLog.Initialize(content.RootDir);

            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(typeof(SmartAIMod).Assembly);
            Compat_CombatExtended.TryApply(harmony);
        }

        public override string SettingsCategory() => "Rim Smart Framework";

        public override void DoSettingsWindowContents(Rect inRect) => Settings.DoWindowContents(inRect);
    }
}
