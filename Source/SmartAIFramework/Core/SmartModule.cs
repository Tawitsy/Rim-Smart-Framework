namespace SmartAIFramework
{
    public abstract class SmartModule
    {
        public abstract string Id { get; }
        public abstract string Label { get; }
        public abstract bool EnabledInSettings { get; }

        public bool Active { get; internal set; }

        public virtual void Register() { }
    }

    public static class ModuleFlags
    {
        public static bool Combat { get; internal set; }
        public static bool Survival { get; internal set; }
        public static bool Work { get; internal set; }
        public static bool Structure { get; internal set; }
        public static bool Mind { get; internal set; }
    }
}
