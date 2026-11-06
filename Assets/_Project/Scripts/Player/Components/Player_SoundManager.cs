using UnityEngine;

/// <summary>
/// Player固有のSEイベントを Player_SoundSettings へ対応付ける薄いマネージャー。
/// 実際のAudioSource生成・寿命管理・再生中操作は SoundManagerSystem が担当する。
/// </summary>
public class Player_SoundManager : MonoBehaviour
{
    [SerializeField] private Player_SoundSettings settings;

    private IPlayerContext player;
    private Vector3 previousPosition;
    private float footstepDistance;
    private float lastFootstepTime = float.NegativeInfinity;
    private int lastJumpIndex = -1;
    private int lastDodgeIndex = -1;
    private int lastDamageIndex = -1;
    private int lastFootstepIndex = -1;
    private int lastAttackHitIndex = -1;
    private int lastChargeStartIndex = -1;
    private float lastAttackHitTime = float.NegativeInfinity;
    private SoundHandle chargeStageHandle;
    private SoundHandle chargeStageLayerHandle;
    private SoundHandle chargeReleaseHandle;
    private Coroutine chargeDoneSequence;

    public Player_SoundSettings Settings => settings;

    public void Initialize(IPlayerContext owner)
    {
        player = owner;
        previousPosition = transform.position;

        if (settings == null)
            Debug.LogWarning("[Player_SoundManager] Player_SoundSettingsが未設定です。PlayerのSEは再生されません。", this);
    }

    private void Update()
    {
        UpdateAutomaticFootsteps();
    }

    public SoundHandle PlayJump(float pitchMultiplier = 1f)
    {
        SoundCue cue = PickDifferent(settings?.Jump, ref lastJumpIndex);
        return Play(cue, pitchMultiplier);
    }

    public SoundHandle PlayLand(float pitchMultiplier = 1f)
        => Play(settings?.Land, pitchMultiplier);

    public SoundHandle PlayDodge(float pitchMultiplier = 1f)
    {
        SoundCue cue = PickDifferent(settings?.Dodge, ref lastDodgeIndex);
        return Play(cue, pitchMultiplier);
    }

    public SoundHandle PlayDamage(float pitchMultiplier = 1f)
    {
        SoundCue cue = PickDifferent(settings?.Damage, ref lastDamageIndex);
        return Play(cue, pitchMultiplier);
    }

    public SoundHandle PlayHeal(float pitchMultiplier = 1f)
        => Play(settings?.Heal, pitchMultiplier);

    public SoundHandle PlayAttackHit(float pitchMultiplier = 1f)
    {
        if (settings == null || Time.unscaledTime - lastAttackHitTime < settings.AttackHitMinInterval)
            return null;

        SoundCue cue = PickDifferent(settings.AttackHit, ref lastAttackHitIndex);
        SoundHandle handle = Play(cue, pitchMultiplier);
        if (handle != null) lastAttackHitTime = Time.unscaledTime;
        return handle;
    }

    public SoundHandle PlayAttackMiss(float pitchMultiplier = 1f)
        => Play(settings?.AttackMiss, pitchMultiplier);

    public SoundHandle PlayChargeStart(float pitchMultiplier = 1f)
    {
        StopAllChargeSounds(ChargeBlendDuration);
        SoundCue cue = PickDifferent(settings?.ChargeStart, ref lastChargeStartIndex);
        float loopStart = settings != null ? settings.ChargeLoopStartSeconds : 0f;
        float loopEnd = settings != null ? settings.ChargeLoopEndSeconds : 1.5f;
        chargeStageHandle = PlayChargeWithFadeIn(
            cue, pitchMultiplier, loop: true,
            loopStartSeconds: loopStart, loopEndSeconds: loopEnd);
        return chargeStageHandle;
    }

