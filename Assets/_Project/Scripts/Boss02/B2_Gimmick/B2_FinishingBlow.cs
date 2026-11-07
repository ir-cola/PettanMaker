/*=============================================================================

 File   : [B2_FinishingBlow.cs]
 Desc   : とどめの一撃。無限ダウン(B2_DownedState)中の女王に近づいて攻撃すると、
          巨大なシーリングスタンプで押し潰すムービーが走る。

          流れ:
            近接+攻撃 → プレイヤーがボスの頭上へジャンプ
            → 巨大スタンプが降ってきて圧殺(シェイク+衝撃波)
            → 白フェード。白い間に:
                 ボスを消す / ステージ中央にシールを落とす /
                 プレイヤーをシールの手前へ移動 / TPSカメラを戻す
            → 白明け → 操作解禁

          ■ 発動の入口
            PlayerAttack は攻撃対象が IsDead のとき「とどめ」を試す分岐を持つ。
            そこから TryFinishingBlow() が呼ばれる。B2_Health と同じ
            GameObject に付けること(GetComponent で拾われるため)。

          ■ シールについて
            EnemyDeath のシール板ポリ生成と同じ作り(Sticker/World シェーダー +
            StickerPickup)。拾えるようにするには StickerData アセットが要る。
            未設定でも板は出るが拾えない(見た目確認用)。

 ------------------------------------------------------------------------------

 Date   : 2026/07/17
 Author : akatu

===============================================================================*/

using System.Collections;
using UnityEngine;

namespace Boss02
{
    /// <summary>ダウン中の女王にとどめを刺すムービー。</summary>
    public class B2_FinishingBlow : MonoBehaviour
    {
        [Header("発動条件")]
        [Tooltip("この距離までプレイヤーが近づいて攻撃するととどめが発動する。\n" +
                 "遠くから殴っただけでは発動しない(近づく動機を作るため)")]
        [SerializeField] private float activationRange = 3f;

        [Header("参照")]
        [Tooltip("未設定なら同じGameObjectから取得")]
        [SerializeField] private B2_Health health;
        [Tooltip("消すボス本体。未設定ならこの GameObject")]
        [SerializeField] private GameObject bossRoot;
        [Tooltip("白フェード用の B2_ScreenFade。未設定ならシーンから探すが、\n" +
                 "暗転用(黒)と別インスタンスにして色を白にしておくこと")]
        [SerializeField] private B2_ScreenFade whiteFade;

        [Header("プレイヤーのジャンプ")]
        [Tooltip("ボスのどの高さまで飛び上がるか(ワールドY)")]
        [SerializeField] private float playerJumpHeight = 7f;
        [Tooltip("ジャンプにかける時間(秒)")]
        [SerializeField] private float jumpDuration = 0.5f;
        [Tooltip("ボスからどれだけ手前で踏み切るか(ボス中心からの距離)")]
        [SerializeField] private float jumpStandoff = 1.5f;

        [Header("スタンプ")]
        [Tooltip("巨大シーリングスタンプのプレハブ。未設定ならスタンプは出ない(潰しだけ起きる)")]
        [SerializeField] private GameObject stampPrefab;
        [Tooltip("スタンプの大きさ(ボス比)")]
        [SerializeField] private float stampScale = 3f;
        [Tooltip("スタンプが現れる高さ(ワールドY)")]
        [SerializeField] private float stampStartHeight = 20f;
        [Tooltip("スタンプが落ち切るまでの時間(秒)")]
        [SerializeField] private float stampFallDuration = 0.3f;
        [Tooltip("押し潰した後スタンプが残る時間(秒)。白フェード中に消える")]
        [SerializeField] private float stampStayTime = 0.4f;

        [Header("衝撃")]
        [SerializeField] private GameObject shockwaveVfxPrefab;
        [SerializeField] private float shakeDuration = 0.5f;
        [SerializeField] private float shakeIntensity = 0.4f;
        [SerializeField] private AudioSource seSource;
        [Tooltip("押し潰した瞬間のSE (Boss02_Explosion など)")]
        [SerializeField] private AudioClip stampSe;

        [Header("白フェード")]
        [SerializeField] private float whiteInDuration = 0.15f;
        [Tooltip("真っ白のまま保持する時間(秒)。この間にボスを消してシールを置く")]
        [SerializeField] private float whiteHold = 0.4f;
        [SerializeField] private float whiteOutDuration = 0.5f;

