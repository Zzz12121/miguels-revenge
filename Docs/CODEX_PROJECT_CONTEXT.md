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

