namespace SmartAIFramework
{
    public abstract class SmartModule
    {
        // Also the HarmonyPatchCategory name of every patch class the module owns.
        public abstract string Id { get; }
        public abstract string Label { get; }
        public abstract bool EnabledInSettings { get; }

        // Snapshot of EnabledInSettings taken once at startup; toggling in the UI only matters after a restart.
        public bool Active { get; internal set; }

        public virtual void Register() { }
    }

    // Plain static reads so Core patches can gate a module's work with a single cached bool.
    public static class ModuleFlags
    {
        public static bool Combat { get; internal set; }
        public static bool Survival { get; internal set; }
        public static bool Work { get; internal set; }
        public static bool Structure { get; internal set; }
    }
}