    public SoundHandle PlayChargeLevel1(float pitchMultiplier = 1f)
    {
        CutForNextChargeTiming();
        // レベルが上がった合図として1回だけ鳴らす (ループの段階切替とは別レイヤー)。
        Play(settings?.ChargeLevel1Done, pitchMultiplier);
        chargeStageHandle = PlayChargeWithFadeIn(settings?.ChargeLevel1, pitchMultiplier);
        return chargeStageHandle;
    }

    public SoundHandle PlayChargeLevel2(float pitchMultiplier = 1f)
    {
        CutForNextChargeTiming();
        chargeStageHandle = PlayChargeWithFadeIn(settings?.ChargeFinish, pitchMultiplier);

        if (chargeStageHandle != null)
        {
            float delay = settings != null ? settings.ChargeDoneStartDelay : 0.7f;
            chargeDoneSequence = StartCoroutine(
                PlayChargeDoneAfterDelay(chargeStageHandle, pitchMultiplier, delay));
        }
        else
            StartChargeDoneLoop(pitchMultiplier);

        return chargeStageHandle;
    }

    public SoundHandle PlayChargeRelease(float pitchMultiplier = 1f)
    {
        StopChargeStage(ChargeBlendDuration);
        SoundManagerSystem.Stop(ref chargeReleaseHandle, ChargeBlendDuration);
        chargeReleaseHandle = PlayChargeWithFadeIn(settings?.ChargeRelease, pitchMultiplier);
        return chargeReleaseHandle;
    }

    /// <summary>チャージキャンセル/ステート退出用。解放済みのSEは止めない。</summary>
    public void StopCharge()
        => StopChargeStage(ChargeBlendDuration);

    private void CutForNextChargeTiming()
    {
        StopChargeStage(ChargeBlendDuration);
        SoundManagerSystem.Stop(ref chargeReleaseHandle, ChargeBlendDuration);
    }

    private void StopChargeStage(float fadeDuration)
    {
        CancelChargeDoneSequence();
        SoundManagerSystem.Stop(ref chargeStageHandle, fadeDuration);
        SoundManagerSystem.Stop(ref chargeStageLayerHandle, fadeDuration);
    }

    private void StopAllChargeSounds(float fadeDuration)
    {
        StopChargeStage(fadeDuration);
        SoundManagerSystem.Stop(ref chargeReleaseHandle, fadeDuration);
    }

    private SoundHandle PlayChargeWithFadeIn(SoundCue cue, float pitchMultiplier,
                                             bool loop = false, float loopStartSeconds = 0f,
                                             float loopEndSeconds = 0f)
    {
        SoundHandle handle = Play(
            cue, pitchMultiplier, loop: loop,
            loopStartSeconds: loopStartSeconds, loopEndSeconds: loopEndSeconds);
        if (handle == null || ChargeBlendDuration <= 0f) return handle;

        float targetVolume = handle.Volume;
        handle.Volume = 0f;
        handle.FadeTo(targetVolume, ChargeBlendDuration);
        return handle;
    }

