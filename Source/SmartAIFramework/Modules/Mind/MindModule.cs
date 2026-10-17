namespace SmartAIFramework.Modules
{
    public sealed class MindModule : SmartModule
    {
        public override string Id => "Mind";
        public override string Label => "Mind";
        public override bool EnabledInSettings => SmartAIMod.Settings.moduleMindEnabled;
    }
}
