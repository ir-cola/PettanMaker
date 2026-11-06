using UnityEngine;

/// <summary>
/// Boss01のSE再生専用コンポーネント。
/// クリップ・音量・ピッチ・3D距離はすべてB1_SoundSettingsへ集約する。
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class B1_SoundManager : MonoBehaviour
{
    [Header("設定")]
    [SerializeField] private B1_SoundSettings settings;

    [Header("出力元（MixerGroupのフォールバックにも使用）")]
    [SerializeField] private AudioSource source;

    private B1_Boss boss;
    private int lastWalkStepIndex = -1;
    private float lastFootstepTime = float.NegativeInfinity;
    private SoundHandle tailChargeHandle;

    public B1_ManaBurstSoundSettings ManaBurst => settings != null ? settings.ManaBurst : null;

    public void Initialize(B1_Boss owner)
    {
        boss = owner;
        if (source == null)
            source = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();

        source.playOnAwake = false;
        source.spatialBlend = 0f;

        if (settings == null)
        {
            Debug.LogWarning("[B1_SoundManager] B1_SoundSettingsが未設定です。Boss01のSEは再生されません。", this);
            return;
        }

        if (settings.OutputMixerGroup != null)
            source.outputAudioMixerGroup = settings.OutputMixerGroup;
    }

    public SoundHandle PlayEntrance() => Play(settings?.Entrance, transform.position);
    public SoundHandle PlayDefeated()
    {
        Duck(settings?.Defeated, settings?.HeavyDuck);
        return Play(settings?.Defeated, transform.position);
    }
    public SoundHandle PlayRushCharge() => Play(settings?.RushCharge, transform.position);
    public SoundHandle PlayRush()
    {
        Duck(settings?.Rush, settings?.MediumDuck);
        return Play(settings?.Rush, transform.position);
    }
    public SoundHandle PlayTeleport()
    {
        Duck(settings?.Teleport, settings?.LightDuck);
        return Play(settings?.Teleport, transform.position);
    }
    public SoundHandle PlayTransparent()
    {
        Duck(settings?.Transparent, settings?.LightDuck);
        return Play(settings?.Transparent, transform.position);
    }

    public SoundHandle PlayScratch(int hitIndex)
    {
        B1_ScratchSoundSettings scratch = settings?.Scratch;
        if (scratch == null) return null;

        int i = Mathf.Clamp(hitIndex, 0, 2);
        SoundCue cue = scratch.Swing;
        if (scratch.SwingVariations != null && scratch.SwingVariations.Length > 0) {
            SoundCue picked = scratch.SwingVariations[Mathf.Clamp(i, 0, scratch.SwingVariations.Length - 1)];
            if (picked != null && picked.Clip != null) cue = picked;
        }
        return Play(cue, transform.position,
            Component(scratch.PitchPerHit, i),
            Component(scratch.VolumePerHit, i));
    }

    public SoundHandle PlayAttackHit()
    {
        B1_ScratchSoundSettings scratch = settings?.Scratch;
        if (scratch == null || boss?.ActionController?.CurrentAction is not B1_ScratchAction) return null;
        Duck(scratch.Hit, settings.LightDuck);

        return Play(scratch.Hit, transform.position,
            scratch.HitPitchMultiplier,
            scratch.HitVolumeMultiplier);
    }

    public SoundHandle PlayCriticalHit()
    {
        Duck(settings?.CriticalHit, settings?.LightDuck);
        return Play(settings?.CriticalHit, transform.position);
    }

    public SoundHandle PlayTailCharge()
    {
        StopTailCharge();
        tailChargeHandle = Play(settings?.TailCharge, transform.position);
        return tailChargeHandle;
    }

    public void StopTailCharge()
    {
        SoundManagerSystem.Stop(ref tailChargeHandle);
    }
    public SoundHandle PlayTailJump()
    {
        Duck(settings?.TailJump, settings?.LightDuck);
        return Play(settings?.TailJump, transform.position);
    }
    public SoundHandle PlayTailAttack()
    {
        Duck(settings?.TailAttack, settings?.MediumDuck);
        return Play(settings?.TailAttack, transform.position);
    }
    public SoundHandle PlayTailLand()
    {
        Duck(settings?.TailLand, settings?.MediumDuck);
        return Play(settings?.TailLand, transform.position);
    }
    public SoundHandle PlayBackStepLand()
    {
        Duck(settings?.BackStepLand, settings?.LightDuck);
        return Play(settings?.BackStepLand, transform.position);
    }

    public SoundHandle PlayTailAttackImpact(Vector3 position)
    {
        Duck(settings?.TailAttackImpact, settings?.HeavyDuck);
        SoundHandle main = Play(settings?.TailAttackImpact, position);
        Play(settings?.TailAttackLowLayer, position);
        return main;
    }

    public SoundHandle PlayTailRainImpact(Vector3 position, bool finisher)
    {
        SoundCue cue = finisher ? settings?.TailRainFinisher : settings?.TailRainImpact;
        Duck(cue, finisher ? settings?.HeavyDuck : settings?.MediumDuck);
        return Play(cue, position);
    }

    public SoundHandle PlayManaBurst(Vector3 position, int group, bool finisher)
    {
        B1_ManaBurstSoundSettings mana = ManaBurst;
        if (mana == null) return null;

        float rise = Mathf.Min(Mathf.Max(0, group) * mana.PitchStep, mana.MaxPitchRise);
        float jitter = UnityEngine.Random.Range(-mana.PitchJitter, mana.PitchJitter);
        float pitch = mana.PitchStart + rise + jitter;
        float volume = 1f;

        if (finisher)
        {
            pitch += mana.FinisherPitchOffset;
            volume *= mana.FinisherVolumeMultiplier;
        }

        Duck(mana.Burst, finisher ? settings.HeavyDuck : settings.LightDuck);
        return Play(mana.Burst, position, pitch, volume, useCueRandomPitch: false);
    }

    /// <summary>Walk/Runクリップの足接地AnimationEventから呼ぶ。</summary>
    public SoundHandle PlayWalkStep()
    {
        B1_FootstepSoundSettings footsteps = settings?.Footsteps;
        if (footsteps?.Steps == null || footsteps.Steps.Length == 0) return null;
        if (Time.time - lastFootstepTime < footsteps.MinInterval) return null;

        int index = PickDifferentIndex(footsteps.Steps.Length, lastWalkStepIndex);
        lastWalkStepIndex = index;
        lastFootstepTime = Time.time;

        bool running = boss?.Locomotion?.CurrentMode == B1_Locomotion.MoveMode.Run;
        float pitch = 1f + UnityEngine.Random.Range(-footsteps.PitchVariation, footsteps.PitchVariation);
        if (running) pitch += footsteps.RunPitchOffset;
        float volume = running ? footsteps.RunVolumeScale : footsteps.WalkVolumeScale;

        return Play(footsteps.Steps[index], transform.position, pitch, volume);
    }

    /// <summary>
    /// Boss01固有設定を共通 SoundManagerSystem のオプションへ変換して再生する。
    /// 戻り値を保持すれば、呼び出し側から再生中のPitch/Volume/Stop等を操作できる。
    /// </summary>
    public SoundHandle Play(SoundCue cue, Vector3 position, float pitchMultiplier = 1f,
                            float volumeMultiplier = 1f, bool useCueRandomPitch = true)
    {
        if (settings == null || cue?.Clip == null) return null;

        return SoundManagerSystem.Play(cue, position, new SoundPlayOptions
        {
            OutputMixerGroup = settings.OutputMixerGroup != null
                ? settings.OutputMixerGroup
                : source != null ? source.outputAudioMixerGroup : null,
            MasterVolume = settings.MasterVolume,
            VolumeMultiplier = volumeMultiplier,
            PitchMultiplier = pitchMultiplier,
            UseRandomPitch = useCueRandomPitch,
            SpatialBlendMultiplier = settings.SpatialBlendMultiplier,
            MinDistanceMultiplier = settings.MinDistanceMultiplier,
            MaxDistanceMultiplier = settings.MaxDistanceMultiplier,
            DistanceRolloff = settings.DistanceRolloff,
            ObjectName = "B1_OneShotSE"
        });
    }

    private void OnDisable() => StopTailCharge();

    private static void Duck(SoundCue cue, B1_BgmDuckSettings duck)
    {
        if (cue?.Clip == null || duck == null || !duck.Enabled || duck.VolumeScale >= 0.999f) return;

        BGMManager.PulseVolume(
            duck.VolumeScale,
            duck.AttackDuration,
            duck.HoldDuration,
            duck.ReleaseDuration);
    }

    private static float Component(Vector3 value, int index)
        => index == 0 ? value.x : index == 1 ? value.y : value.z;

    private static int PickDifferentIndex(int count, int previous)
    {
        if (count <= 1) return 0;
        int index = UnityEngine.Random.Range(0, count - 1);
        if (index >= previous) index++;
        return index;
    }
}
