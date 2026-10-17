# Changelog

## v1.2.1 (unreleased)

### Fixed
- Colonists no longer get stuck believing a spot is safe (or dangerous) after a wall is built or destroyed, or after an enemy stops being a threat: the mod rechecks within a second.
- The mod now tracks where a shooter is standing right now, not where it stood a few steps ago.
- A room is now judged by several points, so a room that is only visible along one side is no longer treated as safe.
- Hauling into storage that sits inside a shooter's range is now caught too, not only picking things up there.
- Colonists no longer keep re-picking the same job inside a shooter's range. They wait a moment and decide again, and the job is no longer blocked for colonists set to Attack.
- The mod tracks up to 128 shooters at once (was 64), which matters in very large raids.
- Colonists who are very hungry or exhausted are now allowed to go eat or sleep in every situation, not just some of them.

### Known limitations
- A shooter's reach is a bounded estimate (8-40 cells, line of sight only), and paths that cross a firing zone are not yet avoided.

## v1.3.0 (unreleased)

### Added
- **Mind module** (can be switched off in Mod Settings, restart required).
- **Tantrums spare explosives.** Colonists in a tantrum no longer smash explosives (shells, chemfuel, IEDs, modded explosives) or shelves and other storage that hold them. Can be turned off.
- **Choose which mental breaks can happen.** Mod Settings lists every Major and Extreme mental break, including ones added by other mods. Uncheck a break and your colonists will not get it: the game picks another break of the same strength, or a milder one if none is left. Applies to your colonists only; everything is allowed by default.

## v1.2.0 (beta) - stability release

This release rebuilds the core of the mod. Most of the work went into making pawns behave reliably, not into new features.

### Changed
- **One decision center.** Every change the mod makes to a pawn's job now goes through a single arbiter with a fixed priority order: player orders > game emergencies > survival > basic needs > tactics > routine work. Features can no longer fight each other or override something more important.
- **Modules.** Combat, Survival, Work and Structure can each be switched off in Mod Settings. A disabled module patches nothing in the game (restart required).
- **Lighter.** No per-tick polling at all. Mod time measured in-game with 110 colonists and a 90-raider raid: about 0.16-0.19 ms per tick during normal work and about 0.19-0.5 ms per tick during the raid (the previous version used about 3.8 ms).
- **Danger has to be real.** Work is only refused for concrete harm: active firing lanes, fire, explosions, or temperatures outside the *safe* range (comfortable range ±10 °C). Underdressed colonies no longer refuse all outdoor work in winter.
- **Danger is shared with the game.** The mod remembers which enemies are aiming at or shooting the colony, how far they reach and what they can see. The game's own decisions now read that: colonists stop picking work, food trips and idle wandering inside an active shooter's reach, the same way they already avoid deadly temperatures.
- **Flee uses the game's own behavior.** Colonists set to *Flee* (the game's default) now flee when a shooter can hit them, even from long range, using vanilla's flee-and-cower. They wait in safety and only go back once the threat is gone. Colonists set to *Attack* get the mod's cover, hold and kite tactics. *Ignore* means no reaction, as the player chose. Set your fighters to *Attack* if you want them to take cover instead of fleeing.
- **Hearing distant gunfire** only makes pawns aware of the fight; it does not move them.
- **Resilient patching.** Each patch is applied on its own. If a game update (for example 1.7) or another mod breaks one, only that feature turns off and the log says which.
- **"Explain pawn decisions"** debug action (Dev mode) shows what the mod decided for a pawn and why. Useful for bug reports.

### Fixed
- Pawns standing frozen after a refused job until an enemy walked up to them.
- Pawns flip-flopping between work and a mod move (sleeping, eating, tending, firefighting, hauling, repairing and wandering against Goto).
- Pawns running back and forth between their work and a retreat during raids: the mod no longer runs a second flee system next to the game's.
- A bleeding pawn that was on fire lying down instead of putting itself out.
- Pawns lying down on the ground again and again when no bed was available during a bleed crisis.
- Work delegation handing a job about a person to that same person.
- "Pawn destination reservation manager failed to clean up properly" and "Reservation manager failed to clean up properly" errors.
- Sanitation before cooking or surgery now takes real time.
- Combat Extended: the sidestep for a blocked shot now triggers (CE rejected the shot line before our check ran).

### Tested
- 32 automated in-game tests covering every bug above, including a test that lets the game choose jobs repeatedly while a shooter is active and checks that no routine job leads into its reach.
- Colony soak: 40 colonists for 3 in-game days with 12 raids, 26 fires, 69 injury rounds and 12 predators. No errors, no pawns stuck, no flip-flopping. The mod's own memory use stays proportional to the pawns on the map. A second 3-day run gave the same result.
- Compatibility run with a 102-mod list including Combat Extended, Common Sense, Pick Up and Haul, Perspective Shift and Search and Destroy: no errors from this mod.

### Not tested yet
- RimWorld 1.7 (the 1.7 folder ships the same build as 1.6).
- CAI 5000, Save Our Ship 2, Odyssey gravship travel, fog-of-war mods. Pawns currently perceive nearby enemies from game data and do not respect fog-of-war visibility.

### Known limitations
- Sowing picks a cell, not a thing, so it is not yet kept out of a shooter's reach. A colonist who walks into the reach flees as soon as it gets there.
- A shooter can reach up to 40 cells while the game's flee goes about 23 cells, so a colonist may flee in more than one step.

If something goes wrong, please report it with your Player.log and, if you can, the output of "Explain pawn decisions" for the pawn involved.

---

## v1.1.2 (beta)
- Prevent medical task delegation; resolve job oscillation in a bleed crisis.

## v1.1.1 (beta)
- Fix edge-case engine interactions; declare the unstable testing phase.

## v1.1.0
- Universal cognitive engine, semantic affordance and tactical hardening.

## v1.0.0 (beta)
- Initial release for RimWorld 1.6 and 1.7.
