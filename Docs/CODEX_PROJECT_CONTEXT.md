# Codex project context

Updated: 2026-10-03 (Asia/Taipei)

This file stores durable context from the initial projectless Codex conversation. It is the shared handoff for future chats opened from this local project; individual chat transcripts remain separate.

## Project identity

- Local project: `C:\NTHU\Introduction_to_Game_Programming\final`
- Git remote: `https://github.com/Zzz12121/miguels-revenge.git`
- Unity Editor: `6000.6.0f1`
- Render pipeline: Universal Render Pipeline `17.6.0`
- Visual Effect Graph: `17.6.0`
- Timeline: `6.6.0`

Git is not the current priority. Do not commit or push merely because a task is finished; wait for an explicit request.

## Calligraphy boss update (2026-10-24)

- Surface flow follow-up (2026-10-25): a flat sea must still have visible horizontal motion, irregular ink islands and spirals. The sea branch of `CalligraphyInkDomain.shader` now uses jittered elliptical vortices with different directions/speeds, multiscale advection and soft irregular edges; it does not reuse the rock fracture shading. Existing Sea settings `churnSpeed`, `vortexDensity`, `vortexStrength`, `edgeStrength` control it. Preserve the thin plane and upward diffusion. `CalligraphyInkDiffusionPreview.SurfaceFlow` checks 0/2/4-second top-down renders with vertex displacement disabled, so temporal differences prove pigment movement rather than waves. See the ink diffusion report for captures/results.
- Ink sea art direction (2026-10-25) supersedes the old overturning-wave notes: keep an almost level matte ink plane and diffuse branching ink upward into the air, then dissolve it quickly. `CalligraphyInkSeaMotion` now owns one 15,680-vertex combined plume mesh with 112 equal-area, independently staggered sites by default; the old curled sheets are removed. New `CalligraphyInkDiffusion.shader` is alpha blended/ZWrite Off, uses procedural multi-scale erosion, and is referenced by `sea.diffusionMaterial` on scene/prefab for builds. `airHeight`, `airLifetime`, `airSpread`, `airOpacity`, `airBranching`, `airPlumeCount` live under skill9 → Sea. Surface displacement is clamped to .35 (default .18); never restore the viscous rolling crests for this request. See `Docs/CALLIGRAPHY_INK_DIFFUSION_UPDATE.md` and `Docs/Validation/CalligraphyInkDiffusionPreview.cs` for setup, continuity, dissolve/cleanup and actual camera captures. Damage/slow/initial launch logic was not changed.
- Avatar face correction (2026-10-25) supersedes the sixth-revision eye/hair presentation below: removed the procedural eyes from `CalligraphyAvatar.shader` and removed all head/hair/beard flutter and gaze updates. The current elder has one shared, non-readable skinned mesh and no separate hair bones, so use the user-approved static-head fallback. Only robe motion below the posed chest plane remains; all displacement, including inertia, is gated by that plane. `CalligraphyAvatarLife` resolves Chest by bone name for the generic rig and disables secondary deformation if it cannot find a reliable boundary. Obsolete hair/eye settings remain hidden solely to retain serialized values. `Docs/Validation/CalligraphyAvatarFacePreview.cs` captures front/side/back portraits and compares the head close-up with robe motion enabled/disabled; the checked result was 1 changed pixel out of 1,000,000 and no shader errors.
- Fifth skill, sixth revision: index9 `墨相鎮海・浮墨天階` is configured under `CalligraphyBossCombat.inkAvatarUltimate` on saved range `CalligraphyElder` and Battle prefab. Current behavior and Inspector paths are in `Docs/CALLIGRAPHY_SIXTH_SKILL_REPORT.md`; the fifth report is historical. Runtime owner `CalligraphyInkAvatarUltimate` clones only the textured visual rig/brush (full model height100), keeps two 60° sweeps, and constrains the actual brush vertically at ground contact. `CalligraphyAvatarLife` drives face/gaze and masked hair/beard/robe secondary motion. Sea uses displaced geometry and overturning mesh sheets; 64 convex cliff-shaped footholds have top heights 3–85 and strongly varied size. Initial impact launches once; later sea contact deals periodic damage and a scoped speed multiplier, NEVER a rescue bounce. `CalligraphyPlatformClimb` requires held W, waist below a nearby rim and facing toward the rock; no auto-climb merely from proximity. It cancels tether only after a valid catch, tracks the moving rim, poses both hands then left/right feet and restores its balanced control lock. `EnergyTetherSkill.IsPullingPlayer` is read-only; do not rewrite normal movement for this feature. Cleanup restores entry coordinates and disables all owned colliders/renderers/particles/shake. Keep projectile/feedback ownership to avoid leaked tails. Checks: `Docs/Validation/CalligraphySixthChecks.cs` plus `CalligraphyAvatarChecks.Cancellation`. Painting dragon count defaults to5, exposed at the top of Inspector group3, evenly spaced 360/count degrees. No new packages or executable rebuild in this revision; user explicitly requested commit/push in the final follow-up.
- Range controls: `RangeInputSettings` is attached to the player at runtime via `PlayerSkillAvailability`/`PlayerHealth`. K opens three paused rebinding cards (sprint/jump/roll), rejects existing-key conflicts, saves `RangeControls.v1.*` PlayerPrefs and updates the HUD; default LeftShift/Space/C. Other scenes retain LeftShift/Space/V. `PlayerSkillAvailability.TryUse()` blocks skills for the painting world's entire lifetime and shows「畫中世界無法使用技能」. All four targeting routes, public PlaySkill and tether use the gate; painting entry cancels active player skills before saving the return coordinate. See `Docs/RANGE_CONTROLS_AND_PAINTING_SKILLS.md` and `RangeControlsAndPaintingChecks`.
- Wave contact update supersedes the earlier whole-cone hit test: see `Docs/CALLIGRAPHY_WAVE_CONTACT_UPDATE.md`. Slam covers all main ground impact surfaces and applies linear distance falloff to launch speed; far edge retains 15%. Pulses clip the baked mesh to the moving texture-V band using the original Custom1.w UV offset and material tiling/offset, with a swept previous/current band. Contact is spent even when jumped/blocked, preventing landing/empty-gap hits. `pulseContactHeight` defaults to 0.55m; the normal controller jump can clear each wave. Use `CalligraphyWaveChecks` for arrival, gaps, repeated jumps, falloff and defence regressions; no player movement code or vendor VFX asset was changed.
- Playback correction: range mode now plays one selected skill then `WaitingForPreview`; Down selects/plays next, Up replays current. Restore scene's accidentally saved diagnostic moveSpeed=0/recoverySeconds=100/dodgeChance=0 to prefab values 2.8/1.1/0.3333. Beam starts charging halfway through the X stroke at body emitter height1.5; locks at firing, uses non-compounding range limiting independent of hit timing, and waits for natural package completion. Pulses direction must follow baked per-wave bounds centre outward from the effect origin: actual Cone_LongAOE particle rotation makes the visible cone point toward root -Z in the inspected resource. The former root +X assertion and corresponding test were incorrect; do not reuse that assumption.
- Latest range changes: see `Docs/CALLIGRAPHY_RANGE_UPDATE.md`, tests in `Docs/Validation/CalligraphyRangeChecks.cs`. Range floor is 200×200 (80×2.5), targets at X=-65, terrain at X=65. Scene has one `CalligraphyElder`; F8 starts that actor, stop/death reset reuses it. This scene reuses its previously saved scene actor (edit its own CalligraphyBossCombat for range values); the Battle Prefab remains reusable/fallback. Painting exit renders the outside at the saved entry coordinate, then warps the player/camera after full reveal; cancellation also returns. Camera render hooks enforce outside renderer isolation. Roll stamina/state/invulnerability commit only after actual movement; no successful roll means no payment. Pulses launch direction follows native +X, rather than prefab forward. General ranged casts keep facing the moving player.
- Follow-up: Beam package playback now starts halfway through `inkBeam.windupSeconds` (default 0.9s of 1.8s), without interrupting the second X stroke. Both Slam's `leapSlam.wave` and Pulses' `frontalPulses` use `yawOffset=-90`: an additional left rotation relative to their previous native-axis correction. Particle-mesh damage follows the rotated visuals; knockback uses the rotated direction. See `CalligraphyFourthChecks.MidWindupAndLeftWaves` for a runtime regression check. The user explicitly authorized pushing this follow-up.
- Fourth-round results supersede earlier timing/targeting defaults: see `Docs/CALLIGRAPHY_FOURTH_SKILL_REPORT.md` and executable checks in `Docs/Validation/CalligraphyFourthChecks.cs`. Rune/Fall and Big Explotion now rebase native particle delays rather than simulating past visible bursts; Rune leaves a removable 0.6× / 2.5s speed lease. Slam locks its initial horizontal direction, emits from actual brush-ground contact and launches on direct hit. General projectiles home through 90% cumulative travel; level-ground Beam is horizontal and bounded to its visible range. Painting streams/scenery are spread through the existing bounds, with interior wetlands and surface-attached eye detail. Do not revert these to the third-round 70% homing or head-targeted Slam behavior.
- The current connected Editor is 6000.6.3f1. Calligraphy testing uses `Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity`, F8 → 遊墨行者 Boss 測試.
- Durable settings belong to `Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab`, component `CalligraphyBossCombat`, with grouped custom Inspector. Runtime clone edits are temporary.
- See `Docs/CALLIGRAPHY_THIRD_SKILL_REPORT.md` for actual fields, validation results and remaining limitations; reusable checks are in `Docs/Validation/CalligraphyThirdChecks.cs` (outside Assets).
- Native VFX axes and particle rotation matter: Pulses launch follows the baked visible cone, Stab +Y. Wave damage follows particle mesh geometry. Rune Fall is triggered by warning completion, damage by the real impact burst.
- Painting world isolates outside rendering/UI, targeting, damage and player collision pairs. New external UI should check `CalligraphyWorldIsolation.Allows(ownerTransform)`; preserve player HUD. Cancellation must restore owned state.
- World size: previous base radius96 ×1.7 =163.2; do not multiply the previous vertical1.5 twice. Ground Pulse explicitly emits its first ring before stopping emission.
- This update did not authorize commit/push or package changes. Preserve pre-existing vendor material, ProjectSettings and earlier animation edits.

