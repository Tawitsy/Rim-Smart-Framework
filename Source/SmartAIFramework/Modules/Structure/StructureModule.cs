namespace SmartAIFramework.Modules
{
    public sealed class StructureModule : SmartModule
    {
        public override string Id => "Structure";
        public override string Label => "Structure";
        public override bool EnabledInSettings => SmartAIMod.Settings.moduleStructureEnabled;
    }
}
