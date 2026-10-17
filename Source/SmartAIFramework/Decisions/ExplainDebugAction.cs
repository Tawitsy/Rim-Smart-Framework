using LudeonTK;
using Verse;

namespace SmartAIFramework.Decisions
{
    public static class ExplainDebugAction
    {
        [DebugAction("Rim Smart Framework", "Explain pawn decisions", actionType = DebugActionType.ToolMapForPawns)]
        private static void ExplainPawnDecisions(Pawn pawn)
        {
            string report = DecisionLog.Explain(pawn);
            Log.Message("[RimSmart] " + report);
            Find.WindowStack.Add(new Dialog_MessageBox(report, title: "Rim Smart Framework: " + pawn.LabelShort));
        }
    }
}
