using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// Entrance カメラ Timeline (PlayableDirector) 用のビート/フォグ/UI ブリッジ。
/// カメラの移動そのものは Timeline 側(CinemachineCamera + Animation Track)が持つので、
/// ここでは旧 B1_CameraDirector の DoAppearBeat/DoRoarBeat/BeginTitle/フォグ駆動ロジックを
/// そのまま移設し、Signal Track の SignalReceiver からこの公開メソッドを呼んでもらう。
/// </summary>
[DisallowMultipleComponent]
public class B1_CameraTimelineBridge : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private PlayableDirector director;
    [Tooltip("Entrance のカメラ設定(Fog/UI/Shockwave/LookHeight を読む)")]
    [SerializeField] private B1_CameraSceneSettings scene;
    [SerializeField] private Transform framingTarget;
    [Tooltip("3D Roarの発生中心。Scene上のGameObjectまたはボーンを指定。未設定時はFraming Targetを使用")]
    [SerializeField] private Transform roarTargetPosition;
    [SerializeField] private B1_Boss boss;
    [SerializeField] private B1_SoundManager soundManager;

    [Header("Fog / Title Timing")]
    [Tooltip("フォグが weight 0→1 に達するまでの秒数。establishing(寄り込み)の尺に合わせる")]
    [SerializeField] private float establishingDuration = 2f;
    [Tooltip("タイトルが自己タイマー(titleDuration>0)を使わない場合のフェードイン秒数")]
    [SerializeField] private float titleFadeInDuration = 0.3f;

    private B1_CinematicFog activeFog;
    private B1_CinematicOverlay activeOverlay;
    private B1_CinematicOverlay detachedOverlay;
    private readonly List<B1_ShockwaveDistortion> activeShockwaves = new();
    private B1_ScreenColorDrain activeColorDrain;
    private Coroutine shockwaveSpawner;
    private Coroutine finishRoutine;

    private bool titleFiredNonTimed;
    private float titleFiredAt;
    // director.played から stopped までの間 true。ForceStop の HPゲージ即時表示を
    // 「実際に再生中の演出が中断された場合」に限定するための判定。
    // これが無いと、PlayEntranceMap 等の予防的な StopTimeline → ForceStop で
    // Entrance 前に隠しているゲージが表示されてしまう。
    private bool sessionActive;

    private void OnEnable()
    {
        if (director == null) return;
        director.played += HandlePlayed;
        director.stopped += HandleStopped;
    }

    private void OnDisable()
    {
        if (director != null)
        {
            director.played -= HandlePlayed;
            director.stopped -= HandleStopped;
        }
        ForceStop();
    }

    private void Update()
    {
        if (director == null || director.state != PlayState.Playing) return;

        if (activeFog != null)
        {
            float w = establishingDuration > 0.0001f
                ? Mathf.Clamp01((float)director.time / establishingDuration)
                : 1f;
            activeFog.SetWeight(w);
        }

        if (titleFiredNonTimed && activeOverlay != null)
        {
            float t = titleFadeInDuration > 0.0001f
                ? Mathf.Clamp01((Time.unscaledTime - titleFiredAt) / titleFadeInDuration)
                : 1f;
            activeOverlay.SetAlpha(t);
        }
    }

    // ======================================================================
    //  Signal Receiver から呼ぶ公開ビート API
    // ======================================================================

    /// <summary>appear ビート: 煙の中からボス出現。</summary>
    public void OnAppear()
    {
        if (boss != null) boss.RevealFromHidden();
    }

    /// <summary>roar ビート: 登場SE + ヒットストップ + スナップズーム + 画面衝撃波 + ポストプロセスパルス。</summary>
    public void OnRoar()
    {
        if (soundManager != null) soundManager.PlayEntrance();

        // インパクトフレーム: 全体を一瞬止め、FOV をパンチインして「タメ→ドン」を作る。
        // Timeline(GameTime 駆動)も一緒に静止するのが狙い。CameraShake/SnapZoom は
        // unscaled 駆動なので静止中も揺れ・ズームは進む。
        var impact = scene != null ? scene.RoarImpact : null;
        if (impact != null && impact.enabled)
        {
            if (impact.hitStopDuration > 0f)
                GlobalHitStop.Play(impact.hitStopDuration, impact.hitStopScale);
            if (impact.snapZoomFov > 0f)
                CameraSnapZoom.Play(impact.snapZoomFov, impact.snapZoomIn, impact.snapZoomHold, impact.snapZoomOut);
        }

        TriggerRoarObjects();
        TriggerPhase2PlayerKnockback();
        SpawnShockwave();
        // カメラシェイク。設定(RoarShake)を持つシーンではそちらを正とし、
        // B1_PostProcessFX 内蔵の固定値シェイクは抑制して二重揺れを防ぐ。
        // 咆哮の発生源から押し出される指向性インパルスにして「衝撃の出どころ」を伝える。
        var roarShake = scene != null ? scene.RoarShake : null;
        if (roarShake != null && roarShake.enabled)
        {
            Transform origin = roarTargetPosition != null ? roarTargetPosition
                             : framingTarget != null ? framingTarget : transform;
            CameraShake.PlayImpulse(origin.position, roarShake.duration, roarShake.intensity, roarShake.frequency);
        }
        // 咆哮のポストプロセス演出 (レンズ歪み+Bloom爆発)。シーンに B1_PostProcessFX があれば発火。
        FindFirstObjectByType<B1_PostProcessFX>()?.TriggerRoarPulse(withShake: roarShake == null);
    }

    private void TriggerRoarObjects()
    {
        Transform target = roarTargetPosition != null
            ? roarTargetPosition
            : framingTarget != null ? framingTarget : transform;
        var shockwave = scene != null ? scene.Shockwave : null;
        float radius = shockwave != null
            ? Mathf.Max(shockwave.startScale, shockwave.endScale)
            : 11f;

        B1_AttackBounceObject.PushFromRoar(target.position, radius);
    }

    private void TriggerPhase2PlayerKnockback()
    {
        if (scene is not B1_Phase2CameraSettings phase2 || !phase2.RoarKnockbackPlayer)
            return;

        PlayerHitReaction reaction = FindFirstObjectByType<PlayerHitReaction>();
        if (reaction == null)
            return;

        Transform origin = roarTargetPosition != null
            ? roarTargetPosition
            : framingTarget != null ? framingTarget : transform;
        reaction.PlayKnockbackFrom(origin.position, phase2.RoarKnockbackDistance, damageType: 2,
                                   durationOverride: phase2.RoarKnockbackDuration);
    }

    /// <summary>colorDrain ビート: Roar の少し手前に画面の色を一瞬抜く（予兆）。Roar Signal の前へ置く。</summary>
    public void OnColorDrain()
    {
        var s = scene != null ? scene.ColorDrain : null;
        if (s == null || !s.enabled) return;
        if (activeColorDrain != null) activeColorDrain.Kill();
        activeColorDrain = B1_ScreenColorDrain.Spawn(s.ToParams());
    }

    /// <summary>Phase2 の変身ビート: 怒りモード用マテリアルへ切り替え、Phase2 移行ポストプロセスを焚く。</summary>
    public void OnEnrageMaterial()
    {
        if (scene != null && scene.SceneKind != B1_CameraSceneKind.Phase2) return;
        boss?.ActivateEnragedVisual();
        // 目が赤くなるこの瞬間に合わせて、Phase2 突入フラッシュ + フェーズ2ルックを開始する。
        FindFirstObjectByType<B1_PostProcessFX>()?.TriggerPhase2Transition();
    }

    /// <summary>title ビート: 自己タイマー(titleDuration&gt;0)なら手放して独立表示、なければフェードインを開始する。</summary>
    public void OnTitleBegin()
    {
        if (activeOverlay == null) return;

        var ui = scene != null ? scene.UI : null;
        if (ui != null && ui.titleDuration > 0f)
        {
            activeOverlay.PlayTimed(ui.titleDuration, 0.3f);
            if (detachedOverlay != null && detachedOverlay != activeOverlay) detachedOverlay.Dispose();
            detachedOverlay = activeOverlay;
            activeOverlay = null;
            return;
        }

        activeOverlay.Begin();
        titleFiredNonTimed = true;
        titleFiredAt = Time.unscaledTime;
    }

    // ======================================================================
    //  Director イベント
    // ======================================================================

    private void HandlePlayed(PlayableDirector pd)
    {
        ForceStop(); // 前回セッションの残骸(フェード中の破棄待ちなど)を確実に片付けてから開始する
        // フォグは登場(Entrance)演出のみ。Phase2 / Defeat では出さない。
        bool useFog = scene != null && scene.SceneKind == B1_CameraSceneKind.Entrance;
        activeFog = B1_CinematicFog.Create(useFog ? scene.Fog : null);
        activeOverlay = B1_CinematicOverlay.Create(scene != null ? scene.UI : null);
        if (useFog) ResolveHealthUI()?.PrepareStickerEntrance();
        titleFiredNonTimed = false;
        sessionActive = true;
    }

    private void HandleStopped(PlayableDirector pd)
    {
        // 正常終了。HPゲージの表示開始は B1_CameraDirector 側の PlayHealthGaugeEntrance に任せる。
        sessionActive = false;
        // GameObject が非アクティブだとコルーチンを開始できない(本来は常時アクティブにしておくべき)。
        // その場合はフェードを諦めて即座に片付ける。
        if (!isActiveAndEnabled) { ForceStop(); return; }
        finishRoutine = StartCoroutine(FinishRoutine());
    }

    /// <summary>Entrance 終了後、通常カメラへ戻ったタイミングでHPゲージを貼り付ける。</summary>
    public void PlayHealthGaugeEntrance()
    {
        ResolveHealthUI()?.PlayStickerEntrance(scene != null ? scene.UI : null);
    }

    private IEnumerator FinishRoutine()
    {
        yield return FadeOutOverlay();
        if (activeFog != null) { activeFog.Finish(); activeFog = null; }
        if (activeOverlay != null) { activeOverlay.Dispose(); activeOverlay = null; }
        finishRoutine = null;
    }

    private IEnumerator FadeOutOverlay()
    {
        if (activeOverlay == null) yield break;
        const float dur = 0.3f;
        float t = 0f;
        while (t < dur)
        {
            activeOverlay.SetAlpha(1f - t / dur);
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        activeOverlay.SetAlpha(0f);
    }

    // ======================================================================
    //  衝撃波
    // ======================================================================

    private void SpawnShockwave()
    {
        var s = scene != null ? scene.Shockwave : null;
        int count = s != null ? s.count : 1;
        if (count <= 0) return;

        var p = s != null ? s.ToParams() : B1_ShockwaveDistortion.Params.Default;
        float height = (scene != null ? scene.LookHeight : 0f) + (s != null ? s.heightOffset : 0f);
        float interval = s != null ? Mathf.Max(0f, s.interval) : 0f;

        if (count == 1 || interval <= 0f)
        {
            for (int i = 0; i < count; i++) SpawnShockwaveAt(height, p);
            return;
        }
        if (shockwaveSpawner != null) StopCoroutine(shockwaveSpawner);
        shockwaveSpawner = StartCoroutine(SpawnShockwavesRoutine(count, interval, height, p));
    }

    private void SpawnShockwaveAt(float height, B1_ShockwaveDistortion.Params p)
    {
        Transform tgt = framingTarget != null ? framingTarget : transform;
        Vector3 pos = roarTargetPosition != null
            ? roarTargetPosition.position
            : tgt.position + Vector3.up * height;
        var sw = B1_ShockwaveDistortion.Spawn(pos, p);
        if (sw != null) activeShockwaves.Add(sw);
    }

    private IEnumerator SpawnShockwavesRoutine(
        int count, float interval, float height, B1_ShockwaveDistortion.Params p)
    {
        for (int i = 0; i < count; i++)
        {
            SpawnShockwaveAt(height, p);
            if (i < count - 1) yield return new WaitForSecondsRealtime(interval);
        }
    }

    // ======================================================================
    //  強制停止(中断 / OnDisable / 再生し直し)
    // ======================================================================

    /// <summary>フェードを待たず即座に片付ける。B1_CameraDirector.Stop() から呼ぶ。</summary>
    public void ForceStop()
    {
        StopAllCoroutines();
        finishRoutine = null;
        shockwaveSpawner = null;

        if (activeFog != null) { activeFog.Dispose(); activeFog = null; }
        if (activeOverlay != null) { activeOverlay.Dispose(); activeOverlay = null; }
        if (detachedOverlay != null) { detachedOverlay.Dispose(); detachedOverlay = null; }

        for (int i = 0; i < activeShockwaves.Count; i++)
            if (activeShockwaves[i] != null) activeShockwaves[i].Kill();
        activeShockwaves.Clear();

        if (activeColorDrain != null) { activeColorDrain.Kill(); activeColorDrain = null; }

        // HPゲージの即時表示は「再生中の演出が中断された」場合の保険に限定する。
        // 予防的な後片付け(再生前の StopTimeline など)で呼ばれたときに表示すると、
        // Entrance 前に隠しているゲージが先に出てしまう。
        if (sessionActive) ResolveHealthUI()?.RevealImmediately();
        sessionActive = false;
        titleFiredNonTimed = false;
    }

    private B1_HealthUI ResolveHealthUI()
    {
        if (boss == null || boss.Health == null) return null;
        return boss.Health.GetComponent<B1_HealthUI>();
    }
}
