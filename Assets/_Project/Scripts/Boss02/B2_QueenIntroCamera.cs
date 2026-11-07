/*=============================================================================

 File   : [B2_QueenIntroCamera.cs]
 Desc   : 女王(Boss02)出現時のカメラ演出。
          チェシャ猫(Boss01系)と同じ汎用シネマ基盤 CinemaTrack を継承し、
          カメラ駆動(cameraRoot のウェイポイント経路 / cameraLookAt 注視 /
          cameraCuts カット切替 / triggers ビート / onBegin·onEnd 入力凍結 /
          TPSカメラ退避)はそのまま流用する。
          その上に女王固有の演出を hook で上乗せする:
            - OnBegin  : プレイヤー凍結 + タイトル/レターボックス生成
            - LateUpdate: レターボックス開閉・ボス名フェード・カメラシェイク
            - OnEnd    : タイトル/帯フェードアウト + プレイヤー解除
            - SuspendCamera(false): 復帰時に ResyncFromCurrentView でスナップ防止

          セットアップ(Inspector):
            - Camera > CameraRoot に Boss2Movie_ 等の空 Transform(pos+rot)を子として並べる
            - Camera > CameraLookAt に女王、CameraCuts でカット/補間を切替
            - Playback > Duration / ProgressCurve で寄りの尺と緩急
            - Triggers に進捗地点の VFX/SE/アニメ発火を UnityEvent で(Cheshire流)
              → 便利メソッド Shake(float) / ShowTitle() / FireQueenAnim(string) も
                トリガから直接呼べる

          B2_Boss が Play(OnIntroComplete) を呼び、完了で戦闘開始。
          moverTarget を設定しなければカメラ単独再生になる。
 ------------------------------------------------------------------------------

 Date   : 2026/07/10
 Author : akatu

===============================================================================*/

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Boss02
{
    public class B2_QueenIntroCamera : CinemaTrack
    {
        [Header("女王タイトル / レターボックス")]
        [Tooltip("ボス名。空ならタイトル文字は出さない")]
        [SerializeField] private string bossName = "";
        [Tooltip("ボス名のフォント(TMP)")]
        [SerializeField] private TMP_FontAsset titleFont;
        [SerializeField] private float titleFontSize = 90f;
        [SerializeField] private Color titleColor = Color.white;
        [SerializeField] private Color titleOutlineColor = Color.black;
        [Tooltip("レターボックス帯の色")]
        [SerializeField] private Color letterboxColor = Color.black;
        [Tooltip("レターボックス帯1本の高さ(画面高に対する割合 0-0.5)。0で帯なし")]
        [SerializeField, Range(0f, 0.5f)] private float letterboxHeight = 0.12f;
        [Tooltip("レターボックス/タイトルのフェード時間(秒)")]
        [SerializeField] private float titleFade = 0.4f;
        [Tooltip("カメラ進捗(0-1)がこの値に達したらボス名を表示。1以上で自動表示しない")]
        [SerializeField, Range(0f, 1f)] private float titleProgress = 0.5f;
        [Tooltip("カメラ進捗(0-1)がこの値に達したらボス名をフェードアウトし始める。\n" +
                 "『名前が消え切ってからプレイヤーへ引く』には、引き始めの進捗より\n" +
                 "フェード時間ぶん手前に設定する(例: 引きが2/3=0.67からなら0.55)")]
        [SerializeField, Range(0f, 1f)] private float titleHideProgress = 0.55f;

        [Header("カメラシェイク")]
        [Tooltip("Shake(amplitude) をトリガから呼んだ時の既定継続時間(秒)")]
        [SerializeField] private float shakeDefaultDuration = 0.35f;

        [Header("女王アニメ")]
        [Tooltip("FireQueenAnim(trigger) で SetTrigger を送る女王の Animator")]
        [SerializeField] private Animator queenAnimator;

        [Header("ボス名(チェシャ猫式)")]
        [Tooltip("チェシャ猫式のボス名表示(B2_BossNameDisplay)。\n" +
                 "表示は前のショット(全体を映す引き)の onBegin が行い、\n" +
                 "ここでは titleHideProgress 到達でフェードアウトさせる。\n" +
                 "設定時は旧来の bossName(内蔵タイトル)は空にすること")]
        [SerializeField] private B2_BossNameDisplay bossNameDisplay;

        [Header("プレイヤー")]
        [Tooltip("演出中プレイヤーの操作を凍結する")]
        [SerializeField] private bool freezePlayer = true;

        [Header("番号付きカメラ点(自動収集)")]
        [Tooltip("ONなら『指定した名前+番号』のオブジェクトを番号順に集めてカメラ経路にする。" +
                 "CameraRoot を手で組まなくても、番号付きの空オブジェクトを置くだけで経路になる")]
        [SerializeField] private bool autoCollectNumberedWaypoints = true;
        [Tooltip("カメラ点の名前の頭。例『Boss2Camera_』なら Boss2Camera_1, Boss2Camera_2 ... を番号順に使う")]
        [SerializeField] private string waypointPrefix = "Boss2Camera_";
        [Tooltip("収集した点をまとめる親(自動生成/流用)。手動で割り当ててもよい")]
        [SerializeField] private Transform autoWaypointRoot;

        [Header("TPSカメラへの引き継ぎ")]
        [Tooltip("ONなら最終ウェイポイントを、TPSカメラが本来座る位置・姿勢へ自動で合わせる。\n" +
                 "プレイヤーがマウスで向けていた角度のまま滑らかに戻れる。\n" +
                 "※ 効かせるには Camera Look At を空にすること(注視点があると回転が上書きされ、\n" +
                 "   最後にTPSの向きへ飛んでしまう)")]
        [SerializeField] private bool snapLastWaypointToTpsHome = true;
        [Tooltip("設定すると『後ろへ引く間ずっとこの点(ボス)を向き、TPS定位置に着いてから\n" +
                 "プレイヤー向きへ振り向く』動きになる。\n" +
                 "仕組み: 末尾から2番目の点を TPS定位置へ寄せ、この点を向かせる。\n" +
                 "→ 最後の1区間が『その場で向き直すだけ』になる。\n" +
                 "未設定なら引きながら徐々にプレイヤー向きへ回る(振り向きが早い)")]
        [SerializeField] private Transform pullBackLookTarget;
        [Tooltip("演出終端からTPS操作視点へ滑らかに沈む時間(秒)。0で即引き継ぎ(スナップ)。\n" +
                 "Camera Look At にボスを入れておくと『ボスを向いて引く→最後にこの時間で操作視点へ沈む』になる")]
        [SerializeField] private float tpsSettleTime = 0.4f;
        [Tooltip("TPSへの移行が完全に終わってから、操作可能になるまでの待ち(秒)")]
        [SerializeField] private float controlDelayAfterSettle = 0.5f;

        // ランタイム
        private B2_IntroOverlay overlay;
        private PlayerLifetimeScope player;
        private TPSCameraController tps;
        private float introElapsed;
        private bool titleRequested;
        private bool titleShown;
        private float titleAlpha;
        private bool fadingOut;
        private bool nameFadeRequested;   // チェシャ猫式ボス名の消し込みを依頼済みか
        private bool settleDone = true;   // TPSへの移行(沈み込み)が終わったか

        // カメラシェイク
        private float shakeTimeLeft;
        private float shakeDurTotal;
        private float shakeAmp;

        // ------------------------------------------------------------------
        //  CinemaTrack hook
        // ------------------------------------------------------------------

        // 参照解決(基盤 Setup の先頭で呼ばれる)。camPath が組まれる前にここで
        // 番号付きの点を集めて cameraRoot を差し替えるので、番号を振って置くだけで経路になる。
        protected override void ResolveRefs()
        {
            base.ResolveRefs();
            if (autoCollectNumberedWaypoints) RebuildWaypointRoot();
            if (snapLastWaypointToTpsHome) SnapLastWaypointToTpsHome();
        }

        // 最終ウェイポイントを TPS カメラの定位置(ホームリグ姿勢)へ合わせる。
        // TPSCameraController.GetHomeFollowPose がこの用途のために用意されている:
        //   「イントロ演出の最終ウェイポイントをこの姿勢へ着地させると、
        //     ResyncFromCurrentView() 併用で後追い再開時にスナップが出ない」
        // yaw は TPS が保持している現在値(=プレイヤーがマウスで向けていた角度)を使うので、
        // 演出後もその角度のまま自然に操作へ戻れる。
        private void SnapLastWaypointToTpsHome()
        {
            if (tps == null) tps = TPSCameraController.Instance;
            if (tps == null) tps = FindObjectOfType<TPSCameraController>();
            if (tps == null || cameraRoot == null || cameraRoot.childCount == 0) return;

            if (cameraLookAt != null)
            {
                Debug.LogWarning("[B2_QueenIntroCamera] Camera Look At が設定されているため、" +
                    "Snap Last Waypoint To Tps Home が効きません(回転が注視点で上書きされ、" +
                    "最後にTPSの向きへ飛びます)。Camera Look At を空にしてください。", this);
                return;
            }

            Transform last = cameraRoot.GetChild(cameraRoot.childCount - 1);
            tps.GetHomeFollowPose(tps.CameraYaw, out Vector3 pos, out Quaternion rot);
            last.SetPositionAndRotation(pos, rot);

            // 末尾から2番目の点を TPS定位置と「同じ位置」に寄せ、ボスを向かせる。
            // すると最後の1区間(prev→last)は移動ゼロの純粋な向き直しになり、
            // 「ボスを向いたまま後ろへ引く → 着いてから振り向く」が成立する。
            if (pullBackLookTarget != null && cameraRoot.childCount >= 2)
            {
                Transform prev = cameraRoot.GetChild(cameraRoot.childCount - 2);
                prev.position = pos;
                Vector3 dir = pullBackLookTarget.position - pos;
                if (dir.sqrMagnitude > 1e-6f)
                    prev.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
        }

        // 再生開始: プレイヤー凍結 + タイトル/レターボックス生成。
        protected override void OnBegin()
        {
            if (freezePlayer)
            {
                player = FindObjectOfType<PlayerLifetimeScope>();
                if (player != null) player.SetUIMode(true);
            }

            // 上下の黒帯を閉じる。名前が出るのはこのトラックなので、ここが「名前表示の寸前」になる。
            // (帯は B2_Letterbox が持っている。プレハブからシーンのオブジェクトは
            //  参照できないため型で探す)
            B2_Letterbox letterbox = FindObjectOfType<B2_Letterbox>();
            if (letterbox != null) letterbox.Hide();

            overlay = B2_IntroOverlay.Create(bossName, titleFont, titleFontSize,
                titleColor, titleOutlineColor, letterboxColor, letterboxHeight);

            introElapsed = 0f;
            titleRequested = false;
            titleShown = false;
            titleAlpha = 0f;
            fadingOut = false;
            nameFadeRequested = false;
            settleDone = true;   // SuspendCamera(false) の沈み込み開始時に false になる
            ResetShake();
        }

        // 再生終了: タイトル/帯フェードアウト。
        // 操作の解禁は「TPSへの移行が完全に終わってから controlDelayAfterSettle 秒後」。
        // (カメラの TPS 復帰は SuspendCamera(false) 側で沈み込み付きで行う)
        protected override void OnEnd()
        {
            if (overlay != null) StartCoroutine(FadeOutOverlay());
            StartCoroutine(UnfreezeAfterSettle());
        }

        // TPSへの沈み込みが終わるのを待ち、さらに controlDelayAfterSettle 待ってから操作を返す
        private IEnumerator UnfreezeAfterSettle()
        {
            while (!settleDone) yield return null;
            if (controlDelayAfterSettle > 0f)
                yield return new WaitForSecondsRealtime(controlDelayAfterSettle);
            if (freezePlayer && player != null) player.SetUIMode(false);
        }

        // TPS カメラの退避/復帰。
        // 復帰時は「今の姿勢(ボスを向いてプレイヤーの後ろ)から TPS 定位置へ滑らかに寄せてから」
        // 引き継ぐ。これで引きの間はボスを向いたままにでき、最後だけ滑らかに操作視点へ沈む。
        protected override void SuspendCamera(bool suspend)
        {
            if (tps == null) tps = TPSCameraController.Instance;
            if (tps == null) tps = FindObjectOfType<TPSCameraController>();
            if (tps == null) return;

            if (suspend)
            {
                tps.enabled = false;
                return;
            }

            if (tpsSettleTime > 0f && camTf != null)
            {
                settleDone = false;
                StartCoroutine(SettleToTpsThenResume());
            }
            else
            {
                tps.ResyncFromCurrentView();
                tps.enabled = true;
                settleDone = true;
            }
        }

        // 演出終端の姿勢から TPS 定位置へ滑らかに移してから後追いを再開する。
        // GetHomeFollowPose には現在のカメラ向き(=プレイヤーの後ろ、-Z)の yaw を渡すので、
        // 定位置はプレイヤーの後方に来る(前に回り込んでプレイヤーの正面を映す事故を防ぐ)。
        private IEnumerator SettleToTpsThenResume()
        {
            tps.GetHomeFollowPose(camTf.eulerAngles.y, out Vector3 pos, out Quaternion rot);
            Vector3 p0 = camTf.position;
            Quaternion r0 = camTf.rotation;
            float dur = Mathf.Max(0.01f, tpsSettleTime);
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                camTf.position = Vector3.Lerp(p0, pos, k);
                camTf.rotation = Quaternion.Slerp(r0, rot, k);
                yield return null;
            }
            camTf.SetPositionAndRotation(pos, rot);
            tps.ResyncFromCurrentView(); // 既に定位置に居るのでスナップは出ない
            tps.enabled = true;
            settleDone = true;   // これを待って UnfreezeAfterSettle が操作を返す
        }

        // 基盤がカメラ姿勢を確定した後(コルーチン→LateUpdate)に、演出UIとシェイクを上乗せする。
        private void LateUpdate()
        {
            if (!IsCameraPlaying) return;

            float dt = Time.unscaledDeltaTime;
            introElapsed += dt;

            // レターボックス: 開始からフェードで開く
            if (overlay != null && !fadingOut)
            {
                overlay.SetBars(titleFade > 0f ? Mathf.Clamp01(introElapsed / titleFade) : 1f);

                // ボス名: titleProgress 到達 or ShowTitle() でフェードイン
                if (!titleShown && (titleRequested || CameraProgress >= titleProgress))
                    titleShown = true;
                if (titleShown)
                {
                    titleAlpha = Mathf.MoveTowards(titleAlpha, 1f, dt / Mathf.Max(0.001f, titleFade));
                    overlay.SetTitleAlpha(titleAlpha);
                }

                // ボス名を途中でフェードアウトする(titleHideProgress 到達で開始)。
                // 「名前が完全に消えてからプレイヤーへ引く」ための消し口。
                if (titleShown && CameraProgress >= titleHideProgress)
                    StartCoroutine(FadeOutOverlay());   // fadingOut が立つので二重開始しない
            }

            // チェシャ猫式ボス名(B2_BossNameDisplay)のフェードアウト。
            // 表示は前のショットが行っているので、ここは消すだけ
            if (!nameFadeRequested && bossNameDisplay != null && CameraProgress >= titleHideProgress)
            {
                nameFadeRequested = true;
                bossNameDisplay.FadeOutAndDispose();
            }

            // カメラシェイク: 基盤が置いた姿勢にオフセットを加算(毎フレーム基盤が置き直すので累積しない)
            if (shakeTimeLeft > 0f && camTf != null)
            {
                float falloff = shakeTimeLeft / shakeDurTotal;
                float amp = shakeAmp * falloff;
                Vector3 off = Random.insideUnitSphere * amp;
                camTf.position += camTf.rotation * off;            // カメラ空間で揺らす
                camTf.rotation *= Quaternion.Euler(off.y * 6f, off.x * 6f, 0f);

                shakeTimeLeft -= dt;
                if (shakeTimeLeft <= 0f) ResetShake();
            }
        }

        // タイトル・帯を同時にフェードアウトして破棄する。
        private IEnumerator FadeOutOverlay()
        {
            fadingOut = true;
            float dur = Mathf.Max(0.001f, titleFade);
            float t = 0f;
            while (t < dur)
            {
                if (overlay == null) yield break;
                float a = 1f - t / dur;
                overlay.SetTitleAlpha(a);
                overlay.SetBars(a);
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (overlay != null) { overlay.Dispose(); overlay = null; }
        }

        // ------------------------------------------------------------------
        //  トリガ/外部から呼べる便利メソッド(CinemaTrack の Triggers に配線可)
        // ------------------------------------------------------------------

        /// <summary>カメラシェイクを起こす。強さ amplitude、継続は shakeDefaultDuration。</summary>
        public void Shake(float amplitude) => AddShake(amplitude, shakeDefaultDuration);

        /// <summary>強さと継続時間を指定してカメラシェイクを起こす。</summary>
        public void ShakeFor(float amplitude, float duration) => AddShake(amplitude, duration);

        /// <summary>ボス名タイトルを今すぐ表示開始する(titleProgress を待たない)。</summary>
        public void ShowTitle() => titleRequested = true;

        /// <summary>女王の Animator へトリガを送る。</summary>
        public void FireQueenAnim(string trigger)
        {
            if (queenAnimator != null && !string.IsNullOrEmpty(trigger))
                queenAnimator.SetTrigger(trigger);
        }

        // ------------------------------------------------------------------
        //  シェイク内部
        // ------------------------------------------------------------------

        private void ResetShake() { shakeTimeLeft = shakeDurTotal = shakeAmp = 0f; }

        private void AddShake(float amplitude, float duration)
        {
            // 進行中の残り強度と比較し、強い方を採用(足し込みで暴れないように)
            float residual = shakeDurTotal > 0f ? shakeAmp * (shakeTimeLeft / shakeDurTotal) : 0f;
            shakeAmp = Mathf.Max(residual, amplitude);
            shakeDurTotal = Mathf.Max(0.01f, duration);
            shakeTimeLeft = shakeDurTotal;
        }

        // ------------------------------------------------------------------
        //  番号付きカメラ点の自動収集
        // ------------------------------------------------------------------

        // 『waypointPrefix + 整数』の名前を持つオブジェクトを番号順に集め、
        // autoWaypointRoot 配下へ並べて cameraRoot に設定する。
        // (向きは cameraLookAt 側で毎フレーム上書きされるため、点は「位置」だけ置けばよい)
        private void RebuildWaypointRoot()
        {
            if (string.IsNullOrEmpty(waypointPrefix)) return;

            var found = new List<KeyValuePair<int, Transform>>();
            foreach (Transform t in FindObjectsOfType<Transform>())
            {
                if (t == null) continue;
                if (t == autoWaypointRoot) continue;
                if (TryParseWaypointNumber(t.name, out int number))
                    found.Add(new KeyValuePair<int, Transform>(number, t));
            }
            if (found.Count == 0) return;

            // 番号順(数値比較なので _2 と _10 が正しく並ぶ)。同番号は名前で安定化。
            found.Sort((a, b) =>
            {
                int c = a.Key.CompareTo(b.Key);
                return c != 0 ? c : string.CompareOrdinal(a.Value.name, b.Value.name);
            });

            if (autoWaypointRoot == null)
            {
                var rootGo = new GameObject("AutoCameraRoot");
                autoWaypointRoot = rootGo.transform;
                autoWaypointRoot.SetParent(transform, false);
            }

            for (int i = 0; i < found.Count; i++)
            {
                Transform point = found[i].Value;
                if (point.parent != autoWaypointRoot) point.SetParent(autoWaypointRoot, true); // 位置は保つ
                point.SetSiblingIndex(i); // 子の並び順=経路順(基盤はこの順で点を読む)
            }

            SetCameraRoot(autoWaypointRoot);
        }

        // "Boss2Camera_3" → 3。接頭辞に一致し末尾が整数のときだけ true。
        private bool TryParseWaypointNumber(string goName, out int number)
        {
            number = 0;
            if (string.IsNullOrEmpty(goName) || !goName.StartsWith(waypointPrefix)) return false;
            string tail = goName.Substring(waypointPrefix.Length).Trim();
            return int.TryParse(tail, out number);
        }

#if UNITY_EDITOR
        // 実行しなくてもエディタ上で経路を確定させ、Gizmo に出す/保存する。
        [ContextMenu("番号付きカメラ点から経路を再構築")]
        private void EditorRebuildWaypoints()
        {
            RebuildWaypointRoot();
            if (Application.isPlaying) return;
            UnityEditor.EditorUtility.SetDirty(this);
            if (autoWaypointRoot != null) UnityEditor.EditorUtility.SetDirty(autoWaypointRoot.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif

        // 演出中に無効化されたら UI を残さない
        private void OnDisable()
        {
            if (overlay != null) { overlay.Dispose(); overlay = null; }
            if (freezePlayer && player != null) { player.SetUIMode(false); player = null; }
        }
    }
}
