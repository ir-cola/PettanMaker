/**===========================================================================

 File   : スケルトンデバッグ表示 [DebugSkeleton.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 SkinnedMeshRenderer / Animator のボーン階層を Gizmo で可視化するデバッグ用。
 各ボーンにジョイント球と親子リンク線を描画し、任意でボーン名も表示する。
 ===========================================================================**/

using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class DebugSkeleton : MonoBehaviour
{
    [Header("表示対象")]
    [SerializeField] private Animator _animator;
    [SerializeField] private SkinnedMeshRenderer[] _skinnedMeshRenderers;

    [Header("表示設定")]
    [SerializeField] private Color _boneColor = Color.cyan;
    [SerializeField] private Color _jointColor = Color.yellow;
    [SerializeField] private float _jointRadius = 0.02f;
    [SerializeField] private bool _showBoneNames = false;
    [SerializeField] private bool _onlyShowInPlayMode = false;

    private readonly HashSet<Transform> _boneSet = new();

    private void OnValidate()
    {
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        if (_skinnedMeshRenderers == null || _skinnedMeshRenderers.Length == 0)
            _skinnedMeshRenderers = GetComponentsInChildren<SkinnedMeshRenderer>();
    }

    private void OnDrawGizmos()
    {
        if (_onlyShowInPlayMode && !Application.isPlaying) return;
        DrawSkeleton();
    }

    private void DrawSkeleton()
    {
        CollectBones();

        foreach (Transform bone in _boneSet)
        {
            if (bone == null) continue;

            Gizmos.color = _jointColor;
            Gizmos.DrawSphere(bone.position, _jointRadius);

            Gizmos.color = _boneColor;
            for (int i = 0; i < bone.childCount; i++)
            {
                Transform child = bone.GetChild(i);
                if (child == null) continue;
                Gizmos.DrawLine(bone.position, child.position);
            }

#if UNITY_EDITOR
            if (_showBoneNames)
            {
                Handles.Label(bone.position + Vector3.up * (_jointRadius * 1.5f), bone.name);
            }
#endif
        }
    }

    private void CollectBones()
    {
        _boneSet.Clear();

        if (_skinnedMeshRenderers != null)
        {
            foreach (var smr in _skinnedMeshRenderers)
            {
                if (smr == null) continue;
                foreach (Transform bone in smr.bones)
                {
                    if (bone != null) _boneSet.Add(bone);
                }
                if (smr.rootBone != null) _boneSet.Add(smr.rootBone);
            }
        }

        if (_boneSet.Count == 0 && _animator != null)
        {
            CollectBonesRecursive(_animator.transform);
        }
    }

    private void CollectBonesRecursive(Transform t)
    {
        _boneSet.Add(t);
        for (int i = 0; i < t.childCount; i++)
            CollectBonesRecursive(t.GetChild(i));
    }
}