## Division of responsibility

The user is responsible for:

- Skill visual effects and general VFX.
- Character animation and animation presentation.
- Finding and evaluating suitable character models.
- Testing teammate systems only as needed to judge or integrate the visual result.

The following are teammate-owned unless explicitly reassigned:

1. Third-person movement, sprint, jump, and camera.
2. Three-hit normal attack combo.
3. Player health, death, respawn, and checkpoints.
4. Boss gate and battle-start flow.
5. Boss health and normal attacks.
6. Boss gameplay attacks such as earthquake, roar, beam, and jump slam.
7. The dynamic arena, including raising, sinking, collapsing, and pillar formation.

Visuals may attach to these systems, but avoid rewriting their gameplay implementation just to preview an effect.

## Desired workflow

1. Receive a reference image, video, sketch, or written description.
2. Break the look into mesh/texture, shader, Particle System or VFX Graph, animation, lighting/post-processing, sound timing, and camera needs.
3. Build the effect in an isolated test setup first.
4. Expose clear parameters for color, size, lifetime, speed, intensity, timing, and attachment points.
5. Validate compilation, Console output, visual appearance, and basic performance.
6. Package the result as reusable assets or an exportable Unity package when requested.

The user can send images to Codex. Codex can inspect them and use them as visual references, but exact reproduction can depend on missing meshes, textures, animations, render settings, or third-party assets. State these dependencies clearly instead of silently guessing.

