using UnityEngine;

/// <summary>
/// テレポート目的地に表示する地面リング＋ビルボードマーカー。
/// Spawn() でインスタンス化し、Destroy() で消す。
/// </summary>
public class B1_TeleportMarker : MonoBehaviour
{
    private LineRenderer ringRenderer;
    private Material     ringMat;
    private Transform    billboardTransform;
    private Color        baseColor;
    private float        elapsed;

    public static B1_TeleportMarker Spawn(Vector3 worldPos, float radius, Color color)
    {
        var go = new GameObject("TeleportMarker");
        go.transform.position = worldPos;

        var marker       = go.AddComponent<B1_TeleportMarker>();
        marker.baseColor = color;

        marker.BuildRing(radius, color);
        marker.BuildBillboard(color);

        return marker;
    }

    private void BuildRing(float radius, Color color)
    {
        var ringGo = new GameObject("Ring");
        ringGo.transform.SetParent(transform, worldPositionStays: false);

        ringRenderer                  = ringGo.AddComponent<LineRenderer>();
        ringRenderer.useWorldSpace    = false;
        ringRenderer.loop             = true;
        ringRenderer.startWidth       = 0.12f;
        ringRenderer.endWidth         = 0.12f;
        ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        const int seg = 36;
        ringRenderer.positionCount = seg;
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            ringRenderer.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0.05f, Mathf.Sin(a) * radius));
        }

        ringMat              = new Material(Shader.Find("Sprites/Default"));
        ringMat.color        = color;
        ringRenderer.material = ringMat;
    }

    private void BuildBillboard(Color color)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Billboard";
        Destroy(quad.GetComponent<MeshCollider>());
        quad.transform.SetParent(transform, worldPositionStays: false);
        quad.transform.localPosition = new Vector3(0f, 2f, 0f);
        quad.transform.localScale    = new Vector3(0.7f, 0.7f, 1f);
        quad.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var mat   = new Material(Shader.Find("Sprites/Default"));
        mat.color = color;
        quad.GetComponent<MeshRenderer>().material = mat;

        billboardTransform = quad.transform;
    }

    private void LateUpdate()
    {
        elapsed += Time.deltaTime;

        // リングをパルス
        float pulse = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(elapsed * Mathf.PI * 3f));
        var c = new Color(baseColor.r, baseColor.g, baseColor.b, pulse);
        ringRenderer.startColor = c;
        ringRenderer.endColor   = c;

        // ビルボードをカメラに向ける
        if (billboardTransform != null && Camera.main != null)
        {
            billboardTransform.LookAt(Camera.main.transform);
            billboardTransform.Rotate(0f, 180f, 0f);
        }
    }

    private void OnDestroy()
    {
        if (ringMat != null) Destroy(ringMat);
    }
}