        [Header("シール")]
        [Tooltip("ドロップするシール(女王服)。未設定だと拾えない板だけが出る")]
        [SerializeField] private StickerData stickerData;
        [Tooltip("シールを落とす位置(ステージ中央)")]
        [SerializeField] private Vector3 stickerPosition = Vector3.zero;
        [Tooltip("シール板の大きさ")]
        [SerializeField] private float stickerSize = 5f;
        [Tooltip("シールを地面へ置くときの浮かせ量(Zファイティング防止)")]
        [SerializeField] private float stickerYOffset = 0.05f;

        [Header("プレイヤーの最終位置")]
        [Tooltip("ムービー後にプレイヤーを置く位置(シールの少し手前)")]
        [SerializeField] private Vector3 playerFinalPosition = new Vector3(0f, 0f, 5f);

        private bool played;

        private void Awake()
        {
            if (health == null) health = GetComponent<B2_Health>();
            if (bossRoot == null) bossRoot = gameObject;
        }

        /// <summary>
        /// とどめを試みる。PlayerAttack の「対象が IsDead」分岐から呼ばれる。
        /// 条件を満たさなければ false を返して普通の空振り扱いにする。
        /// </summary>
        public bool TryFinishingBlow()
        {
            if (played) return false;
            if (health == null || !health.IsDead) return false;

            PlayerLifetimeScope player = FindObjectOfType<PlayerLifetimeScope>();
            if (player == null) return false;

            // 遠くから殴っても発動しない。近づかせるための条件
            if (Vector3.Distance(player.transform.position, transform.position) > activationRange) return false;

            played = true;
            StartCoroutine(MovieRoutine(player));
            return true;
        }

        private IEnumerator MovieRoutine(PlayerLifetimeScope player)
        {
            // 演出中に殴られたり死亡処理が動いたりしないよう固める
            health.Invulnerable = true;
            player.SetUIMode(true);

            if (whiteFade == null) whiteFade = FindWhiteFade();

            // --- プレイヤーがボスの頭上へジャンプ ---
            Vector3 bossPos = transform.position;
            Vector3 from = player.transform.position;
            // ボス中心の少し手前・真上。踏み切り位置はボスから見てプレイヤー側に取る
            Vector3 horizDir = from - bossPos;
            horizDir.y = 0f;
            if (horizDir.sqrMagnitude < 0.01f) horizDir = Vector3.forward;
            horizDir.Normalize();
            Vector3 apex = bossPos + horizDir * jumpStandoff;
            apex.y = playerJumpHeight;

            yield return MovePlayerArc(player.transform, from, apex, jumpDuration);

            // --- 巨大スタンプが降ってくる ---
            GameObject stamp = null;
            if (stampPrefab != null)
            {
                Vector3 stampStart = new Vector3(bossPos.x, stampStartHeight, bossPos.z);
                stamp = Instantiate(stampPrefab, stampStart, Quaternion.identity);
                stamp.transform.localScale *= stampScale;

                Vector3 stampEnd = new Vector3(bossPos.x, bossPos.y, bossPos.z);
                float t = 0f;
                float dur = Mathf.Max(0.01f, stampFallDuration);
                while (t < dur)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / dur);
                    stamp.transform.position = Vector3.Lerp(stampStart, stampEnd, k * k); // 加速して落ちる
                    yield return null;
                }
                stamp.transform.position = stampEnd;
            }

            // --- 圧殺の瞬間 ---
            if (seSource != null && stampSe != null) seSource.PlayOneShot(stampSe);
            if (shakeDuration > 0f && shakeIntensity > 0f) CameraShake.Play(shakeDuration, shakeIntensity);
            if (shockwaveVfxPrefab != null)
            {
                GameObject wave = Instantiate(shockwaveVfxPrefab, bossPos, Quaternion.identity);
                Destroy(wave, 3f);
            }

            if (stampStayTime > 0f) yield return new WaitForSeconds(stampStayTime);

            // --- 白フェード。白い間に入れ替える ---
            if (whiteFade != null) yield return whiteFade.FadeToBlack(whiteInDuration); // 色は白に設定してある

            if (stamp != null) Destroy(stamp);
            bossRoot.SetActive(false);          // ボス消滅。Destroy でなく非表示にして参照切れを避ける
            SpawnSticker();
            PlacePlayer(player);

            if (whiteHold > 0f) yield return new WaitForSecondsRealtime(whiteHold);
            if (whiteFade != null) yield return whiteFade.FadeFromBlack(whiteOutDuration);

