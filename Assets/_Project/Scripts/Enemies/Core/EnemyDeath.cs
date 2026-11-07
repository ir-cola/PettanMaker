/**===========================================================================

 File   : 敵死亡クラス [EnemyDeath.cs]

 Author : Hayato Otsuki
 Date   : 2026/4/22
 -----------------------------------------------------------------------------

 ===========================================================================**/

using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/**===========================================================================
  敵死亡クラス
  死亡時につぶれアニメーションを再生し、地面にデカールを生成する
===========================================================================**/
public class EnemyDeath : MonoBehaviour {

    [Header("撃破 VFX 設定")]
    [SerializeField] private GameObject defeatVfxPrefab;
    [Tooltip("敵の向きを基準にしたワールドスケールの生成位置オフセット")]
    [SerializeField] private Vector3 defeatVfxOffset = Vector3.zero;

    [Header("デカール設定")]
    [SerializeField] private GameObject decalPrefab;
    [Tooltip("URP Decal シェーダーで作ったマテリアルをアサイン（テクスチャは実行時に差し替わる）")]
    [SerializeField] private Material decalMaterialTemplate;
    [SerializeField] private float decalSize = 2.0f;
    [SerializeField] private float decalYOffset = 0.05f;  // 地面からの僅かな浮かせ量
    [SerializeField] private float decalProjectionDepth = 1.5f;   // 地面への投影深度（小さいと貫通しない）

    [Header("つぶれ演出設定")]
    [SerializeField] private float crushDuration = 0.15f;
    [SerializeField] private float crushScaleY = 0.05f;
    [SerializeField] private float disappearDelay = 0.2f;

    [Header("ドロップ設定")]
    [SerializeField] private StickerData[] dropStickerTable;
    [Range(0f, 1f)]
    [SerializeField] private float dropRate = 0.8f;

    [Header("シール板ポリ設定")]
    [Tooltip("true: URP デカールの代わりにワールド板ポリ (World/StickerPeel) でシールを出す。\n" +
             "OccludablePolka 化した地面には DBuffer デカールが映らないためこちらを推奨")]
    [SerializeField] private bool useWorldQuadSticker = true;
    [SerializeField] private float stickerQuadSize = 1.6f;

    // コンポーネント参照
    private EnemyHealth enemyHealth;
    private EnemyBase   enemyBase;

    /**=======================================================================
      初期化
    =======================================================================**/
    void Awake() {
        enemyHealth = GetComponent<EnemyHealth>();
        enemyBase   = GetComponent<EnemyBase>();
    }

    /**=======================================================================
      開始処理
    =======================================================================**/
    void Start() {
        enemyHealth.onDied.AddListener(OnDied);
    }

    /**=======================================================================
      死亡時コールバック
    =======================================================================**/
    private void OnDied() {
        PlayDefeatedSE();
        SpawnDefeatVfx();
        StartCoroutine(DeathSequence());
    }

    /**=======================================================================
      撃破SEを再生する（EnemySettings.DefeatedSE。難易度解決後のsettingsを使用）
    =======================================================================**/
    private void PlayDefeatedSE() {
        EnemySettings s = enemyBase != null ? enemyBase.Settings : null;
        if (s != null && s.DefeatedSE != null)
            AudioSource.PlayClipAtPoint(s.DefeatedSE, transform.position, s.DefeatedSEVolume);
    }

    /**=======================================================================
      共通撃破 VFX を生成する
    =======================================================================**/
    private void SpawnDefeatVfx() {
        if (defeatVfxPrefab == null) return;

        Vector3 spawnPosition = transform.position + transform.rotation * defeatVfxOffset;
        GameObject instance = Instantiate(defeatVfxPrefab, spawnPosition, Quaternion.identity);
        Destroy(instance, GetVfxLifetime(instance));
    }

    /**=======================================================================
      子ParticleSystemを含む再生終了時刻を算出する
    =======================================================================**/
    private static float GetVfxLifetime(GameObject instance) {
        const float CleanupMargin = 0.5f;
        const float FallbackLifetime = 5f;

        float lifetime = 0f;
        ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem particle in particles) {
            ParticleSystem.MainModule main = particle.main;
            float endTime = main.startDelay.constantMax + main.duration + main.startLifetime.constantMax;
            lifetime = Mathf.Max(lifetime, endTime);
        }

