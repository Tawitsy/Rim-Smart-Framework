# Changelog

## v1.3.0

Colonists now avoid firing zones on the way, not only at the destination, and the mod recognises popular mods it runs alongside. Tested on RimWorld 1.6 and the 1.7 unstable branch.

### Added
- **Colonists route around firing zones.** When a reasonable detour exists, undrafted colonists walk around the area an active shooter can hit instead of straight through it. Drafted colonists and anything you order directly take the normal path.
- **Cell work stays out of firing zones.** Sowing, roofing, floors, clearing snow, fishing and the other jobs that target a cell are no longer picked inside a shooter's reach (they already were for jobs that target an item or building).
- **Compatibility detection.** The mod recognises 26 popular mods and adapts. Mod Settings shows a "Detected mods" section and the log lists them at startup.
  - While You're Up / Jobs of Opportunity: the mod's on-route hauling is switched off and left to them (a setting keeps ours instead).
  - Dubs Bad Hygiene: a desperate bladder or thirst lets a colonist leave shelter, like hunger does.
  - Common Sense, Combat Extended and Perspective Shift keep their existing adapters.
- **Turret and mortar discipline (option, off by default).** Your turrets and mortars hold fire when a colonist is in the line or the blast. A target you aim by hand always fires.
- **Mind module** (can be switched off in Mod Settings, restart required).
  - **Tantrums spare explosives.** Colonists in a tantrum no longer smash explosives (shells, chemfuel, IEDs, modded explosives) or shelves and other storage that hold them. Can be turned off.
  - **Choose which mental breaks can happen.** Mod Settings lists every Major and Extreme mental break, including ones added by other mods. Uncheck a break and your colonists will not get it: the game picks another break of the same strength, or a milder one if none is left. Applies to your colonists only; everything is allowed by default.
- **Advanced settings (Dev mode only).** Tune the danger system: how long a shooter is remembered, the minimum and maximum shooter reach, how often room danger is rechecked, how long colonists wait instead of working inside a shooter's reach (at most 2 seconds), heavy evaluations per tick, and the extra path cost of a firing zone (0 turns the detours off). A "Reset" button restores the tested defaults.
- **Threat overlay (Dev mode only).** Draws each shooter the mod is tracking as a ring on the map: red while it is a threat, gray once it is not.

### Fixed
- **Your orders come first, everywhere.**
  - A hauling detour is never inserted in front of fleeing, getting out of the heat or cold, a job you ordered, or a move the mod issued.
  - Early rescue never picks up a colonist you are controlling (drafted, given an order, an order queued with Shift, or your Perspective Shift avatar), including when you take over while the rescuer is still on the way.
- **Explosions.**
  - Explosions that cannot hurt a colonist (firefoam, or blocked by a wall) no longer make them run.
  - A colonist carrying a patient is not made to drop them to flee.
  - Large blasts (e.g. antigrain) now get a real exit outside the blast, with enough time to walk there.
  - A bleeding colonist keeps running from an explosive instead of stopping to tend wounds inside the blast.
  - A second explosive makes a fleeing colonist re-plan; small rooms where the game's own 9-cell flee finds no exit now get one.
- **Toxic fallout** only keeps colonists inside if it can actually hurt them: toxic resistance and immunity count.
- **Rain is no longer treated as danger.** Cleaning filth outdoors that the rain is already washing away is skipped instead (not in observe-only mode).
- Colonists no longer get stuck believing a spot is safe (or dangerous) after a wall is built or destroyed, or after an enemy stops being a threat: the mod rechecks within a second.
- The mod tracks where a shooter is standing right now, not where it stood a few steps ago.
- A room is judged by several points, so a room that is only visible along one side is no longer treated as safe.
- Hauling into storage that sits inside a shooter's range is caught too, not only picking things up there.
- Colonists no longer keep re-picking the same job inside a shooter's range. They wait a moment and decide again, and the job is no longer blocked for colonists set to Attack.
- The mod tracks up to 128 shooters at once (was 64), which matters in very large raids.
- Colonists who are very hungry or exhausted are allowed to go eat or sleep in every situation.
- The Mod Settings window no longer collapses to its title when many mods are detected.
- The mental break list shows each break's real name.

### Performance
Measured in-game with 110 colonists and a 90-raider raid: the mod's own time is about 0.12 ms per tick during normal work and about 0.13-0.21 ms per tick during the raid (about 1% of the tick).

### Tested
- 62 automated in-game tests, all passing on RimWorld 1.6 and on the 1.7 unstable branch.
- Colony soaks: two 3-day runs (40 colonists, 12 raids, 26 fires, 70 injury rounds, 12 predators each) and a 1-day run with raids back to back. No errors from this mod, no pawns stuck, no flip-flopping, memory stays flat.
- A 117-mod list including Combat Extended, Humanoid Alien Races, Vanilla Expanded Framework, Vehicle Framework, Pick Up and Haul, Perspective Shift, Common Sense, Dubs Bad Hygiene and Hospitality.
- A popular-mod list without Combat Extended: CAI 5000, Yayo's Combat 3, While You're Up, Smart Medicine, Simple Sidearms, Achtung!, Work Tab, Allow Tool, Replace Stuff, RimHUD, Numbers, Better Pawn Control, Colony Groups and Better Workbench Management.

### RimWorld 1.7 (unstable branch)
- The same build runs on 1.7; all patches apply and every test passes.
- When you first start 1.7 with a 1.6 config, the game resets your mod list to Core. Re-enable your mods.

### Known limitations
- A colonist may still choose a wander destination inside a firing zone; the route there avoids the zone where it can.
- A shooter's reach is a bounded estimate (8-40 cells, line of sight only), and pawns do not respect fog-of-war visibility.
- A shooter can reach up to 40 cells while the game's flee goes about 23 cells, so a colonist may flee in more than one step.
- Not tested yet: Save Our Ship 2, fog-of-war mods.

If something goes wrong, please report it with your Player.log and, if you can, the output of "Explain pawn decisions" for the pawn involved.

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
