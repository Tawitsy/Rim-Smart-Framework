using SmartAIFramework.Patches;

namespace SmartAIFramework.Modules
{
    public sealed class CombatModule : SmartModule
    {
        public override string Id => "Combat";
        public override string Label => "Combat";
        public override bool EnabledInSettings => SmartAIMod.Settings.moduleCombatEnabled;

        public override void Register()
        {
            Compat_CombatExtended.TryApply(SmartAIMod.Harmony);
        }
    }
}
