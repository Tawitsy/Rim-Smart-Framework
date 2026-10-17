namespace SmartAIFramework.Modules
{
    public sealed class WorkModule : SmartModule
    {
        public override string Id => "Work";
        public override string Label => "Work";
        public override bool EnabledInSettings => SmartAIMod.Settings.moduleWorkEnabled;
    }
}
