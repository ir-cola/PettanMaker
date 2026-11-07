using System;
using UnityEngine;
using UnityEngine.Audio;

[Serializable]
public class PlayerFootstepSurface
{
    [Tooltip("Inspector表示用の名前。")]
    public string Name = "Surface";

    [Tooltip("Collider名・Tag・PhysicMaterial名のいずれかに含まれれば、この足音を使用する。")]
    public string MatchText;

    public SoundCue[] Steps;

    public bool Matches(Collider collider)
    {
        if (collider == null || string.IsNullOrWhiteSpace(MatchText)) return false;
        StringComparison comparison = StringComparison.OrdinalIgnoreCase;

        if (collider.name.IndexOf(MatchText, comparison) >= 0) return true;
        if (collider.tag.IndexOf(MatchText, comparison) >= 0) return true;

        PhysicsMaterial material = collider.sharedMaterial;
        return material != null && material.name.IndexOf(MatchText, comparison) >= 0;
    }
}

[Serializable]
public class PlayerFootstepSettings
{
    public PlayerFootstepSurface[] Surfaces;

    [Tooltip("地面種別に一致しなかった場合に使うSurfacesのIndex。")]
    [Min(0)] public int DefaultSurfaceIndex;

    [Tooltip("1歩を鳴らす水平移動距離(m)。")]
    [Min(0.1f)] public float StepDistance = 1.35f;

    [Tooltip("極端な連続再生を防ぐ最短間隔(秒)。")]
    [Min(0f)] public float MinInterval = 0.12f;

    [Range(0f, 0.2f)] public float PitchVariation = 0.04f;
    [Min(0.1f)] public float SurfaceCheckDistance = 1.2f;
}

[CreateAssetMenu(fileName = "Player_SoundSettings", menuName = "Player/Sound Settings")]
public class Player_SoundSettings : ScriptableObject
{
    [Header("出力")]
    public AudioMixerGroup OutputMixerGroup;
    [Range(0f, 1f)] public float MasterVolume = 1f;

    [Header("共通3D設定")]
    [Range(0f, 1f)] public float SpatialBlendMultiplier = 0.35f;
    [Min(0.1f)] public float MinDistanceMultiplier = 1f;
    [Min(0.1f)] public float MaxDistanceMultiplier = 1f;
    public AnimationCurve DistanceRolloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);

    [Header("基本動作")]
    public SoundCue[] Jump;
    public SoundCue Land = new();
    public SoundCue[] Dodge;
    public SoundCue[] Damage;
    public SoundCue Heal = new();

    [Header("攻撃")]
    public SoundCue[] AttackHit;
    public SoundCue AttackMiss = new();
    [Min(0f)] public float AttackHitMinInterval = 0.06f;

    [Header("チャージ攻撃")]
    public SoundCue[] ChargeStart;
    public SoundCue ChargeLevel1 = new();
    [Tooltip("チャージレベルが上がった瞬間に1回鳴らす合図SE (Player_Charge_Level01_Done)。")]
    public SoundCue ChargeLevel1Done = new();
    public SoundCue ChargeFinish = new();
    public SoundCue ChargeDone = new();
    public SoundCue ChargeRelease = new();
    [Tooltip("ChargeFinish開始からCharge_Doneループを重ね始めるまでの時間(秒)。")]
    [Min(0f)] public float ChargeDoneStartDelay = 0.7f;
    [Tooltip("ChargeStartのループ開始時間(秒)。")]
    [Min(0f)] public float ChargeLoopStartSeconds;
    [Tooltip("ChargeStartの先頭からここまでをループする時間(秒)。")]
    [Min(0.05f)] public float ChargeLoopEndSeconds = 1.5f;
    [Tooltip("Charge_Doneのループ開始時間(秒)。")]
    [Min(0f)] public float ChargeDoneLoopStartSeconds;
    [Tooltip("Charge_Doneのループ終了時間(秒)。")]
    [Min(0.05f)] public float ChargeDoneLoopEndSeconds = 1.5f;
    [Tooltip("チャージ段階が切り替わる際のクロスフェード時間(秒)。")]
    [Min(0f)] public float ChargeBlendDuration = 0.12f;

    [Header("足音")]
    public PlayerFootstepSettings Footsteps = new();

    private void OnValidate()
    {
        ChargeLoopStartSeconds = Mathf.Max(0f, ChargeLoopStartSeconds);
        ChargeLoopEndSeconds = Mathf.Max(ChargeLoopStartSeconds + 0.05f, ChargeLoopEndSeconds);
        ChargeDoneStartDelay = Mathf.Max(0f, ChargeDoneStartDelay);
        ChargeDoneLoopStartSeconds = Mathf.Max(0f, ChargeDoneLoopStartSeconds);
        ChargeDoneLoopEndSeconds = Mathf.Max(
            ChargeDoneLoopStartSeconds + 0.05f, ChargeDoneLoopEndSeconds);
    }
}