    private System.Collections.IEnumerator PlayChargeDoneAfterDelay(
        SoundHandle finishHandle, float pitchMultiplier, float delay)
    {
        float elapsed = 0f;
        while (elapsed < delay)
        {
            if (chargeStageHandle != finishHandle) yield break;
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        chargeDoneSequence = null;
        if (chargeStageHandle != finishHandle) yield break;

        StartChargeDoneLoop(pitchMultiplier);
    }

    private void StartChargeDoneLoop(float pitchMultiplier)
    {
        if (settings == null) return;

        chargeStageLayerHandle = PlayChargeWithFadeIn(
            settings.ChargeDone,
            pitchMultiplier,
            loop: true,
            loopStartSeconds: settings.ChargeDoneLoopStartSeconds,
            loopEndSeconds: settings.ChargeDoneLoopEndSeconds);
    }

    private void CancelChargeDoneSequence()
    {
        if (chargeDoneSequence == null) return;
        StopCoroutine(chargeDoneSequence);
        chargeDoneSequence = null;
    }

    private void OnDisable()
        => StopAllChargeSounds(0f);

    private float ChargeBlendDuration
        => settings != null ? Mathf.Max(0f, settings.ChargeBlendDuration) : 0f;

    /// <summary>AnimationEventからも手動で呼べる。自動足音との二重再生を避けるため距離をリセットする。</summary>
    public SoundHandle PlayFootstep()
    {
        PlayerFootstepSettings footsteps = settings?.Footsteps;
        if (footsteps == null || Time.time - lastFootstepTime < footsteps.MinInterval) return null;

        PlayerFootstepSurface surface = FindCurrentSurface(footsteps);
        SoundCue cue = PickDifferent(surface?.Steps, ref lastFootstepIndex);
        if (cue == null) return null;

        lastFootstepTime = Time.time;
        footstepDistance = 0f;
        float pitch = 1f + Random.Range(-footsteps.PitchVariation, footsteps.PitchVariation);
        return Play(cue, pitch, useRandomPitch: false);
    }

    /// <summary>
    /// Player固有設定を共通 SoundManagerSystem へ渡す。戻り値からPitch/Volume/Stop等を操作できる。
    /// </summary>
    public SoundHandle Play(SoundCue cue, float pitchMultiplier = 1f,
                            float volumeMultiplier = 1f, bool useRandomPitch = true,
                            bool loop = false, float loopStartSeconds = 0f,
                            float loopEndSeconds = 0f)
    {
        if (settings == null || cue?.Clip == null) return null;

        return SoundManagerSystem.PlayAttached(cue, transform, new SoundPlayOptions
        {
            OutputMixerGroup = settings.OutputMixerGroup,
            MasterVolume = settings.MasterVolume,
            VolumeMultiplier = volumeMultiplier,
            PitchMultiplier = pitchMultiplier,
            UseRandomPitch = useRandomPitch,
            SpatialBlendMultiplier = settings.SpatialBlendMultiplier,
            MinDistanceMultiplier = settings.MinDistanceMultiplier,
            MaxDistanceMultiplier = settings.MaxDistanceMultiplier,
            DistanceRolloff = settings.DistanceRolloff,
            Loop = loop,
            LoopStartSeconds = loopStartSeconds,
            LoopEndSeconds = loopEndSeconds,
            ObjectName = "Player_OneShotSE"
        });
    }

    private void UpdateAutomaticFootsteps()
    {
        Vector3 current = transform.position;
        Vector3 delta = current - previousPosition;
        previousPosition = current;
        delta.y = 0f;

        PlayerFootstepSettings footsteps = settings?.Footsteps;
        if (footsteps == null || player?.Mover == null || !player.Mover.IsGrounded || player.Mover.PlanarSpeed01 < 0.1f)
        {
            footstepDistance = 0f;
            return;
        }

        footstepDistance += delta.magnitude;
        if (footstepDistance < footsteps.StepDistance) return;

        PlayFootstep();
    }

    private PlayerFootstepSurface FindCurrentSurface(PlayerFootstepSettings footsteps)
    {
        PlayerFootstepSurface[] surfaces = footsteps.Surfaces;
        if (surfaces == null || surfaces.Length == 0) return null;

        Vector3 origin = transform.position + Vector3.up * 0.25f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                            footsteps.SurfaceCheckDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            for (int i = 0; i < surfaces.Length; i++)
            {
                if (surfaces[i] != null && surfaces[i].Matches(hit.collider))
                    return surfaces[i];
            }
        }

        int fallback = Mathf.Clamp(footsteps.DefaultSurfaceIndex, 0, surfaces.Length - 1);
        return surfaces[fallback];
    }

    private static SoundCue PickDifferent(SoundCue[] cues, ref int previous)
    {
        if (cues == null || cues.Length == 0) return null;
        if (cues.Length == 1)
        {
            previous = 0;
            return cues[0];
        }

        if (previous < 0 || previous >= cues.Length)
        {
            previous = Random.Range(0, cues.Length);
            return cues[previous];
        }

        int index = Random.Range(0, cues.Length - 1);
        if (index >= previous) index++;
        previous = index;
        return cues[index];
    }
}