            // --- 操作解禁 ---
            player.SetUIMode(false);
        }

        // 放物線でプレイヤーを飛ばす。CharacterController は SetUIMode(true) で
        // 止まっているので、transform を直接動かして問題ない
        private IEnumerator MovePlayerArc(Transform player, Vector3 from, Vector3 to, float duration)
        {
            float dur = Mathf.Max(0.01f, duration);
            float t = 0f;
            float peak = Mathf.Max(from.y, to.y) + 2f;   // 山なりに見せるための頂点
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                Vector3 p = Vector3.Lerp(from, to, k);
                // 高さだけ放物線を足して跳ね上がりを出す
                p.y = Mathf.Lerp(from.y, to.y, k) + Mathf.Sin(k * Mathf.PI) * (peak - Mathf.Max(from.y, to.y));
                player.position = p;
                yield return null;
            }
            player.position = to;
        }

        // ステージ中央の地面にシール板を置く。EnemyDeath のシール生成と同じ作り
        private void SpawnSticker()
        {
            Vector3 pos = stickerPosition;
            if (Physics.Raycast(stickerPosition + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f,
                    ~0, QueryTriggerInteraction.Ignore))
            {
                pos = hit.point;
            }
            pos += Vector3.up * stickerYOffset;

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "QueenDressStickerDrop";
            Collider col = quad.GetComponent<Collider>();
            if (col != null) Destroy(col);
            // 回収時のカール剥がし(頂点変形)用に分割グリッドへ差し替え
            quad.GetComponent<MeshFilter>().sharedMesh = StickerQuadMesh.Grid;

            quad.transform.position = pos;
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // 地面に寝かせる
            quad.transform.localScale = new Vector3(stickerSize, stickerSize, 1f);

            MeshRenderer mr = quad.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            Shader stickerShader = Shader.Find("Sticker/World");
            Material mat = new Material(stickerShader != null ? stickerShader : Shader.Find("Sprites/Default"));
            mat.name = "QueenDressStickerMat";
            Texture tex = stickerData != null ? stickerData.decalTexture : null;
            if (tex != null && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_Peel")) mat.SetFloat("_Peel", 0f);
            mr.sharedMaterial = mat;

            if (stickerData != null)
            {
                StickerPickup pickup = quad.AddComponent<StickerPickup>();
                pickup.Initialize(stickerData, stickerData.thumbnail);
            }
            else
            {
                Debug.LogWarning("[B2_FinishingBlow] Sticker Data が未設定です。シールは出ますが拾えません。", this);
            }
        }

        // シールの手前にプレイヤーを立たせ、シールの方を向かせる
        private void PlacePlayer(PlayerLifetimeScope player)
        {
            Vector3 pos = playerFinalPosition;
            if (Physics.Raycast(playerFinalPosition + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f,
                    ~0, QueryTriggerInteraction.Ignore))
            {
                pos = hit.point;
            }
            player.transform.position = pos;

            Vector3 look = stickerPosition - pos;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f) player.transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);

            // TPSカメラを新しい位置へ追従させ直す(演出でプレイヤーを飛ばしたため)
            TPSCameraController tps = TPSCameraController.Instance;
            if (tps == null) tps = FindObjectOfType<TPSCameraController>();
            if (tps != null)
            {
                tps.GetHomeFollowPose(player.transform.eulerAngles.y, out Vector3 camPos, out Quaternion camRot);
                tps.transform.SetPositionAndRotation(camPos, camRot);
                tps.ResyncFromCurrentView();
            }
        }

        // 白フェード用の B2_ScreenFade を探す。暗転用(黒)と紛れないよう、
        // 色が白に近いものを優先する
        private B2_ScreenFade FindWhiteFade()
        {
            B2_ScreenFade[] fades = FindObjectsOfType<B2_ScreenFade>();
            foreach (B2_ScreenFade f in fades)
            {
                if (f.FadeColor.r > 0.5f && f.FadeColor.g > 0.5f && f.FadeColor.b > 0.5f) return f;
            }
            if (fades.Length > 0)
            {
                Debug.LogWarning("[B2_FinishingBlow] 白フェード用の B2_ScreenFade が見つからないため、" +
                                 "暗転用を流用します。白にしたい場合は Fade Color=白 の別インスタンスを用意してください。", this);
                return fades[0];
            }
            return null;
        }

#if UNITY_EDITOR
        [ContextMenu("とどめを強制発動")]
        private void DebugForce()
        {
            if (!Application.isPlaying) { Debug.LogWarning("再生中のみ"); return; }
            PlayerLifetimeScope player = FindObjectOfType<PlayerLifetimeScope>();
            if (player == null) return;
            played = true;
            StartCoroutine(MovieRoutine(player));
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, activationRange);
            Gizmos.color = new Color(0.3f, 1f, 0.5f);
            Gizmos.DrawWireSphere(stickerPosition, 0.4f);
            Gizmos.DrawLine(stickerPosition, playerFinalPosition);
            Gizmos.color = new Color(0.3f, 0.8f, 1f);
            Gizmos.DrawWireSphere(playerFinalPosition, 0.4f);
        }
#endif
    }
}
