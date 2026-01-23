// Copyright (c) Meta Platforms, Inc. and affiliates.
using System;
using System.Collections.Generic;
using Meta.XR.Samples;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NorthStar
{
    /// <summary>
    /// Handles calculating a tighter bounds for shadowmap casting based on where shadow receiving surfaces exist (when bounded by a ShadowImportanceVolume)
    ///
    /// This improve shadow quality by making more effective use of available shadowmap resolution and precision but requires careful authoring and placement of volumes
    /// 
    /// Please note that this works in tandem with custom modifications to URP
    /// </summary>
    [MetaCodeSample("NorthStar")]
    [ExecuteAlways]
    public class ShadowImportanceVolume : MonoBehaviour
    {

        [SerializeField] private bool m_useShadowAdjustment = true;

        [SerializeField] private bool m_quantizeMovement = true;

        protected void OnEnable()
        {
            DefaultCollection.Register(this);
        }

        protected void OnDisable()
        {
            DefaultCollection.Deregister(this);
        }

        protected void OnDrawGizmos()
        {
            var gizmosMatrix = Gizmos.matrix;
            var gizmosColor = Gizmos.color;
            Gizmos.matrix = GetUnitBoxTransform();
            Gizmos.color = new Color(1f, 1f, 1f, 0.2f);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            Gizmos.matrix = gizmosMatrix;
            Gizmos.color = gizmosColor;
        }
        protected void OnDrawGizmosSelected()
        {
            var gizmosMatrix = Gizmos.matrix;
            var gizmosColor = Gizmos.color;
            Gizmos.matrix = GetUnitBoxTransform();
            Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
            Gizmos.DrawCube(Vector3.zero, Vector3.one);
            Gizmos.matrix = gizmosMatrix;
            Gizmos.color = gizmosColor;
        }

        public Matrix4x4 GetUnitBoxTransform()
        {
            return transform.localToWorldMatrix;
        }

        public class Collection
        {
            private static readonly ProfilerMarker s_shadowAdjustment = new("ShadowAdjustment");

            // All currently active volumes
            private List<ShadowImportanceVolume> m_volumes = new();

            public bool Enabled { get; set; } = true;
            public float MaximumDistanceOverride { get; set; } = float.MaxValue;

            // Add a volume to this collection
            public void Register(ShadowImportanceVolume volume)
            {
                if (m_volumes.Count == 0)
                    ShadowUtils.ShadowAdjustment += ShadowUtils_ShadowAdjustment;

                m_volumes.Add(volume);
            }

            // Remove a volume to this collection
            public void Deregister(ShadowImportanceVolume volume)
            {
                _ = m_volumes.Remove(volume);

                if (m_volumes.Count == 0)
                    ShadowUtils.ShadowAdjustment -= ShadowUtils_ShadowAdjustment;
            }

            // Apply adjustments to the shadow slice
            private static Plane[] s_planes = new Plane[6];
            private void ShadowUtils_ShadowAdjustment(ref UniversalCameraData cameraData, ref ShadowSliceData sliceData, ref float shadowDistance)
            {
                // Don't do anything if marked disabled
                if (!Enabled) return;

                shadowDistance = Mathf.Min(shadowDistance, MaximumDistanceOverride);

                using var marker = s_shadowAdjustment.Auto();

                var nearestVolume = FindNearestVolume(cameraData.worldSpaceCameraPos);

                // Tighten shadow frustum to improve texel density
                if (nearestVolume != null && nearestVolume.m_useShadowAdjustment)
                {
                    var cameraProj = cameraData.GetProjectionMatrix();
                    // Adjust the projection matrix to clamp the far-clip at shadowDistance
                    var cameraNear = cameraProj.m23 / (cameraProj.m22 - 1f);
                    var cameraFar = cameraProj.m23 / (cameraProj.m22 + 1f);
                    cameraFar = Mathf.Min(cameraFar, shadowDistance);
                    cameraProj.m22 = -(cameraFar + cameraNear) / (cameraFar - cameraNear);
                    cameraProj.m23 = -(2f * cameraFar * cameraNear) / (cameraFar - cameraNear);
                    // Get the frustum planes to intersect shadow volumes
                    var cameraVP = cameraProj * cameraData.GetViewMatrix();
                    GeometryUtility.CalculateFrustumPlanes(cameraVP, s_planes);

                    // Convert volumes to unit box transforms
                    var boxTransforms = new NativeArray<Matrix4x4>(m_volumes.Count, Allocator.TempJob);
                    for (var i = 0; i < m_volumes.Count; i++)
                        boxTransforms[i] = m_volumes[i].GetUnitBoxTransform();

                    // Compute an adjustment matrix and shadow distance
                    using var adjustmentMatrixRef = new NativeReference<Matrix4x4>(default, Allocator.TempJob);
                    using var shadowDistanceRef = new NativeReference<float>(default, Allocator.TempJob);
                    new GetAdjustmentMatrixJob()
                    {
                        CameraPosition = cameraData.worldSpaceCameraPos,
                        WorldToShadow = sliceData.projectionMatrix * sliceData.viewMatrix,
                        BoxTransforms = boxTransforms,
                        FrustumPlanes = new NativeArray<Plane>(s_planes, Allocator.TempJob),
                        OutAdjustmentMatrix = adjustmentMatrixRef,
                        OutShadowDistance = shadowDistanceRef,
                    }.Run();

                    // If an adjustment is required, apply it
                    if (adjustmentMatrixRef.Value.m33 != 0f)
                    {
                        // Apply an adjustment to the proj matrix to fit this range
                        // and a tighter shadow distance
                        sliceData.projectionMatrix = adjustmentMatrixRef.Value * sliceData.projectionMatrix;
                        shadowDistance = Mathf.Min(shadowDistance, shadowDistanceRef.Value);
                    }
                }

                // Ensure a spcific "fixedPoint" is always centre of a texel
                if (nearestVolume != null && nearestVolume.m_quantizeMovement)
                {
                    var halfRes = sliceData.resolution / 2;     // Half because projection is [-1, 1]
                    var fixedPoint = nearestVolume.transform.position;// cameraData.worldSpaceCameraPos;
                    var worldToShadow = sliceData.projectionMatrix * sliceData.viewMatrix;
                    var localPoint = worldToShadow.MultiplyPoint(fixedPoint);
                    var correction = (Vector3)math.round(localPoint * halfRes) / halfRes - localPoint;
                    sliceData.projectionMatrix = Matrix4x4.Translate(new(correction.x, correction.y, 0f))
                        * sliceData.projectionMatrix;
                }
            }

            // Nearest volume is used for quantized movement
            private ShadowImportanceVolume FindNearestVolume(Vector3 position)
            {
                var nearestDstSq = float.MaxValue;
                var nearestVolume = (ShadowImportanceVolume)null;
                foreach (var volume in m_volumes)
                {
                    var distanceSq = (volume.transform.position - position).sqrMagnitude;
                    if (distanceSq >= nearestDstSq) continue;

                    nearestDstSq = distanceSq;
                    nearestVolume = volume;
                }
                return nearestVolume;
            }
        }

        [BurstCompile]
        private struct GetAdjustmentMatrixJob : IJob
        {
            public Vector3 CameraPosition;
            public Matrix4x4 WorldToShadow;
            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<Matrix4x4> BoxTransforms;
            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<Plane> FrustumPlanes;
            public NativeReference<Matrix4x4> OutAdjustmentMatrix;
            public NativeReference<float> OutShadowDistance;

            public void Execute()
            {
                // Get points of significance for shadowing (corners of intersected shadow volumes)
                using var points = new NativeList<Vector3>(16, Allocator.Temp);
                using var intersection = new BoxFrustumIntersection(Allocator.Temp);
                foreach (var boxTransform in BoxTransforms)
                {
                    intersection.FromBox(boxTransform);
                    for (var i = 0; i < FrustumPlanes.Length; i++)
                    {
                        _ = intersection.Slice(FrustumPlanes[i]);
                    }
                    points.AddRange(intersection.GetCorners());
                    intersection.Clear();
                }

                // points may be empty if no volumes exist, or the camera does not intersect any
                if (points.Length == 0) return;

                // Compute a local shadow-space bounding range of the points
                var range = GetLocalBounds(points.AsArray(), WorldToShadow);

                // Calculate an adjustment to fit the projection more tightly
                OutAdjustmentMatrix.Value = GetAdjustmentMatrix(range);

                // Recalculate the shadow distance
                OutShadowDistance.Value = GetShadowDistance(CameraPosition, points);
            }
        }

        public struct BoxFrustumIntersection : IDisposable
        {
            private struct Edge
            {
                public int Corner1, Corner2;
                public int PolygonL, PolygonR;
                public Edge(int corner1, int corner2, int polyL, int polyR)
                {
                    Corner1 = corner1;
                    Corner2 = corner2;
                    PolygonL = polyL;
                    PolygonR = polyR;
                }
                public int GetPolygon(bool sign) { return sign ? PolygonR : PolygonL; }
                public int GetCorner(bool index) { return index ? Corner2 : Corner1; }
                public override string ToString() { return $"<{Corner1} - {Corner2}>"; }
            }
            private NativeList<Edge> m_edges;
            private NativeList<Vector3> m_corners;
            private BitField64 m_cornerAllocation;
            private int m_polygonCounter;

            public BoxFrustumIntersection(Allocator allocator) : this()
            {
                m_edges = new(allocator);
                m_corners = new(allocator);
            }

            public void Dispose()
            {
                m_edges.Dispose();
                m_corners.Dispose();
            }

            public void Clear()
            {
                m_edges.Clear();
                m_corners.Clear();
                m_cornerAllocation = new(0ul);
                m_polygonCounter = 0;
            }

            public void FromBox(Matrix4x4 unitBoxTransform)
            {
                Debug.Assert(m_edges.Length == 0);
                Debug.Assert(m_corners.Length == 0);
                Debug.Assert(m_polygonCounter == 0);
                for (var i = 0; i < 8; i++)
                {
                    var point = new Vector3((i >> 0) & 1, (i >> 1) & 1, (i >> 2) & 1);
                    point -= new Vector3(0.5f, 0.5f, 0.5f);
                    m_corners.Add(unitBoxTransform.MultiplyPoint3x4(point));
                }
                m_cornerAllocation.SetBits(0, true, m_corners.Length);
                m_edges.Add(new(0, 1, 0, 1)); // Bottom
                m_edges.Add(new(1, 3, 0, 2));
                m_edges.Add(new(3, 2, 0, 4));
                m_edges.Add(new(2, 0, 0, 5));
                m_edges.Add(new(4, 6, 3, 5)); // Top
                m_edges.Add(new(6, 7, 3, 4));
                m_edges.Add(new(7, 5, 3, 2));
                m_edges.Add(new(5, 4, 3, 1));
                m_edges.Add(new(0, 4, 1, 5)); // Mid
                m_edges.Add(new(1, 5, 2, 1));
                m_edges.Add(new(3, 7, 4, 2));
                m_edges.Add(new(2, 6, 5, 4));
                m_polygonCounter += 6;
            }

            public bool Slice(Plane plane)
            {
                BitField64 removePointsMask = default;
                Span<float> dpCache = stackalloc float[m_corners.Length];
                for (var mask = m_cornerAllocation.Value; mask != 0; mask &= mask - 1)
                {
                    var cornerIndex = math.tzcnt(mask);
                    var dp = plane.GetDistanceToPoint(m_corners[cornerIndex]);
                    dpCache[cornerIndex] = dp;
                    if (dp >= 0.01f) continue;
                    removePointsMask.SetBits(cornerIndex, true);
                }
                if (removePointsMask.Value == 0) return false;

                // Create new corners and remove orphaned edges
                var insertedCorners = new NativeList<(int, bool)>(4, Allocator.Temp);
                for (var e = 0; e < m_edges.Length; e++)
                {
                    var edge = m_edges[e];
                    var keep1 = removePointsMask.GetBits(edge.Corner1) == 0;
                    var keep2 = removePointsMask.GetBits(edge.Corner2) == 0;
                    if (keep1 != keep2)
                    {
                        // Need to insert a corner here
                        insertedCorners.Add(new(e, keep1));
                        var dp1 = dpCache[edge.Corner1];
                        var dp2 = dpCache[edge.Corner2];
                        var cornerId = math.tzcnt(~m_cornerAllocation.Value);
                        if (cornerId >= m_corners.Length) m_corners.Add(default);
                        m_corners[cornerId] = Vector3.Lerp(
                            m_corners[edge.Corner1],
                            m_corners[edge.Corner2],
                            (0 - dp1) / (dp2 - dp1)
                        );
                        m_cornerAllocation.SetBits(cornerId, true);
                        (keep1 ? ref edge.Corner2 : ref edge.Corner1) = cornerId;
                        m_edges[e] = edge;
                    }
                    else if (!keep1)
                    {
                        // Edge is entirely on wrong side, delete it
                        m_edges.RemoveAtSwapBack(e--);
                    }
                }
                // Stitch newly inserted corners
                var newPoly = m_polygonCounter++;
                for (var c1 = 0; c1 < insertedCorners.Length; ++c1)
                {
                    var corner1 = insertedCorners[c1];
                    var edge1 = m_edges[corner1.Item1];
                    var nextPoly1 = edge1.GetPolygon(corner1.Item2);
                    var c2 = 0;
                    if (c1 < insertedCorners.Length - 1)
                    {
                        for (c2 = c1 + 1; c2 < insertedCorners.Length; c2++)
                        {
                            var tcorner = insertedCorners[c2];
                            var tprevPoly = m_edges[tcorner.Item1].GetPolygon(!tcorner.Item2);
                            if (tprevPoly == nextPoly1) break;
                        }
                        (insertedCorners[c2], insertedCorners[c1 + 1])
                            = (insertedCorners[c1 + 1], insertedCorners[c2]);
                        c2 = c1 + 1;
                    }
                    var corner2 = insertedCorners[c2];
                    var edge2 = m_edges[corner2.Item1];
                    m_edges.Add(new(edge1.GetCorner(corner1.Item2), edge2.GetCorner(corner2.Item2), newPoly, nextPoly1));
                }
                // Delete unreferenced corners
                m_cornerAllocation.Value &= ~removePointsMask.Value;
                insertedCorners.Dispose();
                return true;
            }

            public NativeArray<Vector3> GetCorners()
            {
                while (true)
                {
                    var lastSet = 64 - math.lzcnt(m_cornerAllocation.Value);
                    while (m_corners.Length > lastSet) m_corners.RemoveAt(m_corners.Length - 1);

                    var firstEmpty = math.tzcnt(~m_cornerAllocation.Value);
                    if (firstEmpty >= m_corners.Length) break;
                    m_corners.RemoveAtSwapBack(firstEmpty);
                    m_cornerAllocation.SetBits(firstEmpty, true);
                    m_cornerAllocation.SetBits(m_corners.Length, false);
                }
                return m_corners.AsArray();
            }
        }

        // Adjustment matrix to remap a projection to a specific local bounds
        public static Matrix4x4 GetAdjustmentMatrix(Bounds range, bool tightZ = false)
        {
            var size = range.size;
            if (size.x <= 0f) return Matrix4x4.identity;

            var scale = new Vector3(2f / size.x, 2f / size.y, 2f / size.z);
            var offset = Vector3.Scale(-range.center, scale);
            if (!tightZ) { scale.z = 1f; offset.z = 0f; }
            return Matrix4x4.TRS(offset, Quaternion.identity, scale);
        }

        // Get the farthest point from the camera (farthest point that can receive shadows)
        private static float GetShadowDistance(Vector3 cameraPosition, NativeList<Vector3> points)
        {
            var shadowDistance = 0f;
            foreach (var point in points)
            {
                shadowDistance = Mathf.Max(shadowDistance, (point - cameraPosition).sqrMagnitude);
            }
            return Mathf.Sqrt(shadowDistance);
        }

        // Get a bounding box of all points after transforming into shadow space
        public static Bounds GetLocalBounds(NativeArray<Vector3> points, Matrix4x4 worldToShadow)
        {
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var corner in points)
            {
                var localCorner = worldToShadow.MultiplyPoint3x4(corner);
                min = Vector3.Min(min, localCorner);
                max = Vector3.Max(max, localCorner);
            }

            min = Vector3.Max(min, new Vector3(-1f, -1f, -1f));
            max = Vector3.Min(max, new Vector3(+1f, +1f, +1f));
            // Dont adjust depth range - we already have enough precision here and
            // we want to capture all shadowcasters casting on the importance range
            min.z = -1f;
            max.z = +1f;
            return new Bounds((min + max) / 2f, max - min);
        }
        public static readonly Collection DefaultCollection = new();

    }
}
