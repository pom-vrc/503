ai

# pom-vrc's VRChat Avatar Tools

A small VPM repository with two NDMF-based, non-destructive avatar optimization tools:

- **[Shape Key Decimator](dev.shapekeydecimator/)** (`dev.shapekeydecimator`) - reduces polygon
  count in the regions each blend shape moves, plus an optional whole-mesh pass.
- **[Material Atlaser](dev.materialatlaser/)** (`dev.materialatlaser`) - packs SkinnedMeshRenderer
  materials' main textures into shared atlases and merges meshes/material slots down to as few
  as one.

Both are non-destructive: nothing is written to your project until you enter Play mode or upload,
and the original mesh/material/texture assets are never modified.

## Installing

**Via VRChat Creator Companion / ALCOM:** add this repository listing URL under
Settings -> Packages -> Add Repository:

```
https://raw.githubusercontent.com/pom-vrc/503/main/index.json
```

Then add "Shape Key Decimator" and/or "Material Atlaser" to your avatar project from the
Packages tab, same as any other VCC package.

**Via Unity Package Manager directly (no VCC):** Window -> Package Manager -> "+" -> Add package
from git URL:

```
https://github.com/pom-vrc/503.git?path=/dev.shapekeydecimator
https://github.com/pom-vrc/503.git?path=/dev.materialatlaser
```

**One-click installer:** double-click `dev.shapekeydecimator.unitypackage` or
`dev.materialatlaser.unitypackage` (attached to each GitHub Release) to install without VCC -
these are tiny installer stubs (using anatawa12's VPM Package Auto Installer) that add the
repository above and install the package automatically.

## License

MIT - see [LICENSE](LICENSE).
