---
name: clay-brg-shader-gotchas
description: "Entities Graphics/BRG DOTS-instanced shaders in CLAY's galaxy can't read the camera position — use clip-space math instead"
metadata: 
  node_type: memory
  type: reference
  originSessionId: d194da85-e834-4254-85ff-aa9b52f10fa4
  modified: 2026-08-21T03:39:58.301Z
---

CLAY's galaxy renders via Entities Graphics (BatchRendererGroup) DOTS-instanced billboard shaders (Assets/Materials/Galaxy/InstancedStar.shader, GalacticDust.shader). Two hard constraints, learned the painful way (hours of magenta stars):

- **Camera POSITION is NOT bound** for these draws. `_WorldSpaceCameraPos`, `GetCameraPositionWS()`, and even a script-fed material/global vector all make the star shader fail to compile → **magenta squares**. The VIEW/PROJECTION matrices (`UNITY_MATRIX_V`, `TransformWorldToHClip`) ARE bound and safe to use.
- To get camera-relative info (distance, on-screen size) without the camera position: project points to clip space and compare. The star "resolve/blend when far" effect measures on-screen size by projecting `center` and `center + right*size` via `TransformWorldToHClip` and taking their NDC separation, then clamps sub-pixel stars to a min size + dims by area (flux conservation). No camera coord needed.

Other notes:
- Every `UnityPerMaterial` CBUFFER member must have a matching `Properties` entry, or Entities Graphics rejects the material (magenta). Plain float properties (like `_Softness`, `_MinPointNdc`) are fine; the camera stuff was the real culprit, not extra properties.
- **Editing a shader while in Play mode often does NOT recompile it** — BRG keeps showing the old/error variant. Always exit Play, wait for the compile spinner, then re-enter Play to test shader changes.
- Starlight "milky when far" is done two ways that stack: the clip-space sub-pixel blend (in-shader) + URP **Bloom** as a GPU post-process (set up at runtime in [[clay-environment-architecture]]'s GalaxyBootstrap via a global Volume; needs Camera.main tagged MainCamera + HDR).
