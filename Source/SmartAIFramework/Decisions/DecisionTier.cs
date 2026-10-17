namespace SmartAIFramework.Decisions
{
    // Lower number wins. A proposal can only displace a vanilla choice of a strictly lower rank.
    public enum DecisionTier
    {
        PlayerOrder = 0,
        VanillaEmergency = 1,
        Survival = 2,
        VanillaNeed = 3,
        Tactical = 4,
        Routine = 5
    }

    public enum Actuation
    {
        AtNextDecision,
        Interrupt
    }
}
