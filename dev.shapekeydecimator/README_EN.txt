┌─┐┌─┐┌─┐
└─┐│ │ ─┤
└─┘└─┘└─┘

# Shape Key Decimator

A non-destructive Unity component that **decimates** the polygons a shape key moves, instead of deleting them. Inspired by Prefabulous Universal's *Delete Polygons* component.

## Requirements

Modular Avatar https://modular-avatar.nadena.dev/

## Install

Drop the files into your project like this. The `Editor` folder name matters

Assets/ShapeKeyDecimator/
    DecimatePolygonsByShapeKey.cs
    Editor/
        ShapeKeyDecimatorUtil.cs
        ShapeKeyMeshDecimator.cs
        ShapeKeyDecimationProcessor.cs
        ShapeKeyDecimatorPreviewManager.cs
        ShapeKeyDecimatorLocalization.cs
        ShapeKeyDecimatorNdmfPlugin.cs     <- delete this file if you don't have Modular Avatar
        DecimatePolygonsByShapeKeyEditor.cs

## Use

Add **Shape Key Decimator → Decimate Polygons By Shape Key** to any object in the avatar (the root is a good place).

1. Drag the skinned meshes you want to affect into **Skinned Meshes**. Targets are always explicit — the component never searches the hierarchy, so it can only touch meshes you hand it.
2. Expand *Add shape key to decimate* and press **+** next to a shape key. That activates it and gives it a row with a slider.
3. Drag the slider: `0` = untouched, `1` = collapse that region as far as the topology allows.
4. Optionally drag the **Whole mesh** slider to thin out everything else too.

The table shows, per row, the triangles available to that pass and the triangles left after it. The **Total triangles** box shows old → new for the whole selection.

Nothing is modified until you enter Play mode or upload the avatar.

## Quick tips

Go to Advanced and adjust Max Normal Deviation. Some models work better with extremely low numbers and some work best at 100.

Uncheck "Protect Region Boundary" if the mesh decimiation based on shape keys leaves a sharp edges

Use separate Game Objects and add the 503 Decimate Polygons By Shape Key component to it for each Skinned Mesh or set of Skinned meshes


## Estimated vs measured counts

The table works in two modes, and it tells you which one you're looking at.

**Estimate** (`~` prefix, header reads *estimate*). Region triangles × slider, computed instantly. It assumes **every collapse succeeds**, so it is an upper bound on the reduction, not a prediction. It cannot know how many collapses the validity rules will reject, because that only becomes knowable by running the algorithm.

**Measured** (no prefix, header reads *measured*). Exact counts from a real decimation pass with the current settings. You get these by pressing **Measure Exact Result** under the totals, or simply by turning on Preview — a preview *is* a real pass, so it fills the numbers in for free. The measurement is tied to a hash of every setting that affects output, so it disappears the moment you change anything and the table falls back to the estimate.

This matters most for **Max Normal Deviation**. It's a rejection threshold: at `0` no triangle may rotate at all, so on a curved mesh essentially every collapse is refused and nothing is decimated — while the estimate still cheerfully reports the full reduction. Higher values decimate more, lower values are more conservative, and the inspector warns below 15°. The same applies to a lesser degree to UV seam locking, border protection, submesh protection, and simply running out of valid collapses.

## Whole mesh and shape keys together

The two work simultaneously, and the order is fixed: **every shape key region is processed first, then the whole-mesh pass runs on what is left.** So a shape key slider means "reduce this area harder than the rest", and the whole-mesh slider means "now thin out everything, including the areas I already reduced".

That ordering is why the **Whole mesh** row's triangle count is not the original mesh total — it is the total minus whatever the shape key rows already removed. Reductions compound rather than add: shape keys at 0.5 plus whole mesh at 0.5 leaves roughly a quarter of the shared area, not zero.

Boundary protection is inherently a no-op for the whole-mesh pass (nothing is outside the region), but border, submesh and normal-deviation protection all still apply, so it will not tear open hems or slide vertices across material seams.

## Never Decimate (blacklist)

Some shape keys need their geometry to stay untouched no matter what — a jaw key whose vertices must line up exactly with a jaw bone, for instance. Expand **Add shape key to blacklist** and press **+** to protect one. A blacklisted shape key cannot also sit in the regular shape-key list (adding it to one removes it from the other), and the whole-mesh pass skips its vertices too — nothing decimates that region, ever, from either source.

## Options

| Option | What it does |
| --- | --- |
| **Skinned Meshes** | The renderers to decimate. Required — nothing happens while this list is empty. |
| **Whole mesh** | Decimation applied to the entire mesh after all shape key regions. |
| **Delta Threshold** | A vertex joins the region when a blend shape frame moves it further than this. Raise it if an exporter left near-zero deltas everywhere. |
| **Protect Region Boundary** | Keeps the vertices at the rim of the region fixed, so the seam with untouched geometry stays crisp. Turn off for more reduction. |
| **Preserve Open Borders** | Keeps vertices on open mesh edges (hems, cuffs, eyelid openings) unless the collapse runs along the border. |
| **Preserve Submesh Boundaries** | Stops collapses from crossing material boundaries. |
| **Preserve UV Seams** | Locks UV seams so a seam vertex can only slide along its own seam, never inward. Leave this on — see below. |
| **UV Error Weight** | How strongly texture stretching counts against a collapse. `0` ignores UVs (geometry only), `1` balances texture error against shape error, higher protects the texture at the cost of silhouette accuracy. |
| **Max Normal Deviation** | Rejects a collapse that would rotate a neighbouring triangle more than this. Lower is more conservative, higher decimates further. Very low values reject nearly everything — see *Estimated vs measured* above. |

## Preview

The **Preview Decimation** button temporarily swaps in the decimated result so you can judge it in the scene. While it's on the button turns green and reads `PREVIEW ON`.

Turning it on builds a decimated duplicate next to each target renderer and switches the original renderer off. Turning it off re-enables the originals, deletes the duplicates and destroys the generated meshes.

Changing any slider or setting while preview is on rebuilds it automatically. The rebuild waits for you to release the slider plus a short idle gap, because each one is a full decimation pass — scrubbing a slider queues exactly one rebuild, not one per frame. The button reads `updating…` while a rebuild is pending.

Preview is transient by design and cleans up after itself in every case I could think of:

- **Entering Play mode** turns it off first, so the build pass never sees a preview duplicate.
- **Recompiling scripts** tears it down before the domain reload rather than leaking objects.
- Preview objects are flagged `DontSave`, so they can never be written into a scene file or a build even if you save while one is live.
- The component records which renderers preview switched off. If the editor is interrupted hard enough to skip teardown entirely (a crash), that record re-enables them next time the scene loads.
- Deleting the component while previewing leaves an orphan, which gets swept on the next load. **Tools → Shape Key Decimator → Turn Off All Previews** forces a sweep at any time.

## Create Decimated Copy In Hierarchy

The button at the bottom bakes the result immediately:

- saves each reduced mesh to `Assets/ShapeKeyDecimator Output/`,
- duplicates the renderer's GameObject as the next sibling, named `… (Decimated)`,
- points the copy at the new mesh, restores its blend shape weights, and strips the component from the copy.


## Notes and limits

- The number in the **Tris** column counts triangles that lie *entirely* inside the region — those are the only ones a pass can remove.
- Only triangle-topology submeshes are decimated. Other topologies are carried through with their indices remapped.
- Region triangle counts are cached per mesh. If you re-import a model and the numbers look stale, use **Tools → Shape Key Decimator → Clear Cached Triangle Counts**.
