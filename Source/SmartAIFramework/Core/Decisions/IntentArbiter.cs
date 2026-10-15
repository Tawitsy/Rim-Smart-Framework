using System.Collections.Generic;
using SmartAIFramework.Core.Knowledge;
using Verse;
using Verse.AI;

namespace SmartAIFramework.Core.Decisions
{
    public static class IntentArbiter
    {
        private static readonly List<Pawn> interruptQueue = new List<Pawn>();
        private static bool flushing;

        public static void Propose(Intent intent)
        {
            if (intent?.pawn == null || intent.jobDef == null) return;
            Pawn pawn = intent.pawn;

            if (!ActionGate.MayActAutonomously(pawn))
            {
                DecisionLog.Record(pawn, "Would propose " + intent, applied: false);
                return;
            }

            IPawnMemoryWriter w = KnowledgeBase.Writer(pawn);
            if (w == null) return;

            Intent current = w.PendingIntent;
            if (current != null && !current.Expired && current.utility >= intent.utility) return;

            w.PendingIntent = intent;
            if (intent.urgent && !interruptQueue.Contains(pawn)) interruptQueue.Add(pawn);
        }

        public static void FlushInterrupts()
        {
            if (interruptQueue.Count == 0 || flushing) return;
            flushing = true;
            try
            {
                for (int i = interruptQueue.Count - 1; i >= 0; i--)
                {
                    Pawn p = interruptQueue[i];
                    if (p == null || !p.Spawned) { interruptQueue.RemoveAt(i); continue; }
                    if (p.stances?.stunner?.Stunned == true) continue;
                    if (p.stances?.curStance is Stance_Busy busy && !(busy is Stance_Cooldown)) continue;

                    interruptQueue.RemoveAt(i);

                    IPawnMemoryWriter w = KnowledgeBase.Writer(p);
                    Intent intent = w?.PendingIntent;
                    if (intent == null || intent.Expired) continue;
                    if (!ActionGate.MayActAutonomously(p)) continue;

                    Job cur = p.jobs?.curJob;
                    if (cur == null || cur.playerForced) continue;
                    if (cur.def == intent.jobDef && cur.targetA == intent.target) continue;

                    p.stances?.CancelBusyStanceHard();
                    p.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
                }
            }
            finally { flushing = false; }
        }

        internal static bool TryConsume(Pawn pawn, Job vanillaChoice, out Job job)
        {
            job = null;
            PawnMemory mem = KnowledgeBase.PeekMemory(pawn);
            if (mem == null) return false;

            IPawnMemoryWriter w = mem;
            Intent intent = w.PendingIntent;
            if (intent == null) return false;

            if (intent.Expired || !ActionGate.MayActAutonomously(pawn))
            {
                w.PendingIntent = null;
                return false;
            }
            if (vanillaChoice != null && vanillaChoice.playerForced) return false;

            w.PendingIntent = null;
            job = intent.MakeJob();
            DecisionLog.Record(pawn, "Executed " + intent, applied: true);
            return true;
        }
    }
}
