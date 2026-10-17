using SmartAIFramework.Work;

namespace SmartAIFramework.Modules
{
    public sealed class SurvivalModule : SmartModule
    {
        public override string Id => "Survival";
        public override string Label => "Survival";
        public override bool EnabledInSettings => SmartAIMod.Settings.moduleSurvivalEnabled;

        public override void Register()
        {
            SelfTriageBehavior.Register();
        }
    }
}
