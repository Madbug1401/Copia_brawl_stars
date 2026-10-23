# Ms paint spray readability

The old effect emitted a few smoke particles with enough velocity and lifetime to travel substantially beyond the 7.5 m damage range. The replacement is a pooled, deterministic paint aerosol reconstructed inside the current attack volume.

- A short jet connects both cans to the spray, with layered violet mist, irregular paint droplets and a thin far rim showing the cone boundary.
- The nominal envelope uses the attack origin, direction, 7.5 m radial range, 36-degree opening and 0.12 s expansion. It fades within the existing 0.6 s attack duration.
- Paint lanes and the far rim raycast against the same shot mask as damage, ignoring the shooter and triggers. Particles stop before the first obstruction; regular depth testing also hides them behind walls.
- Color overrides from skins remain supported. The SITA can labels are retained.
- Both particle systems, their arrays, shared materials and the visual root are reused. Effects are released when the attack expires, its shooter dies, or combat is cleared. Authoritative-only attack records create no visual instance.
- Damage, scheduled ticks, falloff, ammunition, cooldown and deliveries are unchanged. `ConeTickAll` is identical to the previous commit. Its existing character-radius tolerance remains unchanged.

## Validation

- 115 Editor tests passed, including four new cases for original Ms damage/delivery values, range expansion and cone directions, open-space particle bounds, obstacle clipping and reuse.
- A test fixture was moved from coordinates 10,000 to 1,000 to avoid floating-point cancellation in angle measurements; gameplay data was not changed to address the test.
- Live first-person captures use a fixed simulation step so the short burst is captured during its active frames. Open-space and near-wall shots are retained in `Temp/ms-spray-review`.
- A live Ms host shot expired with zero active spray roots remaining. The test used an in-memory profile.
- Visual review prompted stronger mist contrast on bright maps and irregular, camera-facing paint flecks that remain visible when looking along their travel direction.

Concurrent LAN, audio, interface and reload-sound changes remain preserved outside this commit and are included in the local build. Physical two-computer LAN testing has not been performed for this visual change.

The 98 staged runtime C# sources compile independently without the concurrent changes.

Windows x64 build: **Succeeded, 0 errors, 493 warnings**, all three scenes included, 338,916,388 bytes. Direct3D startup passed with no exception or missing-script errors. The build report and startup log are retained in Temp.
