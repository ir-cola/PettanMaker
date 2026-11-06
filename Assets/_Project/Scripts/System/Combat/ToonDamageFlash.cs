/**===========================================================================

 File   : ダメージ時トゥーンリムフラッシュ [ToonDamageFlash.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 被弾時に対象レンダラーの Custom/Toon マテリアルのリム発光を
 MaterialPropertyBlock で一瞬強め、カーブに沿って元へ戻す演出。
 ===========================================================================**/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ToonDamageFlash : MonoBehaviour
{
    private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
    private static readonly int RimIntensityId = Shader.PropertyToID("_RimIntensity");
    private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
    private static readonly int RimShadowMaskId = Shader.PropertyToID("_RimShadowMask");

    public enum TargetMode
    {
        AutoCollectChildren,
        ManualRenderers,
        AutoAndManual
    }

    [Header("Target")]
    [SerializeField] private TargetMode targetMode = TargetMode.AutoCollectChildren;
    [SerializeField] private Renderer[] targetRenderers;
    [SerializeField] private bool includeInactiveChildren = true;
    [SerializeField] private bool refreshTargetsBeforeFlash;
    [SerializeField] private string[] toonShaderNames = { "Custom/Toon", "Custom/FaceToon" };

    [Header("Flash")]
    [SerializeField, ColorUsage(false, true)] private Color flashRimColor = new Color(3f, 0.05f, 0.02f, 1f);
    [SerializeField, Min(0f)] private float flashRimIntensity = 12f;
    [SerializeField, Min(0.01f)] private float flashRimPower = 1.25f;
    [SerializeField, Range(0f, 1f)] private float flashRimShadowMask = 0f;
    [SerializeField, Min(0f)] private float duration = 0.14f;
    [SerializeField] private AnimationCurve flashCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

    private readonly List<MaterialSlot> materialSlots = new List<MaterialSlot>();
    private Coroutine flashCoroutine;

    private void Awake()
    {
        RebuildTargets();
    }

    private void OnDisable()
    {
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
        }

        RestoreOriginals();
    }

    public void Flash()
    {
        if (refreshTargetsBeforeFlash || materialSlots.Count == 0)
            RebuildTargets();

        if (materialSlots.Count == 0)
            return;

        if (flashCoroutine != null)
            StopCoroutine(flashCoroutine);

        flashCoroutine = StartCoroutine(FlashRoutine());
    }

    [ContextMenu("Rebuild Targets")]
    public void RebuildTargets()
    {
        RestoreOriginals();
        materialSlots.Clear();

        var renderers = new HashSet<Renderer>();

        if (targetMode == TargetMode.AutoCollectChildren || targetMode == TargetMode.AutoAndManual)
        {
            foreach (var childRenderer in GetComponentsInChildren<Renderer>(includeInactiveChildren))
            {
                if (childRenderer != null)
                    renderers.Add(childRenderer);
            }
        }

        if (targetMode == TargetMode.ManualRenderers || targetMode == TargetMode.AutoAndManual)
        {
            if (targetRenderers != null)
            {
                foreach (var targetRenderer in targetRenderers)
                {
                    if (targetRenderer != null)
                        renderers.Add(targetRenderer);
                }
            }
        }

        foreach (var targetRenderer in renderers)
        {
            var sharedMaterials = targetRenderer.sharedMaterials;

            // マテリアルが1枚だけなら Renderer 全体のブロックに合成する。
            // インデックス指定のブロックは Renderer 全体のブロックを置き換えてしまい、
            // ToonFaceTextureController が書いた _FaceTex/_FaceBlend が失われる。
            bool rendererWide = sharedMaterials.Length == 1;

            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                var material = sharedMaterials[i];
                if (!IsSupportedToonMaterial(material))
                    continue;

                materialSlots.Add(new MaterialSlot(
                    targetRenderer,
                    i,
                    rendererWide,
                    material.GetColor(RimColorId),
                    material.GetFloat(RimIntensityId),
                    material.GetFloat(RimPowerId),
                    material.GetFloat(RimShadowMaskId)));
            }
        }
    }

    private bool IsSupportedToonMaterial(Material material)
    {
        return material != null
            && material.shader != null
            && MatchesAnyToonShader(material.shader.name)
            && material.HasProperty(RimColorId)
            && material.HasProperty(RimIntensityId)
            && material.HasProperty(RimPowerId)
            && material.HasProperty(RimShadowMaskId);
    }

    private bool MatchesAnyToonShader(string shaderName)
    {
        if (toonShaderNames == null)
            return false;

        for (int i = 0; i < toonShaderNames.Length; i++)
        {
            if (toonShaderNames[i] == shaderName)
                return true;
        }

        return false;
    }

    private IEnumerator FlashRoutine()
    {
        if (duration <= 0f)
        {
            ApplyFlash(1f);
            yield return null;
            RestoreOriginals();
            flashCoroutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            float weight = flashCurve != null ? flashCurve.Evaluate(normalizedTime) : 1f - normalizedTime;
            ApplyFlash(Mathf.Clamp01(weight));

            elapsed += Time.deltaTime;
            yield return null;
        }

        RestoreOriginals();
        flashCoroutine = null;
    }

    private void ApplyFlash(float weight)
    {
        foreach (var slot in materialSlots)
        {
            if (!slot.IsValid)
                continue;

            slot.GetPropertyBlock();
            slot.PropertyBlock.SetColor(RimColorId, Color.Lerp(slot.OriginalRimColor, flashRimColor, weight));
            slot.PropertyBlock.SetFloat(RimIntensityId, Mathf.Lerp(slot.OriginalRimIntensity, flashRimIntensity, weight));
            slot.PropertyBlock.SetFloat(RimPowerId, Mathf.Lerp(slot.OriginalRimPower, flashRimPower, weight));
            slot.PropertyBlock.SetFloat(RimShadowMaskId, Mathf.Lerp(slot.OriginalRimShadowMask, flashRimShadowMask, weight));
            slot.SetPropertyBlock();
        }
    }

    private void RestoreOriginals()
    {
        foreach (var slot in materialSlots)
        {
            if (!slot.IsValid)
                continue;

            slot.GetPropertyBlock();
            slot.PropertyBlock.SetColor(RimColorId, slot.OriginalRimColor);
            slot.PropertyBlock.SetFloat(RimIntensityId, slot.OriginalRimIntensity);
            slot.PropertyBlock.SetFloat(RimPowerId, slot.OriginalRimPower);
            slot.PropertyBlock.SetFloat(RimShadowMaskId, slot.OriginalRimShadowMask);
            slot.SetPropertyBlock();
        }
    }

    private sealed class MaterialSlot
    {
        public readonly Renderer Renderer;
        public readonly int MaterialIndex;
        public readonly bool RendererWide;
        public readonly Color OriginalRimColor;
        public readonly float OriginalRimIntensity;
        public readonly float OriginalRimPower;
        public readonly float OriginalRimShadowMask;
        public readonly MaterialPropertyBlock PropertyBlock = new MaterialPropertyBlock();

        public bool IsValid => Renderer != null;

        public MaterialSlot(
            Renderer renderer,
            int materialIndex,
            bool rendererWide,
            Color originalRimColor,
            float originalRimIntensity,
            float originalRimPower,
            float originalRimShadowMask)
        {
            Renderer = renderer;
            MaterialIndex = materialIndex;
            RendererWide = rendererWide;
            OriginalRimColor = originalRimColor;
            OriginalRimIntensity = originalRimIntensity;
            OriginalRimPower = originalRimPower;
            OriginalRimShadowMask = originalRimShadowMask;
        }

        public void GetPropertyBlock()
        {
            if (RendererWide) Renderer.GetPropertyBlock(PropertyBlock);
            else Renderer.GetPropertyBlock(PropertyBlock, MaterialIndex);
        }

        public void SetPropertyBlock()
        {
            if (RendererWide) Renderer.SetPropertyBlock(PropertyBlock);
            else Renderer.SetPropertyBlock(PropertyBlock, MaterialIndex);
        }
    }
}
