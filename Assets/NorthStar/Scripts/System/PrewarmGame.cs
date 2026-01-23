// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using System.Collections.Generic;
using Meta.Utilities;
using Meta.XR.Samples;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NorthStar
{
    /// <summary>
    /// Prewarm's the game by loading any required shader variants and caching all relevant PSO's to prevent hitching during gameplay
    /// </summary>
    [MetaCodeSample("NorthStar")]
    public class PrewarmGame : MonoBehaviour
    {
        [SerializeField] private QualityData m_qualityData;

        [SerializeField, AutoSet(typeof(GameFlowController))] private GameFlowController m_gameFlow;
        [SerializeField] private ReflectionProbe m_reflectionProbe;
        [SerializeField] private Light m_pointLight;
        [SerializeField] private Light m_directionalLight;

        [SerializeField] private UnityEngine.UI.Image m_loadingBarFill;

        private float LoadingProgress
        {
            set => m_loadingBarFill.fillAmount = value;
        }

#if UNITY_EDITOR
        [MenuItem("Tools/NorthStar/Remove Non-Renderers")]
        private static void RemoveNonRenderers()
        {
            if (Selection.activeObject != null)
            {
                for (var i = 0; i < 3; i++)
                {
                    var components = (Selection.activeObject as GameObject).GetComponentsInChildren<Component>();
                    foreach (var component in components)
                    {
                        component.gameObject.SetActive(true);
                        if (component is Renderer or MeshFilter or SkinnedMeshRenderer)
                        {
                            continue;
                        }
                        DestroyImmediate(component);
                    }
                }
            }
        }
#endif

        private void Start()
        {
            m_renderPass = new("PrewarmOpaque", this, RenderQueueRange.opaque);
            m_renderPassTransparents = new("PrewarmTransparent", this, RenderQueueRange.transparent) { renderPassEvent = RenderPassEvent.AfterRenderingTransparents };
            m_renderPassMotionVector = new("PrewarmMotionVector", this, RenderQueueRange.opaque) { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques, };

            _ = StartCoroutine(PrewarmEverything());
        }

        private IEnumerator<float> Loader_Everything()
        {
            // Tell unity to prewarm shaders
            IEnumerator<float> WarmUpShaders()
            {
                var shaders = m_qualityData.CurrentPreset.ShaderVariants;
                while (!shaders.WarmUpProgressively(100))
                {
                    yield return (float)shaders.warmedUpVariantCount / shaders.variantCount;
                }
            }
            // Force render the scene in several permutations to generate PSOs
            IEnumerator<float> RenderScenePermutations()
            {
                const int DRAWRANGESTEPS = 20;
                for (var i = 0; i < 2 * 2 * 2; i++)
                {
                    m_reflectionProbe.enabled = (i & (1 << 0)) != 0;
                    m_directionalLight.enabled = (i & (1 << 1)) != 0;
                    m_pointLight.enabled = (i & (1 << 2)) != 0;

                    for (var r = 0; r < 10; r++)
                    {
                        // More likely to hit unique shaders at the start
                        var rangeFrom = Mathf.Pow((float)r / DRAWRANGESTEPS, 2f);
                        var rangeTo = Mathf.Pow((float)(r + 1) / DRAWRANGESTEPS, 2f);
                        m_renderPass.SetDrawRange(rangeFrom, rangeTo);
                        m_renderPassTransparents.SetDrawRange(rangeFrom, rangeTo);
                        m_renderPassMotionVector.SetDrawRange(rangeFrom, rangeTo);

                        yield return (i + (float)r / DRAWRANGESTEPS) / 8.0f;
                    }
                }
            }

            // 25% spent on warming shaders
            for (var it = WarmUpShaders(); it.MoveNext();)
            {
                yield return Mathf.Lerp(0f, 0.25f, it.Current);
            }

            // 75% on rendering scene permutations
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;

            for (var it = RenderScenePermutations(); it.MoveNext();)
            {
                yield return Mathf.Lerp(0.25f, 0.95f, it.Current);
            }

            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

            _ = CollisionMaterialPairs.Instance;

            yield return 1f;
        }

        private IEnumerator PrewarmEverything()
        {
            for (var it = Loader_Everything(); it.MoveNext();)
            {
                LoadingProgress = it.Current;
                yield return null;
            }
            m_gameFlow.enabled = true;
        }

        private PrewarmRenderPass m_renderPass;
        private PrewarmRenderPass m_renderPassTransparents;
        private PrewarmRenderPassMV m_renderPassMotionVector;

        public class PrewarmRenderPass : ScriptableRenderPass
        {
            public readonly PrewarmGame PrewarmGame;

            protected readonly ProfilingSampler m_profilingSampler;

            /// <summary>
            /// Used for PSO (PipelineStateObject) warmup by rendering degenerate triangles with various vertex attributes
            /// </summary>
            protected Mesh[] m_meshes;

            // Only process 1 light-probe face
            protected int m_lastReflectionProbeFrame;

            // Render a subset of the full shader range each frame
            protected float m_drawRangeFrom;
            protected float m_drawRangeTo;

            // Render shaders within the specified renderqueue
            private RenderQueueRange m_renderQueueRange;

            public PrewarmRenderPass(string profilerTag, PrewarmGame prewarmGame, RenderQueueRange renderQueueRange)
            {
                PrewarmGame = prewarmGame;
                m_profilingSampler = new ProfilingSampler(profilerTag);
                m_renderQueueRange = renderQueueRange;

                m_meshes = new Mesh[]
                {
                    CreateTriangle(true, true, true),       // 4876 meshes
                    CreateTriangle(false, true, true),      // 2280 meshes
                    CreateTriangle(false, true, false),     // 1006 meshes
                    CreateTriangle(true, true, false),      // 752 meshes
                    CreateTriangle(true, false, false),     // 254 meshes
                    //CreateTriangle(false, false, false),
                };
            }

            public void SetDrawRange(float from, float to)
            {
                m_drawRangeFrom = from;
                m_drawRangeTo = to;
            }

            private Mesh CreateTriangle(bool color, bool uv1, bool uv2)
            {
                var tri = new Mesh
                {
                    vertices = new Vector3[] { new(0, 0, 0), new(0, 0, 0), new(0, 0, 0) },
                    normals = new Vector3[] { Vector3.zero, Vector3.zero, Vector3.zero },
                    tangents = new Vector4[] { Vector4.zero, Vector4.zero, Vector4.zero },
                };

                if (color) tri.colors = new Color[] { Color.white, Color.white, Color.white };
                if (uv1) tri.uv = new Vector2[] { Vector2.zero, Vector2.zero, Vector2.zero };
                if (uv2) tri.uv2 = new Vector2[] { Vector2.zero, Vector2.zero, Vector2.zero };

                tri.SetIndices(new ushort[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
                tri.bounds = new Bounds(Vector3.zero, new(float.MaxValue, float.MaxValue, float.MaxValue));

                return tri;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (m_meshes[0] == null) return;
                // We only need to render to 1 face of the probe to be sure PSOs have been created
                if (renderingData.cameraData.cameraType == CameraType.Reflection)
                {
                    if (m_lastReflectionProbeFrame == Time.frameCount) return;
                    m_lastReflectionProbeFrame = Time.frameCount;
                }

                var cmd = CommandBufferPool.Get();

                // _USE_INTERACTION_PULSE
                using (new ProfilingScope(cmd, m_profilingSampler))
                {

                    cmd.SetViewport(new Rect(0, 0, 1, 1));

                    var shaderCount = PrewarmGame.m_qualityData.CurrentPreset.ShaderVariantsSO.Shaders.Count;

                    var shaderFrom = Mathf.RoundToInt(m_drawRangeFrom * shaderCount);
                    var shaderTo = Mathf.RoundToInt(m_drawRangeTo * shaderCount);
                    RenderVariants(cmd, shaderFrom, shaderTo, ref renderingData);
                }

                ref var cameraData = ref renderingData.cameraData;
                var cameraTarget = cameraData.cameraTargetDescriptor;
                var scaleFactor = new Vector2(ScalableBufferManager.widthScaleFactor, ScalableBufferManager.heightScaleFactor);
                cmd.SetViewport(new Rect(0, 0, cameraTarget.width * scaleFactor.x, cameraTarget.height * scaleFactor.y));

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            protected virtual bool IncludeInPass(ShaderVariantCollectionSO.ShaderData shaderData)
            {
                return shaderData.PassType != PassType.MotionVectors;
            }

            protected virtual void RenderVariants(CommandBuffer cmd, int shaderFrom, int shaderTo, ref RenderingData renderingData)
            {
                var shaderVariants = PrewarmGame.m_qualityData.CurrentPreset.ShaderVariantsSO;

                for (var i = shaderFrom; i < shaderTo; i++)
                {
                    var variant = shaderVariants.Shaders[i];
                    if (!IncludeInPass(variant)) continue;

                    var material = variant.Material;
                    var renderQueue = material.renderQueue;
                    if (renderQueue == -1) renderQueue = material.shader.renderQueue;
                    if (renderQueue < m_renderQueueRange.lowerBound || renderQueue > m_renderQueueRange.upperBound) continue;

                    var pass = variant.PassIndex;
                    if (pass == -1) continue;

                    var attrSets = shaderVariants.GetAttributeSetsForShader(material.shader);
                    if (attrSets != null)
                    {
                        // If we have tracked the usages of this shader in-world, prewarm those mesh types
                        foreach (var attrSet in attrSets)
                        {
                            cmd.DrawMesh(attrSet.GetDummyMesh(), Matrix4x4.identity, material, 0, pass);
                        }
                    }
                    else
                    {
                        // Otherwise prewarm a fixed set of common mesh configurations
                        foreach (var mesh in m_meshes)
                        {
                            cmd.DrawMesh(mesh, Matrix4x4.identity, material, 0, pass);
                        }
                    }
                }
            }
        }

        public class PrewarmRenderPassMV : PrewarmRenderPass
        {
            private RTHandle m_motionVectorRTHandle;

            public PrewarmRenderPassMV(string profilerTag, PrewarmGame prewarmGame, RenderQueueRange renderQueueRange) : base(profilerTag, prewarmGame, renderQueueRange)
            {
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                ref var cameraData = ref renderingData.cameraData;
                // Unity 6: motionVectorRenderTarget is RenderTargetIdentifier, need to wrap in RTHandle
                var rtMotionId = cameraData.xr.motionVectorRenderTarget;

                // Allocate or update RTHandle wrapper
                if (m_motionVectorRTHandle == null)
                {
                    m_motionVectorRTHandle = RTHandles.Alloc(rtMotionId);
                }
                else if (m_motionVectorRTHandle.nameID != rtMotionId)
                {
                    RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref m_motionVectorRTHandle, rtMotionId);
                }

                // Configure target with RTHandle
                ConfigureTarget(m_motionVectorRTHandle, m_motionVectorRTHandle);
                base.OnCameraSetup(cmd, ref renderingData);
            }

            public override void OnCameraCleanup(CommandBuffer cmd)
            {
                Dispose();
                base.OnCameraCleanup(cmd);
            }

            protected override bool IncludeInPass(ShaderVariantCollectionSO.ShaderData shaderData)
            {
                return shaderData.PassType == PassType.MotionVectors;
            }

            public void Dispose()
            {
                m_motionVectorRTHandle?.Release();
                m_motionVectorRTHandle = null;
            }
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            cam.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(m_renderPass);
            cam.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(m_renderPassTransparents);
            cam.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(m_renderPassMotionVector);
        }
    }
}
