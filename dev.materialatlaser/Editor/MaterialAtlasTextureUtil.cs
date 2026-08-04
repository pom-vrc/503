using System.IO;
using UnityEditor;
using UnityEngine;

namespace MaterialAtlaser.Editors
{
    /// <summary>
    /// Texture baking helpers used while building an atlas. Knows nothing about NDMF or the
    /// component - just turns a Material's main texture into a fixed-size, GPU-readback tile
    /// (with tiling baked in), suitable for feeding into Texture2D.PackTextures.
    /// </summary>
    internal static class MaterialAtlasTextureUtil
    {
        // Mip streaming only works on textures that went through a TextureImporter - a purely
        // runtime Texture2D can never have it turned on (Texture2D.streamingMipmaps is read-only
        // from script for such textures). So the packed atlas is written out here as a real PNG
        // asset, imported with mipmapStreaming enabled, and that imported asset is what gets used
        // in the atlas material. Files land in a dedicated, regenerated-every-build folder rather
        // than next to the avatar, and are overwritten in place on every build - nothing accumulates.
        private const string OutputFolderRelative = "Assets/MaterialAtlaser Generated";
        private const string OutputFolderName = "MaterialAtlaser Generated";

        // Texture properties other than the main texture that are not repacked into the atlas.
        // Their UV coordinates would no longer line up once the main texture moves into a tile,
        // so they're stripped from the resulting atlas material rather than left stale.
        private static readonly string[] SecondaryMapProperties =
        {
            "_BumpMap", "_ParallaxMap", "_MetallicGlossMap", "_OcclusionMap",
            "_EmissionMap", "_DetailAlbedoMap", "_DetailNormalMap", "_DetailMask", "_SpecGlossMap",
        };

        public static long GetMainTexturePixelArea(Material material)
        {
            var texture = material != null ? material.mainTexture : null;
            return texture != null ? (long)texture.width * texture.height : 0L;
        }

        /// <summary>
        /// A tiled material (mainTextureScale != (1,1) or mainTextureOffset != (0,0)) can't be
        /// baked into an atlas tile and remapped with a plain linear UV transform - the tile would
        /// need to represent a repeated/offset sample, and the mesh's own UVs might legitimately
        /// extend outside [0,1] for reasons that have nothing to do with tiling (e.g. UDIM-style
        /// multi-tile UV layouts), which a wrap-based fix would silently corrupt instead. Rather
        /// than guess, materials with non-default tiling are simply left out of the atlas.
        /// </summary>
        public static bool HasIdentityTiling(Material material)
        {
            if (material == null) return true;
            return material.mainTextureScale == Vector2.one && material.mainTextureOffset == Vector2.zero;
        }

        /// <summary>
        /// A tile's size before any group-wide fit scaling: its source texture's own resolution
        /// (preserving aspect ratio), or a small fixed size for materials with no main texture.
        /// Deliberately not rounded up to a power of two - Texture2D.PackTextures has no such
        /// requirement, and doing so was wasting a lot of atlas space (a 1200x1200 texture became a
        /// 2048x2048 tile).
        /// </summary>
        public static Vector2Int GetRawTileSize(Material material)
        {
            var texture = material != null ? material.mainTexture : null;
            return texture != null ? new Vector2Int(texture.width, texture.height) : new Vector2Int(64, 64);
        }

        /// <summary>
        /// A rough starting point before the real (tight) RectanglePacker runs: scales every tile
        /// down uniformly so the combined area targets a fill fraction of the atlas, so the packer
        /// isn't handed a wildly oversized request (it would just grow the bin to fit everything at
        /// full size, then get scaled back down anyway by the maxAtlasSize correction step). Returns
        /// 1 (no scaling) when everything already fits comfortably.
        /// </summary>
        public static float ComputeFitScale(Vector2Int[] rawSizes, int maxAtlasSize)
        {
            long totalArea = 0;
            foreach (var size in rawSizes) totalArea += (long)size.x * size.y;

            var budget = (long)maxAtlasSize * maxAtlasSize * 9 / 10;
            if (totalArea <= budget || totalArea <= 0) return 1f;

            return Mathf.Sqrt((float)((double)budget / totalArea));
        }

