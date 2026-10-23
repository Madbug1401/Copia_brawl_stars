# UPRA pet food and SITA spray branding

UPRA is a saved pet-food inventory, separate from coins and pet XP. Pets keep their existing passive abilities. Feeding adds a small temporary enhancement.

## Feeding

- Default control: **T**, rebindable under Settings > Controls > Feed pet (UPRA).
- Requires an equipped pet, a living human player, a running casual match and at least one UPRA.
- One accepted feeding consumes one UPRA. Rejected requests consume nothing.
- Duration: **20 seconds**. Cooldown: **90 seconds from activation**. Maximum: **three uses per match**.
- Death, changing pets or ending the match cancels the effect. Death does not reset the cooldown or usage count. A new match resets them.
- Competitive matches keep pets cosmetic and do not consume food.
- The host validates eligibility, effect values, time window, cooldown and usage quota; their state replicates to clients. Stock follows the game's existing local-profile trust model, like coins; it is not a backend-verified economy.
- A compact status line sits inside the existing health/ammo HUD region, with no extra floating notifications.

| Pet | Additional UPRA effect |
|---|---|
| Faro (dog) | +1 m detection range |
| Bigodes (cat) | +2% movement speed |
| Toucinho (pig) | +5% natural regeneration |
| Alvorada (rooster) | +1 m warning range |
| Pipoca (rabbit) | +3% sprint speed |
| Carapaca (turtle) | +5% natural regeneration |

Food never increases damage or maximum health. Percentage boosts multiply the current stat; the host recalculates from the original loadout when the effect expires, preventing repeated multiplication or permanent stat changes.

## Obtaining UPRA

- Six units are granted once to new and existing profiles, without resetting progression.
- Each valid completed match grants two units. Duplicate match results cannot award food twice.
- The Pets screen shows stock and offers three UPRA for 15 in-game coins.
- Stock is capped at 999. Buying a pack requires room for the entire pack; a failed purchase spends no coins.

## SITA

The existing official SITA texture from the map sponsor assets is printed around the white band of the Ms spray cans. Curved UV-mapped labels preserve its proportions and are visible in first person, third person and previews. Both cans share the material and mesh. No colliders or weapon-stat changes were added.

## Validation

- **111 Editor tests passed**, including 19 food/branding cases covering save migration, persistence, reward deduplication, stock bounds, purchasing, expiry, cooldown, use quota, eligibility, all six light boosts and collision-free SITA printing.
- Live host verification passed: exact one-unit consumption; 2% movement increase; immediate repeat rejection; original stats restored on expiry; death cancels the boost but retains cooldown/quota; third use works after respawn; fourth use is refused; new match resets quota; competitive feeding is refused without consumption.
- Ms was explicitly selected for the final live capture; both cans carried the real SITA label. The first inspection caught a mirrored texture, which was corrected before the final capture.
- Runtime tests use an in-memory profile. Captures and reports are in `Temp/upra-sita-review`, `Temp/upra-editor-tests.json` and `Temp/upra-gameplay-smoke.txt`.
- Physical two-computer LAN testing has not been performed for this change.
- Concurrent LAN discovery/firewall changes and their instructions remain outside this commit; they are preserved in the working directory and therefore included in the local Windows build.

Windows x64 build: **Succeeded, 0 errors, 493 warnings**, with all three maps (338,657,956 bytes). Of these, 491 warning lines concern the existing Sentis compute shaders; the remaining warnings concern the test/performance package and optional Pipeline runtime configuration. Direct3D graphical startup passed with no exceptions or missing-script errors. The 97 staged runtime source files also compile independently without concurrent LAN additions.
