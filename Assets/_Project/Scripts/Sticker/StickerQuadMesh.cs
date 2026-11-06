/**===========================================================================

 File   : シール用分割板ポリ生成 [StickerQuadMesh.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/5
 -----------------------------------------------------------------------------
 World/StickerPeel の 3D カール (頂点変形) には分割された板ポリが必要。
 Unity 組込みの Quad は 4 頂点しかなく曲がらないため、同じ座標系
 (XY 平面 [-0.5, 0.5]、表=−Z、UV 0..1) の N×N グリッドを生成して共有する。

 使い方: meshFilter.sharedMesh = StickerQuadMesh.Grid;
============================================================================*/

using UnityEngine;

public static class StickerQuadMesh
{
    const int Segments = 40;   // 1辺の分割数 (曲面の滑らかさ)

    static Mesh _grid;

    public static Mesh Grid
    {
        get
        {
            if (_grid == null) _grid = Build(Segments);
            return _grid;
        }
    }

    static Mesh Build(int seg)
    {
        int vCount = (seg + 1) * (seg + 1);
        var verts = new Vector3[vCount];
        var uvs   = new Vector2[vCount];
        var norms = new Vector3[vCount];

        for (int y = 0; y <= seg; y++)
        {
            for (int x = 0; x <= seg; x++)
            {
                int i = y * (seg + 1) + x;
                float u = (float)x / seg;
                float v = (float)y / seg;
                verts[i] = new Vector3(u - 0.5f, v - 0.5f, 0f);
                uvs[i]   = new Vector2(u, v);
                norms[i] = new Vector3(0f, 0f, -1f);   // 組込み Quad と同じく −Z が表
            }
        }

        var tris = new int[seg * seg * 6];
        int t = 0;
        for (int y = 0; y < seg; y++)
        {
            for (int x = 0; x < seg; x++)
            {
                int i0 = y * (seg + 1) + x;
                int i1 = i0 + 1;
                int i2 = i0 + (seg + 1);
                int i3 = i2 + 1;
                // 組込み Quad と同じ向き (−Z 側から見て表) になる巻き順
                tris[t++] = i0; tris[t++] = i3; tris[t++] = i1;
                tris[t++] = i0; tris[t++] = i2; tris[t++] = i3;
            }
        }

        var mesh = new Mesh
        {
            name      = $"StickerQuadGrid_{seg}x{seg}",
            vertices  = verts,
            uv        = uvs,
            normals   = norms,
            triangles = tris,
        };
        // カールで −Z 方向 (上) へ最大 2×カール半径ほど持ち上がるためバウンズを拡張
        mesh.bounds = new Bounds(new Vector3(0f, 0f, -0.2f), new Vector3(1.4f, 1.4f, 0.7f));
        return mesh;
    }
}
