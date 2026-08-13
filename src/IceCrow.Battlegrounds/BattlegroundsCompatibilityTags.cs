namespace IceCrow.Battlegrounds;

internal static class BattlegroundsCompatibilityTags
{
    // HDT TagChangeActions.cs at revision d73b3a8220bbc88e836af8cd67a15c44a1fb7021,
    // inspected 2026-08-13, observes 2022 changing from 1 to 0 as the solo
    // Battlegrounds setup-to-combat transition.
    public const int Setup = 2022;

    // HDT TagChangeActions.cs at revision d73b3a8220bbc88e836af8cd67a15c44a1fb7021,
    // inspected 2026-08-13, observes 3533 changing from 1 to 0 as the combat
    // setup-to-combat transition used by the Duos path.
    public const int CombatSetup = 3533;
}
