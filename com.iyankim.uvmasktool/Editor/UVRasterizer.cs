using System;
using System.Collections.Generic;
using UnityEngine;

namespace IyanKim.UVMaskTool.Editor
{
    internal static class UVRasterizer
    {
        private const float AreaEpsilon = 0.00000001f;

        public static Texture2D Rasterize(
            Mesh mesh,
            List<UVIsland> islands,
            HashSet<int> selectedIds,
            int resolution,
            Color bgColor,
            Color fgColor)
        {
            var uvs = UVIslandDetector.ReadUVs(mesh, 0);
            return Rasterize(mesh, uvs, islands, selectedIds, resolution, bgColor, fgColor, 1);
        }

        public static Texture2D Rasterize(
            Mesh mesh,
            List<Vector2> uvs,
            List<UVIsland> islands,
            HashSet<int> selectedIds,
            int resolution,
            Color bgColor,
            Color fgColor,
            int aaScale)
        {
            return Rasterize(mesh, mesh != null ? mesh.triangles : null, uvs, islands, selectedIds, resolution, bgColor, fgColor, aaScale);
        }

        public static Texture2D Rasterize(
            Mesh mesh,
            int[] meshTriangles,
            List<Vector2> uvs,
            List<UVIsland> islands,
            HashSet<int> selectedIds,
            int resolution,
            Color bgColor,
            Color fgColor,
            int aaScale)
        {
            if (islands == null)
            {
                throw new ArgumentNullException(nameof(islands));
            }

            var selectedTriangleIndices = new HashSet<int>();
            if (selectedIds != null)
            {
                for (var i = 0; i < islands.Count; i++)
                {
                    var island = islands[i];
                    if (!selectedIds.Contains(island.id))
                    {
                        continue;
                    }

                    for (var t = 0; t < island.triangleIndices.Count; t++)
                    {
                        selectedTriangleIndices.Add(island.triangleIndices[t]);
                    }
                }
            }

            return Rasterize(mesh, meshTriangles, uvs, selectedTriangleIndices, resolution, bgColor, fgColor, aaScale);
        }

        public static Texture2D Rasterize(
            Mesh mesh,
            int[] meshTriangles,
            List<Vector2> uvs,
            HashSet<int> selectedTriangleIndices,
            int resolution,
            Color bgColor,
            Color fgColor,
            int aaScale)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (uvs == null || uvs.Count != mesh.vertexCount)
            {
                throw new InvalidOperationException("UV data is missing or does not match the mesh vertex count.");
            }

            if (meshTriangles == null || meshTriangles.Length == 0)
            {
                throw new InvalidOperationException("No triangles are available for the selected material slot.");
            }

            if (selectedTriangleIndices == null || selectedTriangleIndices.Count == 0)
            {
                throw new InvalidOperationException("Select at least one UV face before export.");
            }

            if (resolution <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(resolution), "Resolution must be greater than zero.");
            }

            aaScale = Mathf.Clamp(aaScale, 1, 4);
            var workingSize = checked(resolution * aaScale);
            var pixelCount = (long)workingSize * workingSize;
            if (pixelCount > int.MaxValue)
            {
                throw new InvalidOperationException("Requested texture is too large.");
            }

            var pixels = new Color32[(int)pixelCount];
            var bg = (Color32)bgColor;
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = bg;
            }

            var fg = (Color32)fgColor;
            foreach (var triangleIndex in selectedTriangleIndices)
            {
                if (triangleIndex < 0 || triangleIndex * 3 + 2 >= meshTriangles.Length)
                {
                    continue;
                }

                RasterizeTriangle(meshTriangles, uvs, triangleIndex, workingSize, pixels, fg);
            }

            if (aaScale > 1)
            {
                pixels = Downsample(pixels, resolution, aaScale);
            }

            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
            {
                name = "UV_Island_Mask"
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static void RasterizeTriangle(
            int[] meshTriangles,
            List<Vector2> uvs,
            int triangleIndex,
            int size,
            Color32[] pixels,
            Color32 fill)
        {
            var baseIndex = triangleIndex * 3;
            var uvA = uvs[meshTriangles[baseIndex]];
            var uvB = uvs[meshTriangles[baseIndex + 1]];
            var uvC = uvs[meshTriangles[baseIndex + 2]];

            var area = SignedArea(uvA, uvB, uvC);
            if (Mathf.Abs(area) <= AreaEpsilon)
            {
                return;
            }

            var minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(uvA.x, Mathf.Min(uvB.x, uvC.x)) * size), 0, size - 1);
            var maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(uvA.x, Mathf.Max(uvB.x, uvC.x)) * size), 0, size - 1);
            var minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(uvA.y, Mathf.Min(uvB.y, uvC.y)) * size), 0, size - 1);
            var maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(uvA.y, Mathf.Max(uvB.y, uvC.y)) * size), 0, size - 1);

            if (maxX < 0 || maxY < 0 || minX >= size || minY >= size)
            {
                return;
            }

            for (var y = minY; y <= maxY; y++)
            {
                var py = (y + 0.5f) / size;
                var row = y * size;
                for (var x = minX; x <= maxX; x++)
                {
                    var px = (x + 0.5f) / size;
                    if (PointInTriangle(new Vector2(px, py), uvA, uvB, uvC, area))
                    {
                        pixels[row + x] = fill;
                    }
                }
            }
        }

        private static Color32[] Downsample(Color32[] source, int resolution, int scale)
        {
            var target = new Color32[resolution * resolution];
            var workingSize = resolution * scale;
            var sampleCount = scale * scale;

            for (var y = 0; y < resolution; y++)
            {
                for (var x = 0; x < resolution; x++)
                {
                    var r = 0;
                    var g = 0;
                    var b = 0;
                    var a = 0;

                    for (var sy = 0; sy < scale; sy++)
                    {
                        var sourceY = y * scale + sy;
                        var row = sourceY * workingSize;
                        for (var sx = 0; sx < scale; sx++)
                        {
                            var color = source[row + x * scale + sx];
                            r += color.r;
                            g += color.g;
                            b += color.b;
                            a += color.a;
                        }
                    }

                    target[y * resolution + x] = new Color32(
                        (byte)(r / sampleCount),
                        (byte)(g / sampleCount),
                        (byte)(b / sampleCount),
                        (byte)(a / sampleCount));
                }
            }

            return target;
        }

        private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c, float area)
        {
            var w0 = SignedArea(b, c, point) / area;
            var w1 = SignedArea(c, a, point) / area;
            var w2 = 1f - w0 - w1;
            const float epsilon = -0.00001f;
            return w0 >= epsilon && w1 >= epsilon && w2 >= epsilon;
        }

        private static float SignedArea(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }
    }
}
