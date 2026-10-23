# Gameplay and presentation polish

Base: `d0eb0d9`. Work branch: `codex/gameplay-polish`.

## Changes

- Relative ally/enemy labels and ground markers distinguish teams independently of character skins. Hidden enemies remain hidden.
- Gem Grab is displayed as **Gem Rush**; enum values, network IDs and saved preference keys are preserved.
- Pause follows the main menu glass/teal styling with Resume, Settings and Leave Match only. No character showcase, bot controls, progression tabs or sensitivity slider crowd the pause screen.
- Character changes are blocked in Gem Rush and It Takes Two, including the progression character-selection action and `PlayerNetwork.ChangeHero`.
- Co-op spawn selection checks ground, collision clearance, other players and recently reserved positions. It chooses a clear point beside the partner rather than stacking them at the same spawn.
- Team scores, carried gems and the victory countdown share one compact Gem Rush panel. Co-op objectives, bond status, captions and revival prompts have separate regions. World labels avoid reserved HUD regions and other labels. Kill feed and transient notifications have lower limits; only one combat status is shown at a time.
- The main-menu profile entry shows only the player name and XP bar. Clicking opens the full profile and existing progression tabs.
- Runtime and serialized player-facing content is English, including quests, skins, pet abilities, settings, controls, connection messages and co-op dialogue. Old profile reward history is translated for display without rewriting player saves. Numbers use English formatting.
- Character-selection summaries follow `GameJam/storyTelling/Better_Together_Historias_das_Personagens.pdf` and reflect each character's role and personality.
- The IP accent is `#49b0b6`. Intro country-name and coordinate labels are removed, as are map coordinate captions.
- Windows starts in borderless fullscreen, including when an older player window preference exists.

## Ryan's selective integration

Source branch: `mapa/selecao-mapas`, decoration commit `e1f2779e17c698f01760e5ed781208e61f9ba43d`.

Imported only sponsor textures/materials/mesh, `SponsorBoards`, and the decoration hook in the current map builder. The existing Cidade Velha prefab received 16 SITA/UPRANIMAL signs using that hook. Scene files, map loading, mode selection, networking logic and map geometry were not replaced from Ryan's branch.

The prefab retains **67 colliders and 10 spawn points**. All 16 signs were also checked in the live Cidade Velha scene.

## Typography and audio

- Embedded Goblin One for section titles, and static League Gothic for numerals. Official Google Fonts sources and OFL licenses are included in `Assets/_Game/Resources/Fonts`.
- Goblin One source: https://github.com/google/fonts/tree/main/ofl/goblinone
- League Gothic source: https://github.com/google/fonts/tree/main/ofl/leaguegothic
- **HK Modular remains pending a licensed app-embedding font file.** The existing display font is retained for large titles, and `GameTypography.Display` is prepared to load `Resources/Fonts/HKModular`. No substitute font was silently installed for this role.
- Treated files from `GameJam/Audio/audio tratado/TransferNow-20261018P2ZsvCl1` replace the six character voice assets. Their existing GUIDs were retained where applicable. Lupus now has a voice asset as well.
- `menu de server.mp3` is the setup-screen music for mode, map and character selection. A single looping music source changes clips between the main menu and setup, while voice ducking remains active.

## Pradaria image provenance

File: `Assets/_Game/Resources/UI/Menu/map_pradaria.png`. Generated with the built-in ImageGen tool using the previous Pradaria thumbnail plus Cidade Velha and Buracona as references. This is selection artwork, not a change to playable map geometry.

Prompt:

> Create a photorealistic cinematic game map selection thumbnail in landscape 4:3. Subject: a verdant secluded island meadow arena with realistic low weathered stone walls, small wooden supply crates, lush shrubs, flowering trees and four shallow blue ponds around a central worn footpath crossroads. Camera low elevated three-quarter angle, strong readable central composition at thumbnail size. Sunlit subtropical Atlantic island landscape, distant rugged hills and a hint of sea. Natural textures, realistic grass and stone, warm afternoon light, rich but restrained colors. Match the photographic travel-cinematic quality of the supplied Cidade Velha coastal village reference and Buracona natural rock pool reference. Preserve the meadow training-arena identity of the first reference without copying its cartoon rendering. No characters, no text, no logos, no maps or UI overlays.

## Validation

- EditMode: **92 passed, 0 failed**, including asset availability, fullscreen setting, sponsor collision/spawn preservation, concise biographies, and old-profile English display.
- PlayMode: `SixBrawlersSpecialsProgressionAndSync` **passed** in 54.64 seconds. The initial run failed the timed post-root immunity check under Editor load; an isolated repeat passed without any change to root/trap gameplay or its balance data. Both results are retained in Temp.
- Live host smoke: co-op pair spawned 2.50 m apart; 24 repeated co-op spawn selections avoided the partner; character changes were blocked in co-op and Gem Rush; Cidade Velha had 16 sponsor signs; Buracona Gem Rush ran with four players and four team markers.
- Menu/GUI captures are kept in `Temp/polish-menu-review`. The visual review found and corrected wrapped section titles and overlapping English column headers.
- Progression gameplay tests use an in-memory profile.

Physical two-computer LAN testing and HK Modular embedding remain outside the verified results above.

- Windows x64 build: **Succeeded, 0 errors, 2 warnings**, all three scenes included, 338,648,868 bytes. Startup passed with the null graphics device; the graphical startup check is recorded separately in Temp.
- The local build also includes concurrent LAN discovery/firewall work already present in the working directory. That work and its instructions are preserved but excluded from this gameplay-polish commit. The staged 94-file runtime source set was compiled independently without those LAN additions.
