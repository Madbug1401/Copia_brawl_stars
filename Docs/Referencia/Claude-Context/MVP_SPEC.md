# FPS-LAN — MVP Specification

## MVP Goal

Produce a genuinely playable LAN build for 2–4 computers.

The MVP is successful when multiple computers on the same local network can host/join and play a Deathmatch.

## Required Features

### Networking
- Host
- Join
- LAN connectivity
- synchronized players
- synchronized movement
- synchronized combat
- synchronized health
- synchronized deaths
- synchronized respawns
- synchronized scoreboard data

### Player
- first-person camera
- WASD
- mouse look
- sprint
- jump
- gravity
- collision

### Weapon
- one automatic rifle
- firing
- fire rate
- magazine
- reload
- ammunition
- recoil
- damage
- hit detection
- headshots

### Health / Death
- health
- damage
- death
- respawn
- spawn points
- kills/deaths

### Game Mode
- Free For All / Deathmatch
- basic match state
- scoreboard

### UI
- main menu
- Host
- Join
- health
- ammo
- crosshair
- kills/deaths
- scoreboard

### Audio / VFX
- weapon sound
- hit sound
- hit marker
- reload sound
- footsteps
- muzzle flash
- bullet impact
- death feedback

## MVP Rule

Do not delay the playable build for polish.

Placeholders are acceptable.

Functional multiplayer gameplay is more important than visual quality at this stage.

## Success Test

Target test:

PC 1 — Host
PC 2 — Join
PC 3 — Join
PC 4 — Join

All players should be able to:
- enter the same map
- move
- see each other
- shoot
- damage each other
- kill each other
- respawn
- see updated kills/deaths
