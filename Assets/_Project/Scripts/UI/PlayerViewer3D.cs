/**===========================================================================

 File   : �v���C���[3D�r���[�A�N���X [PlayerViewer3D.cs]

 Author : Hayato Otsuki
 Date   : 2026/4/22
 -----------------------------------------------------------------------------

 TAB�ŃV�[�������J�����Ƃ��A���p�l���� RawImage ��
 ���ۂ̃V�[���̃v���C���[���f���B
 �}�E�X�h���b�O�ŃJ�������v���C���[�̎���Ɏ���ł���B

===========================================================================**/

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PlayerViewer3D : MonoBehaviour, IPointerDownHandler, IDragHandler, IDropHandler, IScrollHandler {

    [Header("�����_�����O�ݒ�")]
    [SerializeField] private Camera viewerCamera;
    [SerializeField] private RawImage displayImage;
    [SerializeField] private Vector2Int renderSize = new Vector2Int(512, 512);

    [Header("Preview Isolation (pink panel)")]
    [Tooltip("Book を開いている間だけプレイヤーをこのレイヤーへ一時移動し、ビューアカメラで人物のみを描画する")]
    [SerializeField] private string previewLayerName = "PlayerPreview";
    [SerializeField] private Color backgroundColor = new Color(0.97f, 0.71f, 0.81f, 1f);

    [Header("�J�����ݒ�")]
    [SerializeField] private float distanceFromPlayer = 3.0f;
    [SerializeField] private float heightOffset = 1.0f;
    [SerializeField] private float initialYaw = 25.0f;   // 正面やや斜め（着せ替えプレビュー向き）
    [SerializeField] private float rotationSpeed = 0.4f;

    [Header("ズーム設定")]
    [SerializeField] private float zoomSpeed    = 0.3f;   // スクロール1単位あたりの移動量
    [Tooltip("最至近 = 全身フィット距離 × この倍率 (どこまで寄れるか。小さいほど顔アップ可)")]
    [SerializeField] private float zoomInRatio  = 0.45f;
    [Tooltip("最遠 = 全身フィット距離 × この倍率 (どこまで引けるか。大きすぎると豆粒になる)")]
    [SerializeField] private float zoomOutRatio = 1.35f;

    [Header("自動フレーミング (開くたび全身が綺麗に収まる画角)")]
    [Tooltip("全身フィット距離への余白倍率 (1=画面いっぱい、大きいほど余白)")]
    [SerializeField] private float frameMargin = 1.15f;

    private RenderTexture renderTexture;
    private Transform playerTransform;
    private float currentYaw;
    private float initialDistance;   // バウンズが取れないときのフォールバック定位置
    private float fitDistance;       // 全身が綺麗に収まる自動計算距離 (Activate/着せ替えで更新)
    private Vector2 lastPointerPos;
    private Vector3 fixedLookTarget;   // Activate 時に固定する注視点

    // �V�[���\�t�Ǘ��iActivate ���Ƀv���C���[����擾�j
    private PlayerPasteManager  pasteManager;
    private PlayerModelSwitcher modelSwitcher;

    private readonly System.Collections.Generic.Dictionary<Renderer,int> savedLayers = new System.Collections.Generic.Dictionary<Renderer,int>();
    private bool viewerActive;

    [Header("パネル装飾（角丸＋アウトライン）")]
    [SerializeField] private Shader roundedOutlineShader;   // "UI/RoundedOutline"（未設定なら Shader.Find）
    [SerializeField] private float cornerRadius = 40f;
    [SerializeField] private float outlineWidth = 6f;
    [SerializeField] private Color outlineColor = Color.white;

    [Header("背景タイル（右下→左上へ流れる）")]
    [SerializeField] private Color tileColorA = new Color(0.98f, 0.80f, 0.88f, 1f);
    [SerializeField] private Color tileColorB = new Color(0.93f, 0.60f, 0.74f, 1f);
    [SerializeField] private float tileTiling = 3.5f;                       // 画面内のタイル数（テクスチャは2×2）
    [SerializeField] private Vector2 tileScrollDir = new Vector2(1f, -1f);  // UVオフセット方向（見た目は右下→左上へ流れる）
    [SerializeField] private float tileScrollSpeed = 0.10f;

    [Header("中ドラッグで上下パン（視線は水平のまま）")]
    [Tooltip("中ドラッグ1pxあたりの上下移動量（m）")]
    [SerializeField] private float panSpeed = 0.004f;
    [SerializeField] private float panMin = -0.9f;   // 下（足元）
    [SerializeField] private float panMax = 1.1f;    // 上（顔）

    private Material  panelMat;
    private Material  tileMat;
    private Texture2D tileTex;
    private Transform backdrop;
    private float     lookHeightOffset;   // 中ドラッグの上下量（視線は水平を維持）
    private bool      middleLooking;
    private Vector2   lastMousePos;

    [Header("着替え時のポワッ演出（ビューポート内で再生）")]
    [Tooltip("着替え確定時にプレビュープレイヤー位置で再生するエフェクト（CFXR Magic Poof 等）")]
    [SerializeField] private GameObject changePoofPrefab;
    [Tooltip("エフェクトのスケール")]
    [SerializeField] private float changePoofScale = 1f;
    [Tooltip("プレイヤー中心（注視点）からのワールドオフセット")]
    [SerializeField] private Vector3 changePoofOffset = Vector3.zero;
    [Tooltip("エフェクトの自動破棄までの秒（実時間）")]
    [SerializeField] private float changePoofLifetime = 3f;

    private GameObject lastClothForPoof;   // 着替え検知用（前フレームの装備服）

    void Awake() {
        currentYaw = initialYaw;
        initialDistance = distanceFromPlayer;
        BuildRenderTexture();
    }

    private void BuildRenderTexture() {
        if (renderTexture != null) renderTexture.Release();
        renderTexture = new RenderTexture(renderSize.x, renderSize.y, 24);

        if (viewerCamera != null) {
            viewerCamera.targetTexture = renderTexture;
            viewerCamera.clearFlags = CameraClearFlags.SolidColor;
            viewerCamera.backgroundColor = backgroundColor;
            viewerCamera.gameObject.SetActive(false);
        }

        if (displayImage != null) {
            displayImage.texture = renderTexture;
        }

        BuildPanelDecor();
        BuildBackdrop();
    }

    public void Activate() {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) {
            playerTransform = playerObj.transform;
            pasteManager  = playerObj.GetComponent<PlayerPasteManager>();
            modelSwitcher = playerObj.GetComponent<PlayerModelSwitcher>();
            // �v���C���[�̑S Renderer �̃o�E���Y�����Z���Ď��o�I�Ȓ��S�����߂�
            fixedLookTarget = ComputeStablePivot(playerObj);
        }
        // 開いた瞬間の装備を基準にしておく（開いただけでポワッが出ないように）
        lastClothForPoof = modelSwitcher != null ? modelSwitcher.CurrentClothPrefab : null;

        viewerActive = true;
        ApplyPreviewLayer();

        // 毎回プレイヤーの正面（やや斜め）を定位置で映す。プレイヤーの世界向きへ追従させ、
        // 開くたびに常に正面から同じ画角になるようにする（背面/側面や前回のズームを持ち越さない）。
        currentYaw = (playerTransform != null ? playerTransform.eulerAngles.y : 0f) + initialYaw;
        lookHeightOffset = 0f;
        // Renderer バウンズ + カメラFOV から「全身が綺麗に収まる距離」を毎回計算する。
        // 体格 (服スキルの拡縮・衣装のシルエット) が変わっても常に同じ見え方になる。
        fitDistance = ComputeFitDistance();
        distanceFromPlayer = fitDistance;

        if (viewerCamera != null) {
            viewerCamera.gameObject.SetActive(true);
            UpdateCameraPosition();
        }
    }

    /**=======================================================================
      全身がビューポートに綺麗に収まるカメラ距離を計算する。
      縦 = バウンズ高さ / tan(FOV/2)、横 = 幅 / (tan(FOV/2)×アスペクト) の
      大きい方 + 奥行き分。frameMargin で余白を掛ける。
    =======================================================================**/
    private float ComputeFitDistance() {
        if (playerTransform == null || viewerCamera == null) return initialDistance;
        if (!TryGetBodyBounds(playerTransform.gameObject, out Bounds b)) return initialDistance;

        float halfFovTan = Mathf.Tan(viewerCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        if (halfFovTan < 0.0001f) return initialDistance;
        float aspect = renderSize.y != 0 ? (float)renderSize.x / renderSize.y : 1f;

        float halfH = Mathf.Max(0.1f, b.extents.y);
        float halfW = Mathf.Max(0.1f, Mathf.Max(b.extents.x, b.extents.z));
        float distH = halfH / halfFovTan;
        float distW = halfW / (halfFovTan * aspect);
        // 距離は注視点(中心)基準なので、手前に張り出す奥行き分 (≒halfW) を足す
        return (Mathf.Max(distH, distW) + halfW) * frameMargin;
    }

    public void Deactivate() {
        viewerActive = false;
        RestoreLayers();
        if (viewerCamera != null) viewerCamera.gameObject.SetActive(false);
    }

    void LateUpdate() {
        if (viewerActive) {
            UpdatePanelMaterial();
            ScrollTiles();
            DetectClothChangeForPoof();   // 着替え確定を検知してポワッを出す（全着替え経路を一括カバー）
        }
        if (viewerCamera == null || !viewerCamera.gameObject.activeSelf) return;
        if (playerTransform == null) return;
        // ���t���[���o�E���Y���S���X�V���ăv���C���[����ɉ�ʒ����ɉf��悤�ɂ���
        // 注視点は Activate / 着せ替え時に確定した安定ピボットのまま（毎フレーム更新しない＝回転がぶれない）
        UpdateCameraPosition();
    }

    private void UpdateCameraPosition() {
        // Activate ���ɕۑ������Œ�_���g���A�v���C���[�������Ă��J�����͓����Ȃ�
        // 中ドラッグで注視点を上下に平行移動。カメラも同じ高さに置き、視線は水平（まっすぐ）を保つ。
        Vector3 target = fixedLookTarget + Vector3.up * lookHeightOffset;
        Quaternion rot = Quaternion.Euler(0f, currentYaw, 0f);
        Vector3 offset = rot * new Vector3(0f, 0f, distanceFromPlayer);
        viewerCamera.transform.position = target + offset;
        viewerCamera.transform.LookAt(target);
    }

    /**=======================================================================
      �v���C���[�̑S Renderer �̃o�E���Y���S�����߂�
      �����I�t�Z�b�g��萳�m�Ƀv���C���[�̎��o�I�Ȓ��S��Ԃ�
    =======================================================================**/
    private Vector3 GetPlayerVisualCenter(GameObject playerObj) {
        if (!TryGetBodyBounds(playerObj, out Bounds bounds)) {
            return playerObj.transform.position + Vector3.up * heightOffset;
        }
        return bounds.center;
    }

    /// <summary>体のメッシュだけのバウンズ。演出用 (パーティクル/トレイル/VFXグラフ/非表示) は
    /// 画角・注視点をぶらすので除外する。特に走り土煙 (VFXRenderer) は停止中も固定バウンズ
    /// (足元中心の 1x1x1) を報告し、フレーミングを下・遠へずらすので必ず除外する。</summary>
    private static bool TryGetBodyBounds(GameObject playerObj, out Bounds bounds) {
        bounds = default;
        bool has = false;
        foreach (Renderer r in playerObj.GetComponentsInChildren<Renderer>()) {
            if (!r.enabled) continue;
            if (r is ParticleSystemRenderer || r is TrailRenderer
                || r is UnityEngine.VFX.VFXRenderer) continue;
            if (!has) { bounds = r.bounds; has = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return has;
    }

    // 回転ピボット：水平はプレイヤーの中心軸(root)に固定、高さは見た目の中心。
    // これで左ドラッグの回転が「その場でクルッ」と中心回りになる（ぶれ・弧を描く動きを防ぐ）。
    private Vector3 ComputeStablePivot(GameObject playerObj) {
        Vector3 vc = GetPlayerVisualCenter(playerObj);
        return new Vector3(playerObj.transform.position.x, vc.y, playerObj.transform.position.z);
    }

    public void OnPointerDown(PointerEventData eventData) {
        lastPointerPos = eventData.position;
    }

    public void OnDrag(PointerEventData eventData) {
        if (eventData.button != PointerEventData.InputButton.Left) return;   // ヨーは左ドラッグのみ（中は上下視点）
        Vector2 delta = eventData.position - lastPointerPos;
        lastPointerPos = eventData.position;
        currentYaw += delta.x * rotationSpeed;
        UpdateCameraPosition();
    }

    /**=======================================================================
      �X�N���[������
      �}�E�X�z�C�[���Ńr���[�A���g�k����
    =======================================================================**/
    public void OnScroll(PointerEventData eventData) {
        // ズーム範囲は「全身フィット距離」比で決める: 寄り = 顔アップ程度まで、
        // 引き = 全身+少しの余白まで (豆粒になるほど引けない)。
        float fit = fitDistance > 0.01f ? fitDistance : initialDistance;
        distanceFromPlayer -= eventData.scrollDelta.y * zoomSpeed;
        distanceFromPlayer  = Mathf.Clamp(distanceFromPlayer, fit * zoomInRatio, fit * zoomOutRatio);
        UpdateCameraPosition();
    }

    /**=======================================================================
      �h���b�v����
      �V�[�����r���[�A��Ƀh���b�v���ꂽ��A�r���[�A�J������Raycast����
      �v���C���[�̑̂̏�̏Փ˓_�ɃV�[����\��
    =======================================================================**/
    public void OnDrop(PointerEventData eventData) {
        StickerDragHandler dragHandler = eventData.pointerDrag?.GetComponent<StickerDragHandler>();
        if (dragHandler == null || dragHandler.StickerData == null) return;

        StickerData droppedData = dragHandler.StickerData;

        // 服シール → モデル切り替え（デカールは貼らない）
        if (droppedData.stickerType == StickerType.Clothes) {
            if (modelSwitcher != null && droppedData.equipPrefab != null)
                modelSwitcher.SwitchModel(droppedData.equipPrefab);
            else
                Debug.LogWarning("[PlayerViewer3D] PlayerModelSwitcher 未設定、または equipPrefab が null です。");
            return;
        }

        if (viewerCamera == null || pasteManager == null) return;

        //�X�N���[�����W �� �r���[�A�J�����̃r���[�|�[�g���W�ɕϊ�
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                displayImage.rectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint)) return;

        Rect rect = displayImage.rectTransform.rect;
        float u = (localPoint.x - rect.x) / rect.width;
        float v = (localPoint.y - rect.y) / rect.height;

        // �r���[�A�J�������� Ray �𐶐����� PlaceSticker �ɓn��
        // �iBakeMesh �ɂ��r�W���A�����b�V���ւ̓��e�� PlayerPasteManager ���Ŏ��{�j
        Ray ray = viewerCamera.ViewportPointToRay(new Vector3(u, v, 0f));
        pasteManager.PlaceSticker(ray, droppedData);
    }

    /**=======================================================================
      �h���b�O���̗L�����₢���킹
      �X�N���[�����W���r���[�A���Ȃ烌�C���΂��A�\��邩�ǂ�����Ԃ��B
      onModel=false �Ȃ�r���[�A�O�܂��̓��f���O�i�F�ύX�s�v�j
    =======================================================================**/
    public bool QueryPasteValidity(Vector2 screenPos, Camera uiCamera, out bool onModel) {
        onModel = false;
        if (viewerCamera == null || pasteManager == null) return false;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                displayImage.rectTransform, screenPos, uiCamera, out Vector2 localPoint))
            return false;

        Rect rect = displayImage.rectTransform.rect;
        // �r���[�A��`�O�Ȃ画�肵�Ȃ�
        if (!rect.Contains(localPoint)) return false;

        float u = (localPoint.x - rect.x) / rect.width;
        float v = (localPoint.y - rect.y) / rect.height;
        Ray ray = viewerCamera.ViewportPointToRay(new Vector3(u, v, 0f));
        return pasteManager.QueryPasteValidity(ray, out onModel);
    }

    /// <summary>スクリーン座標がビューア RawImage 内かどうかを返す（BakeMesh なし軽量版）</summary>
    public bool IsPointerOverViewer(Vector2 screenPos, Camera uiCamera) {
        if (displayImage == null) return false;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                displayImage.rectTransform, screenPos, uiCamera, out Vector2 localPoint))
            return false;
        return displayImage.rectTransform.rect.Contains(localPoint);
    }

    /**=======================================================================
      プレイヤー(＋着せ替え衣装)を専用プレビューレイヤーへ移し、ビューア
      カメラの描画対象を人物のみに絞る。着せ替えでメッシュが増えても追従。
    =======================================================================**/
    public void RefreshPreviewLayer() {
        if (!viewerActive) return;
        ApplyPreviewLayer();
        if (playerTransform != null) fixedLookTarget = ComputeStablePivot(playerTransform.gameObject);
        // 着せ替えで体格・シルエットが変わるので、全身フィット距離も取り直して定位置へ
        fitDistance = ComputeFitDistance();
        distanceFromPlayer = fitDistance;
    }

    private void ApplyPreviewLayer() {
        if (playerTransform == null) return;
        int layer = LayerMask.NameToLayer(previewLayerName);
        if (layer < 0) {
            Debug.LogWarning("[PlayerViewer3D] レイヤー \"" + previewLayerName + "\" が未定義です。Tags & Layers に追加してください。");
            return;
        }
        var rends = playerTransform.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends) {
            if (!savedLayers.ContainsKey(r)) savedLayers[r] = r.gameObject.layer;
            r.gameObject.layer = layer;
        }
        if (viewerCamera != null) viewerCamera.cullingMask = 1 << layer;
    }

    private void RestoreLayers() {
        foreach (var kv in savedLayers)
            if (kv.Key != null) kv.Key.gameObject.layer = kv.Value;
        savedLayers.Clear();
    }

    // ---- 中クリックで上下視点（新Input System をポーリング） -------------
    void Update() {
        if (!viewerActive) return;
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.middleButton.wasPressedThisFrame &&
            IsPointerOverViewer(mouse.position.ReadValue(), null)) {
            middleLooking = true;
            lastMousePos = mouse.position.ReadValue();
        }
        if (mouse.middleButton.wasReleasedThisFrame) middleLooking = false;

        if (middleLooking && mouse.middleButton.isPressed) {
            Vector2 pos = mouse.position.ReadValue();
            lookHeightOffset = Mathf.Clamp(
                lookHeightOffset - (pos.y - lastMousePos.y) * panSpeed, panMin, panMax);
            lastMousePos = pos;
            UpdateCameraPosition();
        }
    }

    // ---- パネル装飾（角丸＋白アウトライン） -------------------------------
    private void BuildPanelDecor() {
        if (displayImage == null) return;
        Shader sh = roundedOutlineShader != null ? roundedOutlineShader : Shader.Find("UI/RoundedOutline");
        if (sh == null) { Debug.LogWarning("[PlayerViewer3D] UI/RoundedOutline シェーダーが見つかりません。"); return; }
        panelMat = new Material(sh);
        if (renderTexture != null) panelMat.mainTexture = renderTexture;
        panelMat.SetColor("_OutlineColor", outlineColor);
        panelMat.SetFloat("_OutlineWidth", outlineWidth);
        panelMat.SetFloat("_Radius", cornerRadius);
        displayImage.material = panelMat;
        UpdatePanelMaterial();
    }

    private void UpdatePanelMaterial() {
        if (panelMat == null || displayImage == null) return;
        Rect r = displayImage.rectTransform.rect;
        panelMat.SetVector("_Size", new Vector4(r.width, r.height, 0f, 0f));
        panelMat.SetFloat("_Radius", cornerRadius);
        panelMat.SetFloat("_OutlineWidth", outlineWidth);
        panelMat.SetColor("_OutlineColor", outlineColor);
    }

    // ---- 背景タイル（ビューアカメラの子クワッド。RTの中に人物と一緒に写る） ----
    private void BuildBackdrop() {
        if (viewerCamera == null || backdrop != null) return;
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "PreviewBackdrop";
        var col = q.GetComponent<Collider>(); if (col != null) Destroy(col);
        int layer = LayerMask.NameToLayer(previewLayerName);
        if (layer >= 0) q.layer = layer;
        q.transform.SetParent(viewerCamera.transform, false);

        float dist = 9f;   // プレイヤー(距離~3)より十分奥
        float h = 2f * dist * Mathf.Tan(viewerCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float w = h * (renderSize.y != 0 ? (float)renderSize.x / renderSize.y : 1f);
        q.transform.localPosition = new Vector3(0f, 0f, dist);
        q.transform.localRotation = Quaternion.identity;
        q.transform.localScale = new Vector3(w * 1.2f, h * 1.2f, 1f);

        tileTex = BuildTileTexture();
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlit == null) unlit = Shader.Find("Unlit/Texture");
        tileMat = new Material(unlit);
        if (tileMat.HasProperty("_BaseColor")) tileMat.SetColor("_BaseColor", Color.white);
        if (tileMat.HasProperty("_BaseMap"))   tileMat.SetTexture("_BaseMap", tileTex);
        tileMat.mainTexture = tileTex;
        tileMat.mainTextureScale = new Vector2(tileTiling, tileTiling);
        if (tileMat.HasProperty("_Cull")) tileMat.SetFloat("_Cull", 0f);

        var mr = q.GetComponent<MeshRenderer>();
        mr.sharedMaterial = tileMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        backdrop = q.transform;
    }

    private Texture2D BuildTileTexture() {
        const int S = 128;                       // 2×2 チェッカー（各64px、くっきり）
        Texture2D t = new Texture2D(S, S, TextureFormat.RGBA32, false) {
            name = "T_PreviewTiles (Auto)",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
        };
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++) {
                bool a = (x < S / 2) ^ (y < S / 2);
                t.SetPixel(x, y, a ? tileColorA : tileColorB);
            }
        t.Apply();
        return t;
    }

    // UVオフセットを進めてタイルを右下→左上へ流す
    private void ScrollTiles() {
        if (tileMat == null) return;
        Vector2 off = tileMat.mainTextureOffset
                    + tileScrollDir.normalized * (tileScrollSpeed * Time.unscaledDeltaTime);
        tileMat.mainTextureOffset = off;
    }

    // ---- 着替え時のポワッ演出（ビューポート内で再生） -----------------------
    // ビューア表示中に装備服が変わった瞬間を検知して1度だけ再生する。
    // ドラッグ→セル・ドロップ→ビューア・チュートリアル自動着替えなど全経路を一括カバーし、
    // 開いただけ／起動時プリキャッシュ（ビューア非表示）では出ない。
    private void DetectClothChangeForPoof() {
        if (modelSwitcher == null) return;
        GameObject cur = modelSwitcher.CurrentClothPrefab;
        if (cur == lastClothForPoof) return;
        lastClothForPoof = cur;
        if (cur != null) SpawnChangePoof();   // 脱衣(null)では出さない
    }

    private void SpawnChangePoof() {
        if (changePoofPrefab == null) return;
        Vector3 pos = fixedLookTarget + changePoofOffset;
        GameObject fx = Instantiate(changePoofPrefab, pos, Quaternion.identity);
        if (!Mathf.Approximately(changePoofScale, 1f)) fx.transform.localScale *= changePoofScale;

        // ビューアカメラ(PlayerPreview レイヤーのみ描画)に映るようレイヤーを合わせる
        int layer = LayerMask.NameToLayer(previewLayerName);
        if (layer >= 0) SetLayerRecursive(fx.transform, layer);

        // 世界停止(timeScale=0)中の演出でも動くよう unscaled 再生に
        foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) {
            var main = ps.main; main.useUnscaledTime = true;
        }
        StartCoroutine(DestroyAfter(fx, changePoofLifetime));
    }

    private static void SetLayerRecursive(Transform t, int layer) {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
    }

    private System.Collections.IEnumerator DestroyAfter(GameObject go, float sec) {
        yield return new WaitForSecondsRealtime(sec);
        if (go != null) Destroy(go);
    }

    void OnDestroy() {
        if (renderTexture != null) renderTexture.Release();
        if (panelMat != null) Destroy(panelMat);
        if (tileMat != null)  Destroy(tileMat);
        if (tileTex != null)  Destroy(tileTex);
    }
}
