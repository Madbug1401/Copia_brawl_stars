# FPS-LAN — Technical Direction

## General Principle

Build the smallest technically sound architecture that can support the MVP and future expansion.

Avoid overengineering.

## Unity

Before implementing major systems:
1. inspect the Unity version
2. inspect installed packages
3. inspect existing project structure
4. inspect existing scenes/assets/scripts
5. avoid destroying useful existing work

## Networking

Choose the networking solution based on the actual Unity version and project state.

Priorities:
1. LAN reliability
2. low latency
3. simplicity
4. maintainability
5. future extensibility

Do not manually reinvent networking if a suitable Unity networking solution is available.

## Architecture

Prefer separated responsibilities.

Possible systems:
- PlayerController
- PlayerLook
- PlayerHealth
- PlayerCombat
- WeaponController
- Weapon
- DamageSystem
- RespawnSystem
- NetworkPlayer
- MatchManager
- ScoreManager
- UIManager
- AudioManager

Do not create huge monolithic scripts.

## Suggested Organization

Assets/
  _Game/
    Scripts/
      Player/
      Weapons/
      Combat/
      Multiplayer/
      GameModes/
      UI/
      Audio/
      Systems/
    Prefabs/
    Scenes/
    Materials/
    Audio/
    VFX/
    UI/

Adapt this structure if the actual project benefits from a better organization.

## Performance

The target machines may be modest.

Avoid:
- unnecessary per-frame work
- excessive dynamic objects
- uncontrolled particle spawning
- unnecessary allocations
- systems that add complexity without gameplay value

Use pooling where appropriate.

## Development Philosophy

Build in this order:

1. project inspection
2. player movement
3. basic map
4. multiplayer connection
5. networked player
6. weapon
7. damage/death
8. respawn
9. scoreboard
10. LAN test
11. game feel
12. polish

Always preserve a playable state when possible.
