ai

# pom-vrc's VRChat Avatar Tools

A small VPM repository with three avatar optimization tools:

- **[Shape Key Decimator](dev.shapekeydecimator/)** (`dev.shapekeydecimator`) - reduces polygon
  count in the regions each blend shape moves, plus an optional whole-mesh pass.
- **[Material Atlaser](dev.materialatlaser/)** (`dev.materialatlaser`) - packs SkinnedMeshRenderer
  materials' main textures into shared atlases and merges meshes/material slots down to as few
  as one.
- **[Bone Merger](dev.bonemerger/)** (`dev.bonemerger`) - right-click one or more bones in the
  Hierarchy to merge them into their parent, redistributing skin weights and recomputing bindposes
  so affected meshes deform identically.

Shape Key Decimator and Material Atlaser are both NDMF-based and non-destructive: nothing is
written to your project until you enter Play mode or upload, and the original mesh/material/
texture assets are never modified. Bone Merger is the exception - it's a destructive command that
runs immediately when invoked (Undo/Ctrl+Z reverts it), since merging bones only makes sense as a
one-time, permanent decision rather than something to redo on every build.

## Installing

**One-click (VCC / ALCOM):**

[![Add to VCC](https://img.shields.io/badge/-Add%20to%20VCC-blue?style=for-the-badge)](https://pom-vrc.github.io/503/)

**Manually, via VRChat Creator Companion / ALCOM:** add this repository listing URL under
Settings -> Packages -> Add Repository:

```
https://raw.githubusercontent.com/pom-vrc/503/master/index.json
```

Then add "Shape Key Decimator", "Material Atlaser" and/or "Bone Merger" to your avatar project
from the Packages tab, same as any other VCC package.

**Via Unity Package Manager directly (no VCC):** Window -> Package Manager -> "+" -> Add package
from git URL:

```
https://github.com/pom-vrc/503.git?path=/dev.shapekeydecimator
https://github.com/pom-vrc/503.git?path=/dev.materialatlaser
https://github.com/pom-vrc/503.git?path=/dev.bonemerger
```

**One-click installer:** double-click `dev.shapekeydecimator.unitypackage` or
`dev.materialatlaser.unitypackage` (attached to each GitHub Release) to install without VCC -
these are tiny installer stubs (using anatawa12's VPM Package Auto Installer) that add the
repository above and install the package automatically. (Bone Merger doesn't have one of these
yet - add the repository above and install it from the Packages tab instead.)

## License

MIT - see [LICENSE](LICENSE).
