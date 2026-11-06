using System;
using UnityEngine;
using UnityEngine.Audio;

[Serializable]
public class B1_ScratchSoundSettings
{
    public SoundCue Swing = new();
    [Tooltip("設定するとヒット段(0,1,2)ごとにこの配列のCueを使う（空なら Swing を使用）。")]
    public SoundCue[] SwingVariations;
    public SoundCue Hit = new();

    [Tooltip("Scratch01/02/03のピッチ倍率。後段ほど低くして重さを出す。")]
    public Vector3 PitchPerHit = new(1.08f, 0.98f, 0.86f);

    public Vector3 VolumePerHit = new(0.82f, 0.92f, 1f);

    [Range(0f, 2f)] public float HitPitchMultiplier = 0.76f;
    [Range(0f, 2f)] public float HitVolumeMultiplier = 0.55f;
}

[Serializable]
public class B1_FootstepSoundSettings
{
    public SoundCue[] Steps;

    [Range(0f, 0.15f)] public float PitchVariation = 0.03f;
    [Range(0f, 1.5f)] public float WalkVolumeScale = 1f;
    [Range(0f, 1.5f)] public float RunVolumeScale = 1.2f;
    [Range(-0.2f, 0.2f)] public float RunPitchOffset = -0.025f;
    [Min(0f)] public float MinInterval = 0.08f;
}

[Serializable]
public class B1_ManaBurstSoundSettings
{
    public SoundCue Burst = new();

    [Tooltip("最初の爆発グループのピッチ。")]
    public float PitchStart = 0.82f;

    [Tooltip("外側のグループへ進むごとのピッチ上昇量。")]
    public float PitchStep = 0.09f;

    [Tooltip("機械的な反復を避けるランダム幅。")]
    [Range(0f, 0.15f)] public float PitchJitter = 0.025f;

    [Tooltip("上昇しすぎ防止の最大加算値。")]
    [Min(0f)] public float MaxPitchRise = 0.45f;

    [Tooltip("最終爆発だけ加えるピッチ差。負値で重く着地する。")]
    public float FinisherPitchOffset = -0.14f;

    [Min(0f)] public float FinisherVolumeMultiplier = 1.25f;
}

[Serializable]
public class B1_BgmDuckSettings
{
    public bool Enabled = true;

    [Range(0f, 1f)]
    [Tooltip("SE再生時にBGMを一時的に下げる音量倍率。1なら変化なし。")]
    public float VolumeScale = 0.65f;

    [Min(0f)] public float AttackDuration = 0.02f;
    [Min(0f)] public float HoldDuration = 0.04f;
    [Min(0f)] public float ReleaseDuration = 0.22f;
}

[CreateAssetMenu(fileName = "B1_SoundSettings", menuName = "Boss01/Sound Settings")]
public class B1_SoundSettings : ScriptableObject
{
    [Header("出力")]
    public AudioMixerGroup OutputMixerGroup;
    [Range(0f, 1f)] public float MasterVolume = 1f;

    [Header("全体3D距離減衰")]
    [Tooltip("各SEのSpatialBlendへ乗算する。少し下げると遠距離でも2D成分が残り、聞こえやすくなる。")]
    [Range(0f, 1f)] public float SpatialBlendMultiplier = 0.82f;

    [Tooltip("最大音量のまま聞こえる距離を各SE設定から一括で広げる。")]
    [Min(0.1f)] public float MinDistanceMultiplier = 2f;

    [Tooltip("完全な遠距離になるまでの距離を各SE設定から一括で広げる。")]
    [Min(0.1f)] public float MaxDistanceMultiplier = 1.5f;

    [Tooltip("横軸0=MinDistance、1=MaxDistance。標準のLogarithmicより緩やかなカーブを推奨。")]
    public AnimationCurve DistanceRolloff = new(
        new Keyframe(0f, 1f),
        new Keyframe(0.25f, 0.96f),
        new Keyframe(0.5f, 0.85f),
        new Keyframe(0.75f, 0.65f),
        new Keyframe(1f, 0.32f));

    [Header("SE連動BGMダッキング")]
    [Tooltip("攻撃ヒット・小さな予兆向け。")]
    public B1_BgmDuckSettings LightDuck = new()
    {
        VolumeScale = 0.82f,
        AttackDuration = 0.015f,
        HoldDuration = 0.02f,
        ReleaseDuration = 0.12f
    };

    [Tooltip("突進・攻撃開始・着地向け。")]
    public B1_BgmDuckSettings MediumDuck = new()
    {
        VolumeScale = 0.65f,
        AttackDuration = 0.02f,
        HoldDuration = 0.04f,
        ReleaseDuration = 0.22f
    };

    [Tooltip("叩きつけ・フィニッシャー向け。")]
    public B1_BgmDuckSettings HeavyDuck = new()
    {
        VolumeScale = 0.45f,
        AttackDuration = 0.015f,
        HoldDuration = 0.06f,
        ReleaseDuration = 0.32f
    };

    [Header("基本")]
    public SoundCue Entrance = new();
    public SoundCue Defeated = new();
    public SoundCue RushCharge = new();
    public SoundCue Rush = new();
    public B1_ScratchSoundSettings Scratch = new();
    public SoundCue Teleport = new();
    public SoundCue Transparent = new();

    [Header("TailAttack")]
    public SoundCue TailCharge = new();
    public SoundCue TailJump = new();
    [Tooltip("TailAttackアニメーションへ入る瞬間の攻撃音。")]
    public SoundCue TailAttack = new();
    [Tooltip("しっぽ攻撃の衝撃波発生時。")]
    public SoundCue TailAttackImpact = new();
    [Tooltip("TailAttackImpactへ重ねる低音の2Dレイヤー。未割り当てなら重ねない。")]
    public SoundCue TailAttackLowLayer = new();
    [Tooltip("TailAttack_Landの足が接地する瞬間。")]
    public SoundCue TailLand = new();
    [Tooltip("BackStep_03の足が接地する瞬間。")]
    public SoundCue BackStepLand = new();

    [Header("TailRain")]
    public SoundCue TailRainImpact = new();
    public SoundCue TailRainFinisher = new();

    [Header("ManaBurst")]
    public B1_ManaBurstSoundSettings ManaBurst = new();

    [Header("クリティカルヒット")]
    [Tooltip("プレイヤー攻撃がクリティカルした瞬間に鳴らすSE。")]
    public SoundCue CriticalHit = new();

    [Header("足音")]
    public B1_FootstepSoundSettings Footsteps = new();
}
