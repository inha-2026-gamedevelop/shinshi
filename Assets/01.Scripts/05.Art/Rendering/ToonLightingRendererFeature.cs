// Unity
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Shinshi.Art
{
    /// <summary> 불투명 렌더링 뒤 화면 깊이와 노멀로 메인 광원 조명을 다시 계산해 계단 쪽으로 바꾸고 경계선을 그린다 </summary>
    // 모델과 재질을 바꾸지 않고 화면을 툰처럼 보이게 하려고 후처리로 만들었다.
    // 반투명(VFX, 공격 예고, 물, 구름)은 이 뒤에 그려지므로 영향을 받지 않는다. 값은 ToonLookVolume에서 읽는다
    public sealed class ToonLightingRendererFeature : ScriptableRendererFeature
    {
        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("SH_ToonLightingPost 셰이더. 빌드에 들어가도록 여기서 참조한다")]
        [SerializeField] private Shader _shader;

        private Material         _material;
        private ToonLightingPass _pass;

        /****************************************
        *            Public Methods
        ****************************************/

        public override void Create()
        {
            _pass = new ToonLightingPass();
            _pass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType != CameraType.Game && cameraType != CameraType.SceneView)
            {
                return;
            }

            if (_shader == null)
            {
                return;
            }

            if (_material == null)
            {
                _material = CoreUtils.CreateEngineMaterial(_shader);
            }

            ToonLookVolume volume = VolumeManager.instance.stack.GetComponent<ToonLookVolume>();
            if (volume == null || !volume.IsActive())
            {
                return;
            }

            _pass.Setup(_material, volume);
            renderer.EnqueuePass(_pass);
        }

        /// <summary> 환경광 SH의 방향과 상관없는 평균(L0). 6개 축 방향 값의 평균은 L1, L2 항이 상쇄되어 L0와 같다 </summary>
        public static Color ComputeAmbientL0(SphericalHarmonicsL2 probe)
        {
            probe.Evaluate(ToonLightingPass.G_AxisDirections, ToonLightingPass.G_AxisResults);

            Color sum = Color.black;
            for (int i = 0; i < ToonLightingPass.G_AxisResults.Length; i++)
            {
                sum += ToonLightingPass.G_AxisResults[i];
            }

            Color average = sum / ToonLightingPass.G_AxisResults.Length;
            average.a = 1f;
            return average;
        }

        /****************************************
        *           Protected Methods
        ****************************************/

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        /****************************************
        *               Pass
        ****************************************/

        private sealed class ToonLightingPass : ScriptableRenderPass
        {
            private const string PASS_NAME   = "ToonLightingPost";
            private const int    SHADER_PASS = 0;

            private const float FOG_MODE_NONE   = 0f;
            private const float FOG_MODE_LINEAR = 1f;
            private const float FOG_MODE_EXP    = 2f;
            private const float FOG_MODE_EXP2   = 3f;

            internal static readonly Vector3[] G_AxisDirections =
            {
                Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
            };
            internal static readonly Color[] G_AxisResults = new Color[6];

            private static readonly int G_LightingParamsId = Shader.PropertyToID("_ToonLightingParams");
            private static readonly int G_RatioParamsId    = Shader.PropertyToID("_ToonRatioParams");
            private static readonly int G_FadeParamsId     = Shader.PropertyToID("_ToonFadeParams");
            private static readonly int G_ShadowColorId    = Shader.PropertyToID("_ToonShadowColor");
            private static readonly int G_AmbientL0Id      = Shader.PropertyToID("_ToonAmbientL0");
            private static readonly int G_EdgeParamsId     = Shader.PropertyToID("_ToonEdgeParams");
            private static readonly int G_LineTintId       = Shader.PropertyToID("_ToonLineTint");
            private static readonly int G_FogParamsId      = Shader.PropertyToID("_ToonFogParams");

            private static readonly Vector4 G_FullScaleBias = new Vector4(1f, 1f, 0f, 0f);

            private Material _material;

            private sealed class PassData
            {
                public TextureHandle Source;
                public Material      Material;
            }

            public ToonLightingPass()
            {
                // 카메라 색을 텍스처로 읽어야 하므로 백버퍼가 아닌 중간 텍스처가 필요하다
                requiresIntermediateTexture = true;
            }

            public void Setup(Material material, ToonLookVolume volume)
            {
                _material = material;

                _material.SetVector(G_LightingParamsId, new Vector4(volume.LightingIntensity, volume.RampSteps, volume.RampEdgeWidth, volume.AmbientKeep));
                _material.SetVector(G_RatioParamsId, new Vector4(volume.RatioMin, Mathf.Max(volume.RatioMin, volume.RatioMax), volume.HighlightKeepStart, volume.HighlightKeepRange));
                _material.SetVector(G_FadeParamsId, new Vector4(volume.FadeStart, volume.FadeRange, volume.NormalEdgeFadeRange, 0f));
                _material.SetColor(G_ShadowColorId, volume.ShadowColor);
                _material.SetColor(G_AmbientL0Id, ComputeAmbientL0(RenderSettings.ambientProbe));
                _material.SetVector(G_EdgeParamsId, new Vector4(volume.EdgeIntensity, volume.NormalEdge, volume.DepthEdgeThreshold, volume.LineDarken));
                _material.SetColor(G_LineTintId, volume.LineTint);
                _material.SetVector(G_FogParamsId, BuildFogParams());
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer)
                {
                    return;
                }

                TextureHandle source = resourceData.activeColorTexture;
                TextureHandle depth  = resourceData.cameraDepthTexture;
                TextureHandle normal = resourceData.cameraNormalsTexture;
                if (!source.IsValid() || !depth.IsValid() || !normal.IsValid())
                {
                    return;
                }

                TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
                destinationDesc.name        = PASS_NAME;
                destinationDesc.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

                using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(PASS_NAME, out PassData passData))
                {
                    passData.Source   = source;
                    passData.Material = _material;

                    builder.UseTexture(source);
                    builder.UseTexture(depth);
                    builder.UseTexture(normal);
                    if (resourceData.mainShadowsTexture.IsValid())
                    {
                        builder.UseTexture(resourceData.mainShadowsTexture);
                    }

                    // 깊이, 노멀, 그림자 텍스처를 셰이더가 전역 이름으로 읽는다
                    builder.UseAllGlobalTextures(true);
                    builder.SetRenderAttachment(destination, 0);
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    {
                        Blitter.BlitTexture(context.cmd, data.Source, G_FullScaleBias, data.Material, SHADER_PASS);
                    });
                }

                resourceData.cameraColor = destination;
            }

            private static Vector4 BuildFogParams()
            {
                if (!RenderSettings.fog)
                {
                    return new Vector4(FOG_MODE_NONE, 0f, 0f, 0f);
                }

                switch (RenderSettings.fogMode)
                {
                    case FogMode.Linear:
                        return new Vector4(FOG_MODE_LINEAR, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance, 0f);
                    case FogMode.Exponential:
                        return new Vector4(FOG_MODE_EXP, 0f, 0f, RenderSettings.fogDensity);
                    default:
                        return new Vector4(FOG_MODE_EXP2, 0f, 0f, RenderSettings.fogDensity);
                }
            }
        }
    }
}
