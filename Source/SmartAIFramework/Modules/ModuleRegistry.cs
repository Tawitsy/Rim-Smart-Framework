using System.Reflection;
using HarmonyLib;
using SmartAIFramework.Data;

namespace SmartAIFramework.Modules
{
    public static class ModuleRegistry
    {
        private static readonly SmartModule[] modules =
        {
            new CombatModule(),
            new SurvivalModule(),
            new WorkModule(),
            new StructureModule()
        };

        public static SmartModule[] All => modules;

        public static void Apply(Harmony harmony, Assembly assembly)
        {
            for (int i = 0; i < modules.Length; i++)
                modules[i].Active = modules[i].EnabledInSettings;

            // Flags first, so a patch that fires during startup already sees the final state.
            ModuleFlags.Combat = modules[0].Active;
            ModuleFlags.Survival = modules[1].Active;
            ModuleFlags.Work = modules[2].Active;
            ModuleFlags.Structure = modules[3].Active;

            PatchClasses(harmony, assembly, null);

            for (int i = 0; i < modules.Length; i++)
            {
                SmartModule module = modules[i];
                if (!module.Active)
                {
                    SmartLog.Message("Startup", "Module " + module.Id + " is disabled: no patches applied.");
                    continue;
                }

                PatchClasses(harmony, assembly, module.Id);
                module.Register();
            }
        }

        // One patch class at a time, so a game update that renames or reshapes one target method (1.7, other
        // mods' transpilers) switches off only that feature instead of the whole module or the whole mod.
        private static void PatchClasses(Harmony harmony, Assembly assembly, string category)
        {
            foreach (System.Type type in AccessTools.GetTypesFromAssembly(assembly))
            {
                PatchClassProcessor processor = harmony.CreateClassProcessor(type);
                string own = processor.Category;
                if (category == null ? !string.IsNullOrEmpty(own) : own != category)
                    continue;

                try
                {
                    processor.Patch();
                }
                catch (System.Exception e)
                {
                    Verse.Log.Warning("[Rim Smart Framework] " + type.Name + " (" + (category ?? "Core") +
                                      ") could not be applied, so that feature is off: " + (e.InnerException ?? e).Message);
                }
            }
        }
    }
}
