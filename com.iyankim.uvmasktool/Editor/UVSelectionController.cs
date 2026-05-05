using System.Collections.Generic;
using UnityEngine;

namespace IyanKim.UVMaskTool.Editor
{
    internal static class UVSelectionController
    {
        private const float BarycentricEpsilon = -0.00001f;

        public static int PickTriangle(
            Vector2 localMousePosition,
            UVPreviewRenderer.ViewTransform transform,
            int[] meshTriangles,
            List<Vector2> uvs)
        {
            return PickTriangle(localMousePosition, transform, meshTriangles, uvs, null);
        }

        public static int PickTriangle(
            Vector2 localMousePosition,
            UVPreviewRenderer.ViewTransform transform,
            int[] meshTriangles,
            List<Vector2> uvs,
            Rect[] triangleBounds)
        {
            return PickTriangle(localMousePosition, transform, meshTriangles, uvs, triangleBounds, null);
        }

        public static int PickTriangle(
            Vector2 localMousePosition,
            UVPreviewRenderer.ViewTransform transform,
            int[] meshTriangles,
            List<Vector2> uvs,
            Rect[] triangleBounds,
            IList<int> candidateTriangleIndices)
        {
            if (meshTriangles == null || uvs == null || meshTriangles.Length < 3)
            {
                return -1;
            }

            var uvPoint = transform.ScreenToUv(localMousePosition);
            var pickedTriangleIndex = -1;
            var pickedArea = float.MaxValue;
            var candidateCount = candidateTriangleIndices != null ? candidateTriangleIndices.Count : meshTriangles.Length / 3;
            for (var candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                var triangleIndex = candidateTriangleIndices != null ? candidateTriangleIndices[candidateIndex] : candidateIndex;
                var baseIndex = triangleIndex * 3;
                if (triangleIndex < 0 || baseIndex + 2 >= meshTriangles.Length)
                {
                    continue;
                }

                var a = meshTriangles[baseIndex];
                var b = meshTriangles[baseIndex + 1];
                var c = meshTriangles[baseIndex + 2];
                if (!IsValidVertexIndex(a, uvs.Count) || !IsValidVertexIndex(b, uvs.Count) || !IsValidVertexIndex(c, uvs.Count))
                {
                    continue;
                }

                if (triangleBounds != null && triangleIndex < triangleBounds.Length && !ContainsWithPadding(triangleBounds[triangleIndex], uvPoint))
                {
                    continue;
                }

                var uvA = uvs[a];
                var uvB = uvs[b];
                var uvC = uvs[c];
                if (!PointInTriangle(uvPoint, uvA, uvB, uvC))
                {
                    continue;
                }

                var area = Mathf.Abs(SignedArea(uvA, uvB, uvC));
                if (area <= 0f)
                {
                    continue;
                }

                if (area <= pickedArea)
                {
                    pickedTriangleIndex = triangleIndex;
                    pickedArea = area;
                }
            }

            return pickedTriangleIndex;
        }

        public static int PickIsland(
            Vector2 localMousePosition,
            UVPreviewRenderer.ViewTransform transform,
            int[] meshTriangles,
            List<Vector2> uvs,
            List<UVIsland> islands)
        {
            if (islands == null || islands.Count == 0)
            {
                return -1;
            }

            return FindIslandIdForTriangle(islands, PickTriangle(localMousePosition, transform, meshTriangles, uvs));
        }

        public static void ApplyClick(HashSet<int> selectedIslandIds, int islandId, bool shift, bool control)
        {
            if (selectedIslandIds == null || islandId < 0)
            {
                return;
            }

            if (control)
            {
                selectedIslandIds.Remove(islandId);
                return;
            }

            if (shift)
            {
                selectedIslandIds.Add(islandId);
                return;
            }

            if (selectedIslandIds.Contains(islandId))
            {
                selectedIslandIds.Remove(islandId);
            }
            else
            {
                selectedIslandIds.Clear();
                selectedIslandIds.Add(islandId);
            }
        }

        public static void ApplyGroupClick(HashSet<int> selectedIds, List<int> groupIds, bool shift, bool control)
        {
            if (selectedIds == null || groupIds == null || groupIds.Count == 0)
            {
                return;
            }

            if (control)
            {
                for (var i = 0; i < groupIds.Count; i++)
                {
                    selectedIds.Remove(groupIds[i]);
                }
                return;
            }

            if (shift)
            {
                for (var i = 0; i < groupIds.Count; i++)
                {
                    selectedIds.Add(groupIds[i]);
                }
                return;
            }

            var allSelected = true;
            for (var i = 0; i < groupIds.Count; i++)
            {
                if (!selectedIds.Contains(groupIds[i]))
                {
                    allSelected = false;
                    break;
                }
            }

            if (allSelected)
            {
                for (var i = 0; i < groupIds.Count; i++)
                {
                    selectedIds.Remove(groupIds[i]);
                }
                return;
            }

            selectedIds.Clear();
            for (var i = 0; i < groupIds.Count; i++)
            {
                selectedIds.Add(groupIds[i]);
            }
        }