        /// <summary>Applies a fit scale to a raw tile size, clamped to a sane pixel range.</summary>
        public static Vector2Int ScaleTileSize(Vector2Int rawSize, float scale, int maxAtlasSize)
        {
            var width = Mathf.Clamp(Mathf.RoundToInt(rawSize.x * scale), 8, maxAtlasSize);
            var height = Mathf.Clamp(Mathf.RoundToInt(rawSize.y * scale), 8, maxAtlasSize);
            return new Vector2Int(width, height);
        }

        /// <summary>
        /// Renders a material's main texture into a readable tile at its own aspect ratio. Only
        /// called for materials with default tiling (mainTextureScale (1,1), offset (0,0) - see
        /// HasIdentityTiling in MaterialAtlasProcessor), so this is a plain resize, never a
        /// scaled/offset sample. Materials without a main texture fall back to a flat tile of their
        /// tint color (_Color/_BaseColor) so they can still be packed and merged.
        /// </summary>
        public static Texture2D BuildTile(Material material, Vector2Int size)
        {
            var sourceTexture = material != null ? material.mainTexture : null;

            var rt = RenderTexture.GetTemporary(size.x, size.y, 0, RenderTextureFormat.ARGB32);
            var previousActive = RenderTexture.active;

            if (sourceTexture != null)
            {
                Graphics.Blit(sourceTexture, rt);
            }
            else
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, GetFallbackColor(material));
            }

            RenderTexture.active = rt;
            var tile = new Texture2D(size.x, size.y, TextureFormat.RGBA32, true)
            {
                name = material != null ? material.name : "Atlas Tile",
            };
            tile.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
            tile.Apply();

            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(rt);
            return tile;
        }

        /// <summary>
        /// Composites pre-placed tiles (from RectanglePacker) into one atlas texture of exactly
        /// atlasSize x atlasSize. RectanglePacker's positions are top-down (Y grows downward from
        /// the first tile); GL.LoadPixelMatrix(0, atlasSize, atlasSize, 0) draws in that same
        /// top-down sense, so positions are used as-is here - the Y flip only matters when computing
        /// UV rects afterward (bottom-up), which is the caller's job, not this method's.
        /// </summary>
        public static Texture2D CompositeAtlas(Texture2D[] tiles, RectInt[] positions, int atlasSize)
        {
            var rt = RenderTexture.GetTemporary(atlasSize, atlasSize, 0, RenderTextureFormat.ARGB32);
            var previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));

            GL.PushMatrix();
            GL.LoadPixelMatrix(0, atlasSize, atlasSize, 0);
            for (var i = 0; i < tiles.Length; i++)
            {
                var p = positions[i];
                Graphics.DrawTexture(new Rect(p.x, p.y, p.width, p.height), tiles[i]);
            }
            GL.PopMatrix();

            var atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, true);
            RenderTexture.active = rt;
            atlas.ReadPixels(new Rect(0, 0, atlasSize, atlasSize), 0, 0);
            atlas.Apply();

            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(rt);
            return atlas;
        }

        /// <summary>
        /// Writes a packed atlas texture out as a real PNG asset with mip streaming enabled, and
        /// returns the imported asset (not the in-memory texture that was passed in). Overwrites
        /// any previous atlas asset with the same owner/group name.
        /// </summary>
        public static Texture2D SaveAtlasAsset(Texture2D atlasTexture, string ownerName, int groupIndex)
        {
            var absoluteFolder = Path.Combine(Application.dataPath, OutputFolderName);
            if (!Directory.Exists(absoluteFolder))
            {
                Directory.CreateDirectory(absoluteFolder);
            }

            var fileName = SanitizeFileName($"{ownerName} Atlas {groupIndex}") + ".png";
            var absolutePath = Path.Combine(absoluteFolder, fileName);
            var relativePath = $"{OutputFolderRelative}/{fileName}";

            File.WriteAllBytes(absolutePath, atlasTexture.EncodeToPNG());
            AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(relativePath) is TextureImporter importer)
            {
                importer.mipmapEnabled = true;
                importer.streamingMipmaps = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(relativePath);
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        private static Color GetFallbackColor(Material material)
        {
            if (material == null) return Color.white;
            if (material.HasProperty("_Color")) return material.GetColor("_Color");
            if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
            return Color.white;
        }

        public static void ClearUnsupportedMaps(Material material)
        {
            foreach (var property in SecondaryMapProperties)
            {
                if (material.HasProperty(property))
                {
                    material.SetTexture(property, null);
                }
            }
        }
    }
}
