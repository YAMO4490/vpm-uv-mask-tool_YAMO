using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace IyanKim.UVMaskTool.Editor
{
    internal static class UVPreviewRenderer
    {
        private static readonly Color Background = new Color(0.12f, 0.12f, 0.12f, 1f);
        private static readonly Color GridMajor = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color GridMinor = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color HoverFill = new Color(0.72f, 0.86f, 1f, 0.55f);
        private static readonly Color SelectedFill = new Color(1f, 0.67f, 0.15f, 0.72f);

        public readonly struct ViewTransform
        {
            private readonly Rect viewRect;
            private readonly Rect uvBounds;
            private readonly float scale;
            private readonly Vector2 pan;

            public ViewTransform(Rect viewRect, Rect uvBounds, float zoom, Vector2 pan)
            {
                this.viewRect = viewRect;
                this.uvBounds = uvBounds;
                this.pan = pan;

                var safeWidth = Mathf.Max(uvBounds.width, 0.0001f);
                var safeHeight = Mathf.Max(uvBounds.height, 0.0001f);
                var fitScale = Mathf.Min(viewRect.width / safeWidth, viewRect.height / safeHeight) * 0.88f;
                scale = Mathf.Max(1f, fitScale) * Mathf.Max(0.05f, zoom);
            }

            public Vector2 UvToScreen(Vector2 uv)
            {
                var uvCenter = uvBounds.center;
                var viewCenter = viewRect.center + pan;
                return new Vector2(
                    viewCenter.x + (uv.x - uvCenter.x) * scale,
                    viewCenter.y - (uv.y - uvCenter.y) * scale);
            }

            public Vector2 ScreenToUv(Vector2 point)
            {
                var uvCenter = uvBounds.center;
                var viewCenter = viewRect.center + pan;
                return new Vector2(
                    uvCenter.x + (point.x - viewCenter.x) / scale,
                    uvCenter.y - (point.y - viewCenter.y) / scale);
            }
        }

        public static ViewTransform CreateView(Rect localPreviewRect, List<Vector2> uvs, float zoom, Vector2 pan)
        {
            return new ViewTransform(localPreviewRect, CalculateUvBounds(uvs), zoom, pan);
        }

        public static ViewTransform CreateView(Rect localPreviewRect, List<Vector2> uvs, int[] meshTriangles, float zoom, Vector2 pan)
        {
            return new ViewTransform(localPreviewRect, CalculateUvBounds(uvs, meshTriangles), zoom, pan);
        }

        public static Rect CalculateUvBounds(List<Vector2> uvs)
        {
            if (uvs == null || uvs.Count == 0)
            {
                return new Rect(0f, 0f, 1f, 1f);
            }

            var min = uvs[0];
            var max = uvs[0];
            for (var i = 1; i < uvs.Count; i++)
            {
                min = Vector2.Min(min, uvs[i]);
                max = Vector2.Max(max, uvs[i]);
            }

            if (Mathf.Abs(max.x - min.x) < 0.0001f)
            {
                min.x -= 0.5f;
                max.x += 0.5f;
            }

            if (Mathf.Abs(max.y - min.y) < 0.0001f)
            {
                min.y -= 0.5f;
                max.y += 0.5f;
            }

            var bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            var padding = Mathf.Max(bounds.width, bounds.height) * 0.08f;
            bounds.xMin -= padding;
            bounds.xMax += padding;
            bounds.yMin -= padding;
            bounds.yMax += padding;
            return bounds;
        }

        public static Rect CalculateUvBounds(List<Vector2> uvs, int[] meshTriangles)
        {
            if (uvs == null || meshTriangles == null || meshTriangles.Length == 0)
            {
                return CalculateUvBounds(uvs);
            }

            var hasBounds = false;
            var min = Vector2.zero;
            var max = Vector2.zero;
            for (var i = 0; i < meshTriangles.Length; i++)
            {
                var vertexIndex = meshTriangles[i];
                if (vertexIndex < 0 || vertexIndex >= uvs.Count)
                {
                    continue;
                }

                var uv = uvs[vertexIndex];
                if (!hasBounds)
                {
                    min = uv;
                    max = uv;
                    hasBounds = true;
                    continue;
                }

                min = Vector2.Min(min, uv);
                max = Vector2.Max(max, uv);
            }

            if (!hasBounds)
            {
                return CalculateUvBounds(uvs);
            }

            if (Mathf.Abs(max.x - min.x) < 0.0001f)
            {
                min.x -= 0.5f;
                max.x += 0.5f;
            }

            if (Mathf.Abs(max.y - min.y) < 0.0001f)
            {
                min.y -= 0.5f;
                max.y += 0.5f;
            }

            var bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            var padding = Mathf.Max(bounds.width, bounds.height) * 0.08f;
            bounds.xMin -= padding;
            bounds.xMax += padding;
            bounds.yMin -= padding;
            bounds.yMax += padding;
            return bounds;
        }

        public static void Draw(
            Rect previewRect,
            int[] meshTriangles,
            Vector2[] uvs,
            List<UVIsland> islands,
            HashSet<int> selectedTriangleIndices,
            int hoverTriangleIndex,
            int hoverIslandId,
            Vector2Int[] wireEdges,
            bool showWireframe,
            Color wireframeColor,
            Texture2D previewTexture,
            float previewTextureAlpha,
            ViewTransform transform)
        {
            EditorGUI.DrawRect(previewRect, Background);
            GUI.BeginClip(previewRect);

            DrawTextureOverlay(previewTexture, previewTextureAlpha, transform);

            Handles.BeginGUI();

            DrawGrid(new Rect(0f, 0f, previewRect.width, previewRect.height), transform);

            if (meshTriangles != null && uvs != null)
            {
                var screenUvs = BuildScreenUvs(uvs, transform);
                DrawHoverFills(meshTriangles, screenUvs, islands, selectedTriangleIndices, hoverTriangleIndex, hoverIslandId);
                DrawSelectedFills(meshTriangles, screenUvs, selectedTriangleIndices);
                if (showWireframe)
                {
                    DrawWireframe(wireEdges, meshTriangles, screenUvs, wireframeColor);
                }
            }

            Handles.EndGUI();
            GUI.EndClip();

            GUI.Box(previewRect, GUIContent.none, EditorStyles.helpBox);
        }

        private static void DrawGrid(Rect localRect, ViewTransform transform)
        {
            DrawUvLine(transform, new Vector2(0f, 0f), new Vector2(1f, 0f), GridMajor, 2f);
            DrawUvLine(transform, new Vector2(1f, 0f), new Vector2(1f, 1f), GridMajor, 2f);
            DrawUvLine(transform, new Vector2(1f, 1f), new Vector2(0f, 1f), GridMajor, 2f);
            DrawUvLine(transform, new Vector2(0f, 1f), new Vector2(0f, 0f), GridMajor, 2f);

            for (var i = -4; i <= 4; i++)
            {
                var value = i * 0.25f;
                DrawUvLine(transform, new Vector2(value, -4f), new Vector2(value, 4f), GridMinor, 1f);
                DrawUvLine(transform, new Vector2(-4f, value), new Vector2(4f, value), GridMinor, 1f);
            }

            EditorGUI.DrawRect(new Rect(0f, 0f, localRect.width, 1f), GridMajor);
            EditorGUI.DrawRect(new Rect(0f, localRect.height - 1f, localRect.width, 1f), GridMajor);
            EditorGUI.DrawRect(new Rect(0f, 0f, 1f, localRect.height), GridMajor);
            EditorGUI.DrawRect(new Rect(localRect.width - 1f, 0f, 1f, localRect.height), GridMajor);
        }

        public static Rect[] BuildTriangleBounds(int[] meshTriangles, List<Vector2> uvs)
        {
            if (meshTriangles == null || uvs == null || meshTriangles.Length < 3)
            {
                return new Rect[0];
            }

            var triangleCount = meshTriangles.Length / 3;
            var bounds = new Rect[triangleCount];
            for (var triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                var baseIndex = triangleIndex * 3;
                var a = meshTriangles[baseIndex];
                var b = meshTriangles[baseIndex + 1];
                var c = meshTriangles[baseIndex + 2];
                if (!IsValidVertexIndex(a, uvs.Count) || !IsValidVertexIndex(b, uvs.Count) || !IsValidVertexIndex(c, uvs.Count))
                {
                    bounds[triangleIndex] = new Rect(0f, 0f, 0f, 0f);
                    continue;
                }

                var uvA = uvs[a];
                var uvB = uvs[b];
                var uvC = uvs[c];
                var minX = Mathf.Min(uvA.x, Mathf.Min(uvB.x, uvC.x));
                var minY = Mathf.Min(uvA.y, Mathf.Min(uvB.y, uvC.y));
                var maxX = Mathf.Max(uvA.x, Mathf.Max(uvB.x, uvC.x));
                var maxY = Mathf.Max(uvA.y, Mathf.Max(uvB.y, uvC.y));
                bounds[triangleIndex] = Rect.MinMaxRect(minX, minY, maxX, maxY);
            }

            return bounds;
        }

        public static int[] BuildIslandMembership(List<UVIsland> islands, int triangleCount)
        {
            var membership = new int[triangleCount];
            for (var i = 0; i < membership.Length; i++)
            {
                membership[i] = -1;
            }

            if (islands == null)
            {
                return membership;
            }

            for (var i = 0; i < islands.Count; i++)
            {
                var island = islands[i];
                for (var t = 0; t < island.triangleIndices.Count; t++)
                {
                    var triangleIndex = island.triangleIndices[t];
                    if (triangleIndex >= 0 && triangleIndex < membership.Length)
                    {
                        membership[triangleIndex] = island.id;
                    }
                }
            }

            return membership;
        }

        public static Vector2Int[] BuildWireEdges(int[] meshTriangles)
        {
            if (meshTriangles == null || meshTriangles.Length < 2)
            {
                return new Vector2Int[0];
            }

            var seen = new HashSet<ulong>();
            var edges = new List<Vector2Int>(meshTriangles.Length);
            for (var i = 0; i + 2 < meshTriangles.Length; i += 3)
            {
                AddWireEdge(meshTriangles[i], meshTriangles[i + 1], seen, edges);
                AddWireEdge(meshTriangles[i + 1], meshTriangles[i + 2], seen, edges);
                AddWireEdge(meshTriangles[i + 2], meshTriangles[i], seen, edges);
            }

            return edges.ToArray();
        }

        private static void DrawHoverFills(
            int[] meshTriangles,
            Vector2[] screenUvs,
            List<UVIsland> islands,
            HashSet<int> selectedTriangleIndices,
            int hoverTriangleIndex,
            int hoverIslandId)
        {
            Handles.color = HoverFill;

            if (hoverIslandId >= 0 && islands != null && hoverIslandId < islands.Count)
            {
                var hoveredIsland = islands[hoverIslandId];
                for (var i = 0; i < hoveredIsland.triangleIndices.Count; i++)
                {
                    var triangleIndex = hoveredIsland.triangleIndices[i];
                    if (selectedTriangleIndices != null && selectedTriangleIndices.Contains(triangleIndex))
                    {
                        continue;
                    }

                    DrawTriangle(meshTriangles, screenUvs, triangleIndex);
                }
                return;
            }

            if (hoverTriangleIndex >= 0 && (selectedTriangleIndices == null || !selectedTriangleIndices.Contains(hoverTriangleIndex)))
            {
                DrawTriangle(meshTriangles, screenUvs, hoverTriangleIndex);
            }
        }

        private static void DrawSelectedFills(
            int[] meshTriangles,
            Vector2[] screenUvs,
            HashSet<int> selectedTriangleIndices)
        {
            if (selectedTriangleIndices == null || selectedTriangleIndices.Count == 0)
            {
                return;
            }

            Handles.color = SelectedFill;
            foreach (var triangleIndex in selectedTriangleIndices)
            {
                DrawTriangle(meshTriangles, screenUvs, triangleIndex);
            }
        }

        private static void DrawWireframe(Vector2Int[] wireEdges, int[] meshTriangles, Vector2[] screenUvs, Color wireframeColor)
        {
            Handles.color = wireframeColor;
            if (wireEdges == null || wireEdges.Length == 0)
            {
                for (var i = 0; i < meshTriangles.Length; i += 3)
                {
                    var a = meshTriangles[i];
                    var b = meshTriangles[i + 1];
                    var c = meshTriangles[i + 2];
                    if (!IsValidVertexIndex(a, screenUvs.Length) || !IsValidVertexIndex(b, screenUvs.Length) || !IsValidVertexIndex(c, screenUvs.Length))
                    {
                        continue;
                    }

                    Handles.DrawAAPolyLine(1.4f, screenUvs[a], screenUvs[b], screenUvs[c], screenUvs[a]);
                }
                return;
            }

            for (var i = 0; i < wireEdges.Length; i++)
            {
                var edge = wireEdges[i];
                if (!IsValidVertexIndex(edge.x, screenUvs.Length) || !IsValidVertexIndex(edge.y, screenUvs.Length))
                {
                    continue;
                }

                Handles.DrawAAPolyLine(1.2f, screenUvs[edge.x], screenUvs[edge.y]);
            }
        }

        private static void DrawTextureOverlay(Texture2D previewTexture, float alpha, ViewTransform transform)
        {
            if (previewTexture == null || alpha <= 0.001f)
            {
                return;
            }

            var topLeft = transform.UvToScreen(new Vector2(0f, 1f));
            var bottomRight = transform.UvToScreen(new Vector2(1f, 0f));
            var rect = Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
            if (rect.width <= 0.001f || rect.height <= 0.001f)
            {
                return;
            }

            var previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            GUI.DrawTextureWithTexCoords(rect, previewTexture, new Rect(0f, 0f, 1f, 1f), true);
            GUI.color = previousColor;
        }

        private static Vector2[] BuildScreenUvs(Vector2[] uvs, ViewTransform transform)
        {
            var screenUvs = new Vector2[uvs.Length];
            for (var i = 0; i < uvs.Length; i++)
            {
                screenUvs[i] = transform.UvToScreen(uvs[i]);
            }

            return screenUvs;
        }

        private static void DrawTriangle(int[] meshTriangles, Vector2[] screenUvs, int triangleIndex)
        {
            var baseIndex = triangleIndex * 3;
            if (triangleIndex < 0 || baseIndex + 2 >= meshTriangles.Length)
            {
                return;
            }

            var a = meshTriangles[baseIndex];
            var b = meshTriangles[baseIndex + 1];
            var c = meshTriangles[baseIndex + 2];
            if (!IsValidVertexIndex(a, screenUvs.Length) || !IsValidVertexIndex(b, screenUvs.Length) || !IsValidVertexIndex(c, screenUvs.Length))
            {
                return;
            }

            Handles.DrawAAConvexPolygon(screenUvs[a], screenUvs[b], screenUvs[c]);
        }

        private static void DrawUvLine(ViewTransform transform, Vector2 fromUv, Vector2 toUv, Color color, float width)
        {
            Handles.color = color;
            Handles.DrawAAPolyLine(width, transform.UvToScreen(fromUv), transform.UvToScreen(toUv));
        }

        private static bool IsValidVertexIndex(int index, int count)
        {
            return index >= 0 && index < count;
        }

        private static void AddWireEdge(int a, int b, HashSet<ulong> seen, List<Vector2Int> edges)
        {
            if (a == b)
            {
                return;
            }

            var min = Mathf.Min(a, b);
            var max = Mathf.Max(a, b);
            var key = ((ulong)(uint)min << 32) | (uint)max;
            if (!seen.Add(key))
            {
                return;
            }

            edges.Add(new Vector2Int(min, max));
        }
    }
}
