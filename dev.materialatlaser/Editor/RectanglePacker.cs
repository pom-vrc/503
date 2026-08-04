using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MaterialAtlaser.Editors
{
    /// <summary>
    /// Growing binary-tree rectangle packer (the well-known "growing packer" algorithm - see e.g.
    /// Jake Gordon's "A Thousand Ways to Pack the Bin"). Texture2D.PackTextures was measured
    /// directly against this on the same input (four 832x832 tiles + one 416x416, already
    /// pre-scaled to fit a fill budget) and left ~60% of the atlas empty even so; this packer
    /// achieves ~85% fill on identical input, because it actually grows to fit tiles tightly
    /// instead of applying its own extra conservative shrink on top of an already-sized request.
    ///
    /// Coordinates are top-down (Y grows downward from the first-placed tile), matching common 2D
    /// bin-packing convention - not the bottom-up convention Unity UVs use. Callers need to flip Y
    /// when converting a placement into a UV rect (see BuildAtlasForGroup).
    /// </summary>
    internal static class RectanglePacker
    {
        private class Node
        {
            public int X, Y, W, H;
            public bool Used;
            public Node Right;
            public Node Down;
        }

        /// <summary>
        /// Packs every size without overlap, in largest-side-first order for good results, growing
        /// the bin as needed. Returns one placement per input size, in the same order as the input,
        /// plus the final bin size the packer grew to (which the caller may need to scale down to
        /// fit a hard maximum, since this packer doesn't cap itself).
        /// </summary>
        public static RectInt[] Pack(IReadOnlyList<Vector2Int> sizes, out Vector2Int binSize)
        {
            var results = new RectInt[sizes.Count];
            if (sizes.Count == 0)
            {
                binSize = Vector2Int.zero;
                return results;
            }

            var order = Enumerable.Range(0, sizes.Count)
                .OrderByDescending(i => Mathf.Max(sizes[i].x, sizes[i].y))
                .ToArray();

            var root = new Node { X = 0, Y = 0, W = sizes[order[0]].x, H = sizes[order[0]].y };

            foreach (var index in order)
            {
                var size = sizes[index];
                var node = FindNode(root, size.x, size.y) ?? GrowNode(ref root, size.x, size.y);
                var placed = SplitNode(node, size.x, size.y);
                results[index] = new RectInt(placed.X, placed.Y, size.x, size.y);
            }

            binSize = new Vector2Int(root.W, root.H);
            return results;
        }

        private static Node FindNode(Node node, int w, int h)
        {
            if (node == null) return null;
            if (node.Used)
            {
                return FindNode(node.Right, w, h) ?? FindNode(node.Down, w, h);
            }
            return w <= node.W && h <= node.H ? node : null;
        }

        private static Node SplitNode(Node node, int w, int h)
        {
            node.Used = true;
            node.Down = new Node { X = node.X, Y = node.Y + h, W = node.W, H = node.H - h };
            node.Right = new Node { X = node.X + w, Y = node.Y, W = node.W - w, H = h };
            return node;
        }

        private static Node GrowNode(ref Node root, int w, int h)
        {
            var canGrowDown = w <= root.W;
            var canGrowRight = h <= root.H;
            var shouldGrowRight = canGrowRight && root.H >= root.W + w;
            var shouldGrowDown = canGrowDown && root.W >= root.H + h;

            if (shouldGrowRight) return GrowRight(ref root, w, h);
            if (shouldGrowDown) return GrowDown(ref root, w, h);
            if (canGrowRight) return GrowRight(ref root, w, h);
            if (canGrowDown) return GrowDown(ref root, w, h);
            return null; // unreachable once the root has any real size
        }

        private static Node GrowRight(ref Node root, int w, int h)
        {
            var oldRoot = root;
            var newRoot = new Node { X = 0, Y = 0, W = oldRoot.W + w, H = oldRoot.H, Used = true };
            newRoot.Down = oldRoot;
            newRoot.Right = new Node { X = oldRoot.W, Y = 0, W = w, H = oldRoot.H };
            root = newRoot;
            return FindNode(root, w, h);
        }

        private static Node GrowDown(ref Node root, int w, int h)
        {
            var oldRoot = root;
            var newRoot = new Node { X = 0, Y = 0, W = oldRoot.W, H = oldRoot.H + h, Used = true };
            newRoot.Right = oldRoot;
            newRoot.Down = new Node { X = 0, Y = oldRoot.H, W = oldRoot.W, H = h };
            root = newRoot;
            return FindNode(root, w, h);
        }
    }
}
