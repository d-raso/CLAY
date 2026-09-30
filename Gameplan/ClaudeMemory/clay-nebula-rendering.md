---
name: clay-nebula-rendering
description: "Volumetric hero nebula system — the working approach, class system, and the checkpoint that looks decent"
metadata: 
  node_type: memory
  type: project
  originSessionId: d194da85-e834-4254-85ff-aa9b52f10fa4
  modified: 2026-09-19T03:46:20.180Z
---

Raymarched volumetric hero nebulae live in `Assets/Materials/Galaxy/VolumetricNebula.shader` (a NORMAL mesh material — camera pos IS available, unlike the BRG star/dust shaders, see [[clay-brg-shader-gotchas]]), driven by `VolumetricNebula.cs` and spawned by `GalaxyBootstrap.ConfigureNebula()`. Press **N** in Play for an isolated single-nebula preview (`SpawnSingleNebulaPreview`/`FrameNebulaPreview`), **R** for a full galaxy.

**Known-good checkpoint (2026-09-18):** backed up to `_Checkpoints/nebula_2026-09-18/` (OUTSIDE Assets so Unity doesn't compile duplicate shader names). User called this "decent" — restore from here if a later change regresses.

**What actually works (learned the hard way — many failed attempts):**
- Shape MUST come from *amplified fBm minus a radial penalty*: `envelope = fbm3(warped)*2.3 - length(p)*2.5 - thr`. This tears the boundary to a different radius per direction → irregular lobes/tendrils. Anything else (smoothstep-on-noise, noise×boxFade, a smooth radial `fall`) gives a convex blob that reads as a SPHERE, or fills the box and reads as a CUBE. Low-freq shape noise also = sphere/cube; needs ~2.3+ cells across the box.
- **Emission-absorption** compositing (both gas+dust extinguish; brightness plateaus at source radiance) — NOT purely additive (that blows the core white). `sg=gas*0.9`, `sd=dust*_Absorb*1.6`, emission `_EmissionMul*0.8`.
- Filaments need **squared ridged noise + high contrast** or they integrate to smooth fog.
- distFade in WORLD space (object-space skews with non-uniform Shape stretch): `saturate(4/camDistW)^2` → bright up close, dim far.

**Three classes** (`_Class`): 0 planetary (noise-perturbed wavy bipolar shell + knots, no dust), 1 big (sculpted Eagle/Carina), 2 fragment (eroded floaty bits via a mask). Non-uniform `Shape` stretch per nebula for silhouette variety.

**OPEN ISSUE the user still wants fixed:** dark dust clouds still read as "omissions in brightness" (absence of glow) rather than tangible lit dark clouds. User suggested **separating dark clouds into their own objects/shader** rather than coupling gas+dust in one volume. Next step to explore.
