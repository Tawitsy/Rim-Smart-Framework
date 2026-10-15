using Verse;

namespace SmartAIFramework.Core.Knowledge
{
    public interface IReadOnlyTacticalGrid
    {
        int FiringLaneAt(IntVec3 c);
        int ThreatAt(IntVec3 c);
        int HazardAt(IntVec3 c);

        float DangerAt(IntVec3 c);
    }

    public interface ITacticalGridWriter
    {
        void RegisterFiringLane(Thing actor, IntVec3 from, IntVec3 to, float aoeRadius, bool hostileToPlayer);
        void RegisterHazard(Thing source, IntVec3 center, float radius);
        void ClearActor(Thing actor);
    }
}
