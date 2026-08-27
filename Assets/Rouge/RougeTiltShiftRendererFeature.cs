using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

public sealed class RougeTiltShiftRendererFeature : ScriptableRendererFeature
{
    [SerializeField] private Shader shader;

    private Material _material;
    private TiltShiftPass _pass;

    public override void Create()
    {
        CoreUtils.Destroy(_material);
        if (shader == null) shader = Shader.Find("Hidden/Rouge/TiltShift");
        if (shader == null) return;

        _material = CoreUtils.CreateEngineMaterial(shader);
        _pass = new TiltShiftPass(_material)
        {
            // Blur the already-graded frame so Bloom cannot spread the defocused
            // neon a second time and turn the view into a luminous haze.
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass == null || !RougeTiltShiftCamera.IsColorPassActive) return;
        if (renderingData.cameraData.cameraType != CameraType.Game) return;
        if (renderingData.cameraData.renderType != CameraRenderType.Base) return;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
        _pass = null;
        CoreUtils.Destroy(_material);
        _material = null;
    }

    private sealed class TiltShiftPass : ScriptableRenderPass
    {
        private static readonly int BlurredTextureId = Shader.PropertyToID("_RougeTiltShiftBlurTexture");
        private static readonly int VerticalBlurScaleId = Shader.PropertyToID("_RougeTiltShiftVerticalScale");

        private readonly Material _material;
        private readonly ProfilingSampler _profilingSampler = new ProfilingSampler("Rouge Tilt Shift");
        private RTHandle _horizontalBlur;
        private RTHandle _verticalBlur;
        private RTHandle _compositeColor;

        private sealed class CompositePassData
        {
            public TextureHandle source;
            public TextureHandle blurred;
            public Material material;
            public bool useBlur;
        }

        public TiltShiftPass(Material material)
        {
            _material = material;
            ConfigureInput(ScriptableRenderPassInput.Color);
            requiresIntermediateTexture = true;
        }

#pragma warning disable 618, 672
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            RenderTextureDescriptor blurDescriptor = descriptor;
            if (RougeTiltShiftCamera.IsEffectActive)
            {
                int downsample = RougeVisualQualityManager.TiltShiftDownsample;
                blurDescriptor.width = Mathf.Max(1, blurDescriptor.width / downsample);
                blurDescriptor.height = Mathf.Max(1, blurDescriptor.height / downsample);
                RenderingUtils.ReAllocateHandleIfNeeded(ref _horizontalBlur, blurDescriptor,
                    FilterMode.Bilinear, TextureWrapMode.Clamp,
                    name: "_RougeTiltShiftHorizontalBlur");
                RenderingUtils.ReAllocateHandleIfNeeded(ref _verticalBlur, blurDescriptor,
                    FilterMode.Bilinear, TextureWrapMode.Clamp,
                    name: "_RougeTiltShiftVerticalBlur");
            }
            RenderingUtils.ReAllocateHandleIfNeeded(ref _compositeColor, descriptor,
                FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_RougeTiltShiftCompositeColor");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null || _compositeColor == null) return;

            RTHandle cameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, _profilingSampler))
            {
                if (RougeTiltShiftCamera.IsEffectActive &&
                    _horizontalBlur != null && _verticalBlur != null)
                {
                    _material.SetFloat(VerticalBlurScaleId,
                        1f / RougeVisualQualityManager.TiltShiftDownsample);
                    Blitter.BlitCameraTexture(cmd, cameraColor, _horizontalBlur, _material, 0);
                    Blitter.BlitCameraTexture(cmd, _horizontalBlur, _verticalBlur, _material, 1);
                    _material.SetTexture(BlurredTextureId, _verticalBlur.rt);
                }
                Blitter.BlitCameraTexture(cmd, cameraColor, _compositeColor, _material, 2);
                Blitter.BlitCameraTexture(cmd, _compositeColor, cameraColor);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
#pragma warning restore 618, 672

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null) return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer)
            {
                Debug.LogWarning("Rouge Tilt Shift skipped because the active camera color is the back buffer.");
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;
            TextureDesc sourceDescriptor = renderGraph.GetTextureDesc(source);
            sourceDescriptor.clearBuffer = false;

            bool useBlur = RougeTiltShiftCamera.IsEffectActive;
            TextureHandle verticalBlur = default;
            if (useBlur)
            {
                TextureDesc blurDescriptor = sourceDescriptor;
                int downsample = RougeVisualQualityManager.TiltShiftDownsample;
                blurDescriptor.width = Mathf.Max(1, blurDescriptor.width / downsample);
                blurDescriptor.height = Mathf.Max(1, blurDescriptor.height / downsample);
                blurDescriptor.filterMode = FilterMode.Bilinear;
                _material.SetFloat(VerticalBlurScaleId, 1f / downsample);

                blurDescriptor.name = "_RougeTiltShiftHorizontalBlur";
                TextureHandle horizontalBlur = renderGraph.CreateTexture(blurDescriptor);
                RenderGraphUtils.BlitMaterialParameters horizontalParameters =
                    new RenderGraphUtils.BlitMaterialParameters(
                        source, horizontalBlur, _material, 0);
                renderGraph.AddBlitPass(horizontalParameters,
                    "Rouge Tilt Shift - Gaussian Horizontal");

                blurDescriptor.name = "_RougeTiltShiftVerticalBlur";
                verticalBlur = renderGraph.CreateTexture(blurDescriptor);
                RenderGraphUtils.BlitMaterialParameters verticalParameters =
                    new RenderGraphUtils.BlitMaterialParameters(
                        horizontalBlur, verticalBlur, _material, 1);
                renderGraph.AddBlitPass(verticalParameters,
                    "Rouge Tilt Shift - Gaussian Vertical");
            }

            sourceDescriptor.name = "_RougeTiltShiftCompositeColor";
            TextureHandle destination = renderGraph.CreateTexture(sourceDescriptor);
            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<CompositePassData>(
                       "Rouge Tilt Shift - Composite", out CompositePassData passData, _profilingSampler))
            {
                passData.source = source;
                passData.blurred = verticalBlur;
                passData.material = _material;
                passData.useBlur = useBlur;

                builder.UseTexture(passData.source, AccessFlags.Read);
                if (useBlur) builder.UseTexture(passData.blurred, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
                {
                    if (data.useBlur)
                        data.material.SetTexture(BlurredTextureId, data.blurred);
                    Blitter.BlitTexture(context.cmd, data.source, Vector2.one, data.material, 2);
                });
            }

            resourceData.cameraColor = destination;
        }

        public void Dispose()
        {
            _horizontalBlur?.Release();
            _horizontalBlur = null;
            _verticalBlur?.Release();
            _verticalBlur = null;
            _compositeColor?.Release();
            _compositeColor = null;
        }
    }
}