## Unity and Codex integration

- Unity AI Game Developer is configured in `Custom` mode using a local HTTP MCP server.
- The Unity window has confirmed all three states: Unity connected, MCP server running, and `codex-mcp-client` connected.
- Codex is the selected AI agent. Cloud mode and a separate sign-in are not required for this local connection.
- Project MCP settings live in `.codex/config.toml`. The project must remain trusted for project-local configuration to load.
- Use the Codex GUI from this local project; the CLI is optional and only an alternative configuration path.

## Installed Unity extensions relevant to this work

- Animation MCP extension.
- ParticleSystem MCP extension.
- Splines MCP extension.
- Timeline MCP extension.

Extensions that were intentionally not kept for this VFX/animation-focused setup include Cinemachine, InputSystem, Navigation, ProBuilder, Terrain, and Tilemap. Reinstall one only when a concrete task needs it.

## Available project skills and prompts

The project has generated skills under `.agents/skills`, including:

- Animation clip creation, inspection, and modification.
- Animator creation, inspection, and modification.
- Particle System inspection and modification.
- Splines creation and editing.
- Timeline creation, tracks, clips, markers, and bindings.
- Shader Graph, URP HLSL, post-processing, profiling, and shader optimization guidance.
- General Unity scene, asset, GameObject, script, test, screenshot, package, and Console tools.

Relevant prompts previously enabled include Animator setup, animation blending, procedural animation, animation events, IK, Timeline sequencing, and particle-effect creation. Do not enable every prompt by default; use only prompts that match the current task to keep context focused.

## Current repository state at handoff

- `Packages/manifest.json` and `Packages/packages-lock.json` contain uncommitted package changes from configuring the Unity MCP extensions.
- The project previously compiled cleanly with no Unity Console errors or warnings after the extension cleanup.
- Preserve unrelated user and teammate changes. Inspect `git status` before editing overlapping files.

## Practical defaults for visual work

- Prefer URP-compatible shaders and materials.
- Use Particle System for straightforward sparks, smoke, trails, bursts, and attached effects when it is sufficient.
- Use VFX Graph for effects that benefit from larger GPU particle counts or more complex simulation, while remembering that current MCP graph editing may be limited.
- Use Shader Graph or HLSL for dissolve, flowing runes, distortion, masks, fresnel rims, and other material-driven looks.
- Use Animation/Animator for reusable character motion and Timeline for staged demonstrations or tightly choreographed sequences.
- Use Splines for controlled trails, beams, ribbons, paths, and guided motion.
- Build a simple comparison/test scene when judging multiple visual variants.