        public static int FindIslandIdForTriangle(List<UVIsland> islands, int triangleIndex)
        {
            if (islands == null || triangleIndex < 0)
            {
                return -1;
            }

            for (var i = 0; i < islands.Count; i++)
            {
                var island = islands[i];
                if (island.triangleIndices.Count == 0)
                {
                    continue;
                }

                for (var t = 0; t < island.triangleIndices.Count; t++)
                {
                    if (island.triangleIndices[t] == triangleIndex)
                    {
                        return island.id;
                    }
                }
            }

            return -1;
        }

        public static float GetBrushUvRadius(UVPreviewRenderer.ViewTransform transform, float brushRadiusPixels)
        {
            return EstimateUvRadius(transform, brushRadiusPixels);
        }

        public static bool ApplyBrush(
            HashSet<int> selectedIds,
            Vector2 uvCenter,
            float uvRadius,
            int[] meshTriangles,
            List<Vector2> uvs,
            Rect[] triangleBounds,
            IList<int> candidateTriangleIndices,
            bool erase)
        {
            if (selectedIds == null || meshTriangles == null || uvs == null || uvRadius <= 0.000001f)
            {
                return false;
            }

            var uvRadiusSqr = uvRadius * uvRadius;
            var changed = false;
            var candidateCount = candidateTriangleIndices != null ? candidateTriangleIndices.Count : meshTriangles.Length / 3;
            for (var candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                var triangleIndex = candidateTriangleIndices != null ? candidateTriangleIndices[candidateIndex] : candidateIndex;
                var baseIndex = triangleIndex * 3;
                if (triangleIndex < 0 || baseIndex + 2 >= meshTriangles.Length)
                {
                    continue;
                }

                if (triangleBounds != null && triangleIndex < triangleBounds.Length && !IntersectsExpandedBounds(triangleBounds[triangleIndex], uvCenter, uvRadius))
                {
                    continue;
                }

                var a = meshTriangles[baseIndex];
                var b = meshTriangles[baseIndex + 1];
                var c = meshTriangles[baseIndex + 2];
                if (!IsValidVertexIndex(a, uvs.Count) || !IsValidVertexIndex(b, uvs.Count) || !IsValidVertexIndex(c, uvs.Count))
                {
                    continue;
                }

                var uvA = uvs[a];
                var uvB = uvs[b];
                var uvC = uvs[c];
                if (!CircleIntersectsTriangle(uvCenter, uvRadiusSqr, uvA, uvB, uvC))
                {
                    continue;
                }

                if (erase)
                {
                    changed |= selectedIds.Remove(triangleIndex);
                }
                else
                {
                    changed |= selectedIds.Add(triangleIndex);
                }
            }

            return changed;
        }

        private static bool ContainsWithPadding(Rect bounds, Vector2 point)
        {
            const float padding = 0.0001f;
            return point.x >= bounds.xMin - padding
                && point.x <= bounds.xMax + padding
                && point.y >= bounds.yMin - padding
                && point.y <= bounds.yMax + padding;
        }

        private static bool IntersectsExpandedBounds(Rect bounds, Vector2 center, float radius)
        {
            return center.x >= bounds.xMin - radius
                && center.x <= bounds.xMax + radius
                && center.y >= bounds.yMin - radius
                && center.y <= bounds.yMax + radius;
        }

        private static bool IsValidVertexIndex(int index, int vertexCount)
        {
            return index >= 0 && index < vertexCount;
        }

        private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            var denominator = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Approximately(denominator, 0f))
            {
                return false;
            }

            var u = ((b.y - c.y) * (point.x - c.x) + (c.x - b.x) * (point.y - c.y)) / denominator;
            var v = ((c.y - a.y) * (point.x - c.x) + (a.x - c.x) * (point.y - c.y)) / denominator;
            var w = 1f - u - v;
            return u >= BarycentricEpsilon && v >= BarycentricEpsilon && w >= BarycentricEpsilon;
        }

        private static float SignedArea(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }

        private static float EstimateUvRadius(UVPreviewRenderer.ViewTransform transform, float brushRadiusPixels)
        {
            var uvCenter = transform.ScreenToUv(Vector2.zero);
            var uvOffset = transform.ScreenToUv(new Vector2(brushRadiusPixels, 0f));
            return Vector2.Distance(uvCenter, uvOffset);
        }

        private static bool CircleIntersectsTriangle(Vector2 center, float radiusSqr, Vector2 a, Vector2 b, Vector2 c)
        {
            if (PointInTriangle(center, a, b, c))
            {
                return true;
            }

            if ((a - center).sqrMagnitude <= radiusSqr
                || (b - center).sqrMagnitude <= radiusSqr
                || (c - center).sqrMagnitude <= radiusSqr)
            {
                return true;
            }

            return DistanceToSegmentSqr(center, a, b) <= radiusSqr
                || DistanceToSegmentSqr(center, b, c) <= radiusSqr
                || DistanceToSegmentSqr(center, c, a) <= radiusSqr;
        }

        private static float DistanceToSegmentSqr(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var abSqr = ab.sqrMagnitude;
            if (abSqr <= Mathf.Epsilon)
            {
                return (point - a).sqrMagnitude;
            }

            var t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / abSqr);
            var closest = a + ab * t;
            return (point - closest).sqrMagnitude;
        }
    }
}
