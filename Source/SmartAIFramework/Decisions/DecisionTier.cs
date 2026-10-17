namespace SmartAIFramework.Decisions
{
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
