using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

// URP の ScriptableRendererFeature として動作するポストプロセス管理クラス。
// 複数のポストプロセスマテリアルを順番に Blit し、最後に元のカラーバッファへ書き戻す。
// モーションブラー等のために前フレームのビュープロジェクション行列もグローバルに配信する。
public class PostProcessFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        public Material[] materials; // 適用するポストプロセスマテリアルの配列（上から順番に適用）
    }

    // カメラごとに前フレームの VP 行列を保持する履歴データ
    private sealed class CameraHistory
    {
        public Camera    camera;
        public Matrix4x4 previousViewProjection;
        public int       lastFrameSeen;
    }

    // 複数カメラに対応するため、カメラの InstanceID をキーにして履歴を管理する
    private readonly Dictionary<int, CameraHistory> cameraHistoryById = new();

    private static readonly int PreviousViewProjectionId = Shader.PropertyToID("_PostProcessPreviousViewProjection");
    private static readonly int LightScreenPositionId    = Shader.PropertyToID("_LightScreenPos");
    private static readonly int LightDirectionId         = Shader.PropertyToID("_LightDirection");
    private static readonly int CameraDepthTextureId     = Shader.PropertyToID("_CameraDepthTexture");

    // 太陽光のスクリーン座標と方向をグローバルおよび各マテリアルに設定する
    private void ApplyLightData(Vector4 lightScreenPosition, Vector4 lightDirection)
    {
        Shader.SetGlobalVector(LightScreenPositionId, lightScreenPosition);
        Shader.SetGlobalVector(LightDirectionId, lightDirection);

        if (settings.materials == null)
            return;

        for (int i = 0; i < settings.materials.Length; i++)
        {
            Material material = settings.materials[i];
            if (material != null)
            {
                material.SetVector(LightScreenPositionId, lightScreenPosition);
                material.SetVector(LightDirectionId, lightDirection);
            }
        }
    }

    // Global Volume の HeightFogVolume を Fog マテリアルへ反映する（高さ/遠景フォグを一元管理）
    private void ApplyFogVolume()
    {
        if (settings.materials == null)
            return;

        VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
        HeightFogVolume fog = stack != null ? stack.GetComponent<HeightFogVolume>() : null;
        if (fog == null)
            return;
        // 常に現在の Volume 値を反映する。Volume を置いていないシーンでは
        // strength 既定 0 が流し込まれるのでフォグは出ない（Stage_1 のみ有効）。

        for (int i = 0; i < settings.materials.Length; i++)
        {
            Material material = settings.materials[i];
            if (material == null || !material.HasProperty("_HeightFogBase"))
                continue;

            material.SetColor("_FogColor", fog.fogColor.value);
            material.SetFloat("_HeightFogBase", fog.heightBase.value);
            material.SetFloat("_HeightFogFalloff", fog.heightFalloff.value);
            material.SetFloat("_HeightFogStrength", fog.heightStrength.value);
            material.SetFloat("_FogStart", fog.distanceStart.value);
            material.SetFloat("_FogEnd", fog.distanceEnd.value);
            material.SetFloat("_FogStrength", fog.distanceStrength.value);
            material.SetFloat("_VolumetricScattering", fog.volumetricScattering.value);
            material.SetFloat("_VolumetricAnisotropy", fog.volumetricAnisotropy.value);
        }
    }

    // Cheshire 用の絵画調 Volume を AnimeStylize マテリアルへ反映する。
    // Volume がないシーンでは intensity の既定値 0 により、元映像をそのまま返す。
    private void ApplyAlicePaintingVolume()
    {
        if (settings.materials == null)
            return;

        VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
        AlicePaintingVolume painting = stack != null ? stack.GetComponent<AlicePaintingVolume>() : null;
        if (painting == null)
            return;

        for (int i = 0; i < settings.materials.Length; i++)
        {
            Material material = settings.materials[i];
            if (material == null || !material.HasProperty("_AlicePaintingIntensity"))
                continue;

            material.SetFloat("_AlicePaintingIntensity", painting.intensity.value);
            material.SetFloat("_BrushSize", painting.brushSize.value);
            material.SetFloat("_BrushStrength", painting.brushStrength.value);
            material.SetFloat("_Wobble", painting.wobble.value);
            material.SetFloat("_EdgeStrength", painting.edgeStrength.value);
            material.SetFloat("_EdgeThreshold", painting.edgeThreshold.value);
            material.SetFloat("_PaperGrain", painting.paperGrain.value);
            material.SetFloat("_PosterizeSteps", painting.posterizeSteps.value);
            material.SetFloat("_Saturation", painting.saturation.value);
            material.SetFloat("_Contrast", painting.contrast.value);
            material.SetColor("_ShadowTint", painting.shadowTint.value);
            material.SetColor("_MidTint", painting.midTint.value);
            material.SetColor("_HighlightTint", painting.highlightTint.value);
        }
    }

    // 「鏡の国」風の屈折・プリズム設定を LookingGlassDream マテリアルへ反映する。
    private void ApplyLookingGlassVolume()
    {
        if (settings.materials == null)
            return;

        VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
        LookingGlassVolume lookingGlass = stack != null ? stack.GetComponent<LookingGlassVolume>() : null;
        if (lookingGlass == null)
            return;

        for (int i = 0; i < settings.materials.Length; i++)
        {
            Material material = settings.materials[i];
            if (material == null || !material.HasProperty("_LookingGlassIntensity"))
                continue;

            material.SetFloat("_LookingGlassIntensity", lookingGlass.intensity.value);
            material.SetFloat("_Refraction", lookingGlass.refraction.value);
            material.SetFloat("_Swirl", lookingGlass.swirl.value);
            material.SetFloat("_DriftSpeed", lookingGlass.driftSpeed.value);
            material.SetFloat("_ReflectionEcho", lookingGlass.reflectionEcho.value);
            material.SetFloat("_ChromaticShift", lookingGlass.chromaticShift.value);
            material.SetFloat("_SoftFocus", lookingGlass.softFocus.value);
            material.SetFloat("_PrismStrength", lookingGlass.prismStrength.value);
            material.SetFloat("_DreamVignette", lookingGlass.vignette.value);
            material.SetColor("_CoolTint", lookingGlass.coolTint.value);
            material.SetColor("_WarmTint", lookingGlass.warmTint.value);
        }
    }

    // 古い挿絵・版画風の設定を StorybookPrint マテリアルへ反映する。
    private void ApplyStorybookPrintVolume()
    {
        if (settings.materials == null)
            return;

        VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
        StorybookPrintVolume storybook = stack != null ? stack.GetComponent<StorybookPrintVolume>() : null;
        if (storybook == null)
            return;

        for (int i = 0; i < settings.materials.Length; i++)
        {
            Material material = settings.materials[i];
            if (material == null || !material.HasProperty("_StorybookIntensity"))
                continue;

            material.SetFloat("_StorybookIntensity", storybook.intensity.value);
            material.SetFloat("_ToneSteps", storybook.toneSteps.value);
            material.SetFloat("_ColorRetention", storybook.colorRetention.value);
            material.SetFloat("_InkStrength", storybook.inkStrength.value);
            material.SetFloat("_EdgeThreshold", storybook.edgeThreshold.value);
            material.SetFloat("_HatchStrength", storybook.hatchStrength.value);
            material.SetFloat("_PrintOffset", storybook.printOffset.value);
            material.SetFloat("_PaperGrain", storybook.paperGrain.value);
            material.SetFloat("_CrimsonAccent", storybook.crimsonAccent.value);
            material.SetColor("_InkColor", storybook.inkColor.value);
            material.SetColor("_PaperColor", storybook.paperColor.value);
            material.SetColor("_ShadowColor", storybook.shadowColor.value);
            material.SetColor("_CrimsonColor", storybook.crimsonColor.value);
        }
    }

    // AAA トゥーン向けの面整理・輪郭・シネマティック階調を反映する。
    private void ApplyCinematicToonVolume()
    {
        if (settings.materials == null)
            return;

        VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
        CinematicToonVolume toon = stack != null ? stack.GetComponent<CinematicToonVolume>() : null;
        if (toon == null)
            return;

        for (int i = 0; i < settings.materials.Length; i++)
        {
            Material material = settings.materials[i];
            if (material == null || !material.HasProperty("_CinematicToonIntensity"))
                continue;

            material.SetFloat("_CinematicToonIntensity", toon.intensity.value);
            material.SetFloat("_SurfaceSmoothing", toon.surfaceSmoothing.value);
            material.SetFloat("_SmoothingRadius", toon.smoothingRadius.value);
            material.SetFloat("_ToneBands", toon.toneBands.value);
            material.SetFloat("_BandSoftness", toon.bandSoftness.value);
            material.SetFloat("_OutlineStrength", toon.outlineStrength.value);
            material.SetFloat("_DepthSensitivity", toon.depthSensitivity.value);
            material.SetFloat("_ColorSensitivity", toon.colorSensitivity.value);
            material.SetFloat("_OutlineWidth", toon.outlineWidth.value);
            material.SetFloat("_LocalContrast", toon.localContrast.value);
            material.SetFloat("_HighlightGlow", toon.highlightGlow.value);
            material.SetFloat("_GlowThreshold", toon.glowThreshold.value);
            material.SetFloat("_ToonSaturation", toon.saturation.value);
            material.SetColor("_ToonOutlineColor", toon.outlineColor.value);
            material.SetColor("_ToonShadowTint", toon.shadowTint.value);
            material.SetColor("_ToonHighlightTint", toon.highlightTint.value);
        }
    }

    // Ver4のHalftone / Watercolor / Heat Haze / LUT / Contact AO / Blurを反映する。
    private void ApplyAAAStylizedEffectsVolume()
    {
        if (settings.materials == null)
            return;

        VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
        AAAStylizedEffectsVolume effects = stack != null ? stack.GetComponent<AAAStylizedEffectsVolume>() : null;
        if (effects == null)
            return;

        for (int i = 0; i < settings.materials.Length; i++)
        {
            Material material = settings.materials[i];
            if (material == null || !material.HasProperty("_AAAEffectsIntensity"))
                continue;

            material.SetFloat("_AAAEffectsIntensity", effects.intensity.value);
            material.SetFloat("_HalftoneStrength", effects.halftoneStrength.value);
            material.SetFloat("_HalftoneScale", effects.halftoneScale.value);
            material.SetColor("_HalftoneColor", effects.halftoneColor.value);
            material.SetFloat("_WatercolorStrength", effects.watercolorStrength.value);
            material.SetFloat("_BleedRadius", effects.bleedRadius.value);
            material.SetFloat("_PigmentVariation", effects.pigmentVariation.value);
            material.SetFloat("_HeatHazeStrength", effects.heatHazeStrength.value);
            material.SetFloat("_HeatHazeScale", effects.heatHazeScale.value);
            material.SetFloat("_HeatHazeSpeed", effects.heatHazeSpeed.value);
            material.SetFloat("_HeatHazeMask", effects.heatHazeMask.value);
            material.SetFloat("_LookupStrength", effects.lookupStrength.value);
            material.SetFloat("_LookupSize", effects.lookupSize.value);
            material.SetColor("_LookupShadow", effects.lookupShadow.value);
            material.SetColor("_LookupMid", effects.lookupMid.value);
            material.SetColor("_LookupHighlight", effects.lookupHighlight.value);

            Texture lookupTexture = effects.lookupTexture.value;
            material.SetFloat("_UseLookupTex", lookupTexture != null ? 1f : 0f);
            if (lookupTexture != null)
                material.SetTexture("_LookupTex", lookupTexture);

            material.SetFloat("_ContactShadowStrength", effects.contactShadowStrength.value);
            material.SetFloat("_ContactShadowRadius", effects.contactShadowRadius.value);
            material.SetFloat("_ContactShadowBias", effects.contactShadowBias.value);
            material.SetColor("_ContactShadowColor", effects.contactShadowColor.value);
            material.SetFloat("_BlurStrength", effects.blurStrength.value);
            material.SetFloat("_BlurRadius", effects.blurRadius.value);
            material.SetFloat("_KuwaharaStrength", effects.kuwaharaStrength.value);
            material.SetFloat("_KuwaharaRadius", effects.kuwaharaRadius.value);
        }
    }

    // 通常プレイ向けセミリアルの質感・接地感・自然色・空気遠近を反映する。
    private void ApplySemiRealVolume()
    {
        if (settings.materials == null)
            return;

        VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
        SemiRealVolume semiReal = stack != null ? stack.GetComponent<SemiRealVolume>() : null;
        if (semiReal == null)
            return;

        for (int i = 0; i < settings.materials.Length; i++)
        {
            Material material = settings.materials[i];
            if (material == null || !material.HasProperty("_SemiRealIntensity"))
                continue;

            material.SetFloat("_SemiRealIntensity", semiReal.intensity.value);
            material.SetFloat("_Clarity", semiReal.clarity.value);
            material.SetFloat("_SemiRealSharpen", semiReal.sharpen.value);
            material.SetFloat("_SurfaceSmoothing", semiReal.surfaceSmoothing.value);
            material.SetFloat("_DetailRadius", semiReal.detailRadius.value);
            material.SetFloat("_HighlightRolloff", semiReal.highlightRolloff.value);
            material.SetFloat("_ContactAO", semiReal.contactAO.value);
            material.SetFloat("_AORadius", semiReal.aoRadius.value);
            material.SetFloat("_AOBias", semiReal.aoBias.value);
            material.SetColor("_AOColor", semiReal.aoColor.value);
            material.SetFloat("_NaturalExposure", semiReal.exposure.value);
            material.SetFloat("_NaturalContrast", semiReal.contrast.value);
            material.SetFloat("_NaturalSaturation", semiReal.saturation.value);
            material.SetColor("_NaturalShadowTint", semiReal.shadowTint.value);
            material.SetColor("_NaturalHighlightTint", semiReal.highlightTint.value);
            material.SetFloat("_AerialStrength", semiReal.aerialStrength.value);
            material.SetFloat("_AerialStart", semiReal.aerialStart.value);
            material.SetFloat("_AerialEnd", semiReal.aerialEnd.value);
            material.SetColor("_AerialColor", semiReal.aerialColor.value);
            material.SetFloat("_NaturalLensDistortion", semiReal.lensDistortion.value);
            material.SetFloat("_NaturalChromaticAberration", semiReal.chromaticAberration.value);
        }
    }

    // 実際のレンダーパス。RenderGraph を使って Blit チェーンを組み立てる。
    class PostProcessPass : ScriptableRenderPass
    {
        private const string ProfilerTag = "Post Process";
        private static readonly int AlicePaintingIntensityId = Shader.PropertyToID("_AlicePaintingIntensity");
        private static readonly int LookingGlassIntensityId = Shader.PropertyToID("_LookingGlassIntensity");
        private static readonly int StorybookIntensityId = Shader.PropertyToID("_StorybookIntensity");
        private static readonly int CinematicToonIntensityId = Shader.PropertyToID("_CinematicToonIntensity");
        private static readonly int AAAEffectsIntensityId = Shader.PropertyToID("_AAAEffectsIntensity");
        private static readonly int SemiRealIntensityId = Shader.PropertyToID("_SemiRealIntensity");

        private readonly ProfilingSampler passProfilingSampler = new(ProfilerTag);
        private Material[] materials;

        private class CopyPassData
        {
            public TextureHandle source;
        }

        private class EffectPassData
        {
            public TextureHandle source;
            public TextureHandle depth;
            public Material      material;
        }

        public PostProcessPass(RenderPassEvent passEvent)
        {
            renderPassEvent            = passEvent;
            requiresIntermediateTexture = true;
        }

        public void Setup(Material[] postProcessMaterials)
        {
            materials = postProcessMaterials;
        }

        // 比較用の各バージョンは同じ Renderer Feature に登録されているため、
        // Volume 強度が 0 の専用マテリアルは Blit チェーン自体から除外する。
        private static bool IsMaterialActive(Material material)
        {
            if (material == null)
                return false;

            if (material.HasProperty(AlicePaintingIntensityId))
                return material.GetFloat(AlicePaintingIntensityId) > 0.0001f;
            if (material.HasProperty(LookingGlassIntensityId))
                return material.GetFloat(LookingGlassIntensityId) > 0.0001f;
            if (material.HasProperty(StorybookIntensityId))
                return material.GetFloat(StorybookIntensityId) > 0.0001f;
            if (material.HasProperty(CinematicToonIntensityId))
                return material.GetFloat(CinematicToonIntensityId) > 0.0001f;
            if (material.HasProperty(AAAEffectsIntensityId))
                return material.GetFloat(AAAEffectsIntensityId) > 0.0001f;
            if (material.HasProperty(SemiRealIntensityId))
                return material.GetFloat(SemiRealIntensityId) > 0.0001f;

            return true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (materials == null || materials.Length == 0)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData   cameraData   = frameData.Get<UniversalCameraData>();

            // プレビューカメラやバックバッファ直接書き込み時はスキップ
            if (cameraData.isPreviewCamera || resourceData.isActiveTargetBackBuffer)
                return;

            // null マテリアルを除いた有効数をカウント
            int validCount = 0;
            for (int i = 0; i < materials.Length; i++)
            {
                if (IsMaterialActive(materials[i]))
                    validCount++;
            }

            if (validCount == 0)
                return;

            TextureHandle sourceTexture = resourceData.activeColorTexture;

            // 深度なし・MSAA なしの一時テクスチャを2枚用意してピンポン Blit する
            var descriptor          = cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples  = 1;

            TextureHandle tempTextureA = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, descriptor, "_PostProcessTempA", false);

            TextureHandle tempTextureB = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, descriptor, "_PostProcessTempB", false);

            // まずアクティブカラーを tempA にコピーする
            using (var builder = renderGraph.AddRasterRenderPass<CopyPassData>("PostProcess Copy", out var passData, passProfilingSampler))
            {
                passData.source = sourceTexture;

                builder.UseTexture(sourceTexture, AccessFlags.Read);
                builder.SetRenderAttachment(tempTextureA, 0, AccessFlags.Write);
                builder.SetRenderFunc((CopyPassData data, RasterGraphContext context) =>
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), 0.0f, false));
            }

            // 有効なマテリアルを順番に適用し、最後だけ元のカラーバッファへ書き戻す
            bool currentIsTempA = true;
            int  materialIndex  = 0;

            for (int i = 0; i < materials.Length; i++)
            {
                Material currentMaterial = materials[i];
                if (!IsMaterialActive(currentMaterial))
                    continue;

                bool isLast             = materialIndex == validCount - 1;
                TextureHandle source      = currentIsTempA ? tempTextureA : tempTextureB;
                TextureHandle destination = isLast ? sourceTexture : (currentIsTempA ? tempTextureB : tempTextureA);

                using (var builder = renderGraph.AddRasterRenderPass<EffectPassData>($"PostProcess Apply {materialIndex}", out var passData, passProfilingSampler))
                {
                    passData.source   = source;
                    passData.material = currentMaterial;

                    // 深度テクスチャを LensFlare・GodRays 等のオクルージョン判定に渡す
                    TextureHandle depthHandle = resourceData.cameraDepthTexture;
                    if (depthHandle.IsValid())
                    {
                        passData.depth = depthHandle;
                        builder.UseTexture(depthHandle, AccessFlags.Read);
                    }

                    builder.UseTexture(source, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    // SetGlobalTexture を使うために必要
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc((EffectPassData data, RasterGraphContext context) =>
                    {
                        if (data.depth.IsValid())
                            context.cmd.SetGlobalTexture(CameraDepthTextureId, data.depth);
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                    });
                }

                if (!isLast)
                    currentIsTempA = !currentIsTempA;

                materialIndex++;
            }
        }
    }

    public Settings settings = new();

    private PostProcessPass pass;

    public override void Create()
    {
        pass = new PostProcessPass(settings.renderPassEvent);
        pass.ConfigureInput(ScriptableRenderPassInput.Depth); // 深度テクスチャが必要なエフェクト用
    }

    // 一定フレーム間隔で、長期間参照されていないカメラ履歴を削除する
    private void CleanupCameraHistory()
    {
        List<int> deadCameraIds   = null;
        int       staleThreshold  = Time.frameCount - 120; // 約2秒間更新がなければ削除

        foreach (var pair in cameraHistoryById)
        {
            CameraHistory history = pair.Value;
            if (history == null || history.camera == null || history.lastFrameSeen < staleThreshold)
            {
                deadCameraIds ??= new List<int>();
                deadCameraIds.Add(pair.Key);
            }
        }

        if (deadCameraIds == null)
            return;

        for (int i = 0; i < deadCameraIds.Count; i++)
            cameraHistoryById.Remove(deadCameraIds[i]);
    }

    // RenderSettings.sun を優先し、なければ最も強度の高い Directional Light を返す
    private static Light FindDirectionalLight()
    {
        if (RenderSettings.sun != null && RenderSettings.sun.enabled && RenderSettings.sun.gameObject.activeInHierarchy)
            return RenderSettings.sun;

        Light[] lights    = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        Light   bestLight = null;
        float   bestScore = float.NegativeInfinity;

        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light == null || !light.enabled || !light.gameObject.activeInHierarchy || light.type != LightType.Directional)
                continue;

            float score = light.intensity;
            if (score > bestScore)
            {
                bestScore = score;
                bestLight = light;
            }
        }

        return bestLight;
    }

    // 太陽光がスクリーン上のどこにあるかを XY（ビューポート座標）・Z（深度）・W（可視フラグ）で返す
    private static Vector4 GetLightScreenPosition(Camera camera, Light light)
    {
        if (camera == null || light == null)
            return new Vector4(0.5f, 0.5f, -1f, 0f);

        Vector3 lightDir  = -light.transform.forward;
        Vector3 worldPos  = camera.transform.position + lightDir * 1000f;
        Vector3 viewport  = camera.WorldToViewportPoint(worldPos);
        float   visible   = viewport.z > 0f ? 1f : 0f; // カメラの前にある場合のみ 1
        return new Vector4(viewport.x, viewport.y, viewport.z, visible);
    }

    // PostProcessDirectionalLightProvider があればそちらを優先、なければ FindDirectionalLight へフォールバック
    private static Light ResolveDirectionalLight()
    {
        Light providerLight = PostProcessDirectionalLightProvider.Current;
        if (providerLight != null && providerLight.enabled && providerLight.gameObject.activeInHierarchy && providerLight.type == LightType.Directional)
            return providerLight;

        return FindDirectionalLight();
    }

    private static Vector4 GetLightDirection(Light light)
    {
        if (light == null)
            return new Vector4(0f, -1f, 0f, 0f);

        Vector3 direction = -light.transform.forward;
        return new Vector4(direction.x, direction.y, direction.z, 0f);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.materials == null || settings.materials.Length == 0)
            return;

        Camera camera = renderingData.cameraData.camera;
        if (camera != null)
        {
            int      cameraId              = camera.GetInstanceID();
            Matrix4x4 currentViewProjection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;

            // 初回または無効になったカメラは現在の VP を「前フレーム」として初期化する
            if (!cameraHistoryById.TryGetValue(cameraId, out CameraHistory history) || history == null || history.camera == null)
            {
                history = new CameraHistory
                {
                    camera                 = camera,
                    previousViewProjection = currentViewProjection,
                    lastFrameSeen          = Time.frameCount
                };
                cameraHistoryById[cameraId] = history;
            }

            // シェーダーに前フレームの VP を渡してからバッファを更新する
            Shader.SetGlobalMatrix(PreviousViewProjectionId, history.previousViewProjection);
            history.previousViewProjection = currentViewProjection;
            history.camera                 = camera;
            history.lastFrameSeen          = Time.frameCount;

            Light directionalLight = ResolveDirectionalLight();
            ApplyLightData(GetLightScreenPosition(camera, directionalLight), GetLightDirection(directionalLight));
        }
        else
        {
            ApplyLightData(new Vector4(0.5f, 0.5f, -1f, 0f), new Vector4(0f, -1f, 0f, 0f));
        }

        // 64フレームに1回、古いカメラ履歴を掃除する
        if ((Time.frameCount & 63) == 0)
            CleanupCameraHistory();

        ApplyFogVolume();
        ApplyAlicePaintingVolume();
        ApplyLookingGlassVolume();
        ApplyStorybookPrintVolume();
        ApplyCinematicToonVolume();
        ApplyAAAStylizedEffectsVolume();
        ApplySemiRealVolume();

        pass.Setup(settings.materials);
        renderer.EnqueuePass(pass);
    }
}