        return (lifetime > 0f ? lifetime : FallbackLifetime) + CleanupMargin;
    }

    /**=======================================================================
      死亡演出コルーチン
    =======================================================================**/
    private IEnumerator DeathSequence() {
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        SnapToGround();

        yield return StartCoroutine(CrushAnimation());

        if (useWorldQuadSticker) SpawnStickerQuad();
        else                     SpawnCrushedDecal();

        Destroy(gameObject);
    }

    /**=======================================================================
      ワールド板ポリのシールを地面に生成する
      ステージ導入の WhiteBunnySticker と同じ見た目 (World/StickerPeel)。
      OccludablePolka 化した地面には URP デカールが映らないため、
      デカールではなく板ポリで出す。
    =======================================================================**/
    private void SpawnStickerQuad() {
        StickerData drop = DetermineDropSticker();
        if (drop == null || drop.decalTexture == null) {
            Debug.Log($"[EnemyDeath] ドロップなし（板ポリ生成スキップ, drop={drop?.stickerName ?? "null"}）");
            return;
        }

        // 地面の高さを取得
        Vector3 spawnPos = transform.position;
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 10.0f)
            && !hit.collider.CompareTag("Enemy")) {
            spawnPos = hit.point + Vector3.up * decalYOffset;
        }

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = $"StickerDrop_{drop.name}";
        Collider quadCol = quad.GetComponent<Collider>();
        if (quadCol != null) Destroy(quadCol);
        // 回収時の 3D カール剥がし (頂点変形) 用に分割グリッドへ差し替え
        quad.GetComponent<MeshFilter>().sharedMesh = StickerQuadMesh.Grid;

        quad.transform.position   = spawnPos;
        quad.transform.rotation   = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);   // 地面に寝かせる
        quad.transform.localScale = new Vector3(stickerQuadSize, stickerQuadSize, 1f);

        MeshRenderer mr = quad.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        mr.lightProbeUsage   = UnityEngine.Rendering.LightProbeUsage.Off;

        Shader stickerShader = Shader.Find("Sticker/World");
        Material mat = new Material(stickerShader != null ? stickerShader : Shader.Find("Sprites/Default"));
        mat.name = $"StickerDropMat_{drop.stickerName}";
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", drop.decalTexture);
        if (mat.HasProperty("_Peel"))    mat.SetFloat("_Peel", 0f);
        mr.sharedMaterial = mat;

        StickerPickup pickup = quad.AddComponent<StickerPickup>();
        pickup.Initialize(drop, drop.thumbnail);

        Debug.Log($"[EnemyDeath] シール板ポリ生成：{drop.stickerName}");
    }

    /**=======================================================================
      地面スナップ
    =======================================================================**/
    private void SnapToGround() {
        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 10.0f)) {
            if (!hit.collider.CompareTag("Enemy")) {
                Vector3 pos = transform.position;
                pos.y = hit.point.y;
                transform.position = pos;
            }
        }
    }

    /**=======================================================================
      つぶれアニメーション
    =======================================================================**/
    private IEnumerator CrushAnimation() {
        Vector3 originalScale = transform.localScale;
        Vector3 targetScale = new Vector3(
            originalScale.x * 1.3f,
            originalScale.y * crushScaleY,
            originalScale.z * 1.3f
        );

        float elapsed = 0f;
        while (elapsed < crushDuration) {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / crushDuration);
            transform.localScale = Vector3.Lerp(originalScale, targetScale, t);
            yield return null;
        }

        transform.localScale = targetScale;
        yield return new WaitForSeconds(disappearDelay);
    }

    /**=======================================================================
      地面デカールを生成する
    =======================================================================**/
    private void SpawnCrushedDecal() {
        if (decalPrefab == null) {
            Debug.LogWarning("[EnemyDeath] Decal Prefab が未設定です。");
            return;
        }

        // ドロップシールを先に抽選する（デカールテクスチャに使うため）
        StickerData drop = DetermineDropSticker();

        // 地面の高さを取得
        Vector3 spawnPos = transform.position;

        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 10.0f)) {
            if (!hit.collider.CompareTag("Enemy")) {
                spawnPos = hit.point + Vector3.up * decalYOffset;
            }
        }

        // URP DecalProjector は -Z 方向に投影するため、-X 90°回転でローカル-Zを下向きに
        // ※ HDRPは+Z投影だったが URPでは逆なので符号を反転している
        GameObject decalObj = Instantiate(decalPrefab, spawnPos, Quaternion.Euler(-90f, 0f, 0f));

        // DecalProjector の設定
        // Prefab が古い HDRP コンポーネントの場合は URP 版を動的に追加する
        DecalProjector projector = decalObj.GetComponent<DecalProjector>();
        if (projector == null) {
            projector = decalObj.AddComponent<DecalProjector>();
            Debug.Log("[EnemyDeath] DecalProjector を動的追加しました（Prefab を URP 版に更新してください）。");
        }
        if (projector != null) {
            projector.size = new Vector3(decalSize, decalSize, decalProjectionDepth);

            // StickerData の decalTexture をプロジェクターのマテリアルに適用する
            if (drop != null && drop.decalTexture != null) {
                // Inspector の decalMaterialTemplate → Prefab の material → null の優先順で使用
                Material baseMat = decalMaterialTemplate != null ? decalMaterialTemplate
                                 : projector.material;

                if (baseMat == null) {
                    Debug.LogWarning("[EnemyDeath] デカールマテリアルが未設定です。Inspector の 'Decal Material Template' に URP Decal マテリアルをアサインしてください。");
                }
                else {
                    Material matInstance = Object.Instantiate(baseMat);
                    matInstance.name = $"DecalMat_{drop.stickerName}";
                    SetDecalTexture(matInstance, drop.decalTexture);
                    projector.material = matInstance;
                    Debug.Log($"[EnemyDeath] デカールテクスチャ適用: {drop.decalTexture.name}");
                }
            }
            else {
                // テクスチャなしでもマテリアルだけ設定して形を表示する
                Material baseMat = decalMaterialTemplate != null ? decalMaterialTemplate
                                 : projector.material;
                if (baseMat != null) projector.material = baseMat;
                Debug.LogWarning($"[EnemyDeath] decalTexture が未設定です: drop={drop?.stickerName ?? "null"}");
            }
        }
        else {
            Debug.LogWarning("[EnemyDeath] decalPrefab に DecalProjector がありません。");
        }

        // StickerPickup がなければ自動追加
        StickerPickup pickup = decalObj.GetComponent<StickerPickup>();
        if (pickup == null) {
            pickup = decalObj.AddComponent<StickerPickup>();
        }

        // UI表示スプライトは StickerData.thumbnail を優先して使う
        Sprite displaySprite = drop?.thumbnail;

        pickup.Initialize(drop, displaySprite);

        Debug.Log(drop != null
            ? $"[EnemyDeath] デカール生成 & シールドロップ：{drop.stickerName}"
            : "[EnemyDeath] デカール生成（シールなし）");
    }

    /**=======================================================================
      デカールテクスチャをマテリアルにセットする
      URP Decal.shadergraph は "Base_Map"（アンダースコアなし）、
      カスタムシェーダーは "_BaseMap" などを使うため、HasProperty で判定する
    =======================================================================**/
    private static void SetDecalTexture(Material mat, Texture2D tex) {
        // 優先順: URP Decal.shadergraph → URP Lit 系 → HDRP 系 → フォールバック
        if      (mat.HasProperty("Base_Map"))      mat.SetTexture("Base_Map",      tex);
        else if (mat.HasProperty("_BaseMap"))      mat.SetTexture("_BaseMap",      tex);
        else if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", tex);
        else if (mat.HasProperty("_MainTex"))      mat.SetTexture("_MainTex",      tex);
        else                                       mat.mainTexture = tex;
    }

    /**=======================================================================
      ドロップするシールを抽選する
    =======================================================================**/
    private StickerData DetermineDropSticker() {
        Debug.Log("[EnemyDeath] ドロップシール抽選開始");

        if (dropStickerTable == null || dropStickerTable.Length == 0) {
            Debug.LogWarning("[EnemyDeath] dropStickerTable が空です。");
            return null;
        }

        Debug.Log($"[EnemyDeath] ドロップシール候補数: {dropStickerTable.Length}");

        if (Random.value > dropRate) return null;

        int index = Random.Range(0, dropStickerTable.Length);
        Debug.Log($"[EnemyDeath] ドロップシール決定: {dropStickerTable[index].stickerName}");
        return dropStickerTable[index];
    }
}
