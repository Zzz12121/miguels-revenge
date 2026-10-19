# Codex project guidance

## Communication

- Use Traditional Chinese by default unless the user asks for another language.
- Lead with the practical next action and explain Unity terminology in plain language.

## User-owned scope

- The user's primary responsibility is skill VFX, general visual effects, character animation, and finding suitable character models.
- Treat movement, sprinting, jumping, camera, normal attack combos, player health/death/respawn/checkpoints, boss-door flow, boss health/AI/attacks, and the dynamic arena as teammate-owned systems.
- Inspect or test teammate-owned systems when needed to integrate or evaluate visuals, but do not take ownership of or substantially rewrite them unless the user explicitly asks.
- For VFX or animation tasks, consult `Docs/CODEX_PROJECT_CONTEXT.md` for the current setup and decisions.

## Working method

- Prefer modular, reusable prefabs, materials, shaders, animation assets, and test scenes that can be handed to teammates without coupling them to unfinished gameplay code.
- Inspect the current project structure and existing assets before creating new folders or naming conventions.
- Keep preview/test wiring separate from production gameplay logic. Use small adapters or isolated test objects where necessary.
- Use the connected Unity MCP tools when practical. After changes, check compilation and the Unity Console, then visually verify effects in Game or Scene view.
- When the user supplies a reference image, analyze its shapes, timing, color, glow, distortion, particles, trails, and camera presentation, then state what can be recreated and implement the closest practical Unity version.
- Consider URP transparency, overdraw, particle count, shader variants, and target-platform performance while preserving the intended look.
- Do not commit, push, install, or remove packages unless the user explicitly requests it for the current task.

