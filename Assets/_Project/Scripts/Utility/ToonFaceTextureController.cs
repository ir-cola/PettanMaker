/**===========================================================================

 File   : トゥーン顔テクスチャ制御 [ToonFaceTextureController.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 MaterialPropertyBlock でトゥーンシェーダーの顔テクスチャを切り替える。
 即時切り替えと、次の顔へブレンドする時間遷移の両方をサポートする。
 ===========================================================================**/
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class ToonFaceTextureController : MonoBehaviour
{
    private static readonly int FaceTexId = Shader.PropertyToID("_FaceTex");
    private static readonly int FaceNextTexId = Shader.PropertyToID("_FaceNextTex");
    private static readonly int FaceBlendId = Shader.PropertyToID("_FaceBlend");
    private static readonly int FaceTransitionId = Shader.PropertyToID("_FaceTransition");

    [SerializeField] private List<Renderer> targetRenderers = new();
    [SerializeField] private List<Texture> faceTextures = new();
    [SerializeField, Min(0)] private int initialFaceIndex;
    [SerializeField, Range(0f, 1f)] private float faceBlend = 1f;

    private MaterialPropertyBlock propertyBlock;
    private Texture currentFaceTexture;
    private Coroutine transitionCoroutine;

    private void Awake()
    {
        EnsurePropertyBlock();
        EnsureTargetRenderers();
        currentFaceTexture = GetFaceTexture(initialFaceIndex);
        ApplyImmediate(currentFaceTexture);
    }

    private void OnValidate()
    {
        if (!gameObject.scene.IsValid()) return;

        EnsurePropertyBlock();
        EnsureTargetRenderers();
        currentFaceTexture = GetFaceTexture(initialFaceIndex);
        ApplyImmediate(currentFaceTexture);
    }

    private void EnsurePropertyBlock()
    {
        propertyBlock ??= new MaterialPropertyBlock();
    }

    private void EnsureTargetRenderers()
    {
        if (targetRenderers == null) targetRenderers = new List<Renderer>();
        if (targetRenderers.Count == 0) targetRenderers.AddRange(GetComponentsInChildren<Renderer>());
    }

    public int FaceTextureCount => faceTextures.Count;

    public Texture GetFaceTexture(int index)
    {
        if (index < 0 || index >= faceTextures.Count)
        {
            Debug.LogWarning($"[{name}] faceTextures[{index}] は範囲外です（登録数: {faceTextures.Count}）。顔テクスチャは変更されません。", this);
            return null;
        }
        return faceTextures[index];
    }

    public void AddFaceTexture(Texture faceTexture)
    {
        if (faceTexture == null || faceTextures.Contains(faceTexture)) return;
        faceTextures.Add(faceTexture);
    }

    public bool RemoveFaceTexture(Texture faceTexture)
    {
        return faceTextures.Remove(faceTexture);
    }

    public void SetFaceTexture(int index)
    {
        var faceTexture = GetFaceTexture(index);
        if (faceTexture == null) return;
        SetFaceTexture(faceTexture);
    }

    public void SetFaceTexture(Texture faceTexture)
    {
        // 遷移中に即時切り替えが来た場合、遷移を止めないと _FaceTransition が
        // 走り続けて即時指定した顔を上書きしてしまう。
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        currentFaceTexture = faceTexture;
        ApplyImmediate(currentFaceTexture);
    }

    public void TransitionFaceTexture(int index, float duration)
    {
        var faceTexture = GetFaceTexture(index);
        if (faceTexture == null) return;
        TransitionFaceTexture(faceTexture, duration);
    }

    public void TransitionFaceTexture(Texture nextFaceTexture, float duration)
    {
        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = StartCoroutine(TransitionRoutine(nextFaceTexture, duration));
    }

    private IEnumerator TransitionRoutine(Texture nextFaceTexture, float duration)
    {
        if (targetRenderers == null || targetRenderers.Count == 0)
        {
            transitionCoroutine = null;
            yield break;
        }

        foreach (var renderer in targetRenderers)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetTexture(FaceTexId, currentFaceTexture);
            propertyBlock.SetTexture(FaceNextTexId, nextFaceTexture);
            propertyBlock.SetFloat(FaceBlendId, faceBlend);
            propertyBlock.SetFloat(FaceTransitionId, 0f);
            renderer.SetPropertyBlock(propertyBlock);
        }

        if (duration <= 0f)
        {
            currentFaceTexture = nextFaceTexture;
            ApplyImmediate(currentFaceTexture);
            transitionCoroutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float transition = Mathf.Clamp01(elapsed / duration);
            foreach (var renderer in targetRenderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat(FaceTransitionId, transition);
                renderer.SetPropertyBlock(propertyBlock);
            }
            yield return null;
        }

        currentFaceTexture = nextFaceTexture;
        ApplyImmediate(currentFaceTexture);
        transitionCoroutine = null;
    }

    private void ApplyImmediate(Texture faceTexture)
    {
        if (targetRenderers == null) return;

        foreach (var renderer in targetRenderers)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(propertyBlock);
            if (faceTexture != null)
            {
                propertyBlock.SetTexture(FaceTexId, faceTexture);
                propertyBlock.SetTexture(FaceNextTexId, faceTexture);
            }
            propertyBlock.SetFloat(FaceBlendId, faceTexture == null ? 0f : faceBlend);
            propertyBlock.SetFloat(FaceTransitionId, 0f);
            renderer.SetPropertyBlock(propertyBlock);
        }
    }
}
