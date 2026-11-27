using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public enum GraphicsQualityTier
{
    UltraGTA6 = 0,
    High = 1,
    Mobile60FPS = 2
}

// ===========================================================================
// SECTION 1: NEXT-GEN PROCEDURAL MESH, 4-CHANNEL PBR & NOISE ENGINE
// ===========================================================================
#region 1. Safe Shaders, Cached PBR Materials, Low-Poly Meshes & 256x256 Terrain PBR
public static class LowPolyMeshHelper
{
    private static Dictionary<string, Mesh> s_MeshCache = new Dictionary<string, Mesh>();

    public static Mesh MakeFlatShaded(Mesh source)
    {
        if (source == null) return null;
        Vector3[] oldVerts = source.vertices;
        int[] oldTris = source.triangles;
        Vector2[] oldUvs = source.uv;
        bool hasUvs = (oldUvs != null && oldUvs.Length == oldVerts.Length);

        Vector3[] newVerts = new Vector3[oldTris.Length];
        Vector2[] newUvs = hasUvs ? new Vector2[oldTris.Length] : null;
        int[] newTris = new int[oldTris.Length];

        for (int i = 0; i < oldTris.Length; i++)
        {
            int origIdx = oldTris[i];
            newVerts[i] = oldVerts[origIdx];
            if (hasUvs) newUvs[i] = oldUvs[origIdx];
            newTris[i] = i;
        }

        Mesh flat = new Mesh();
        flat.name = source.name + "_Flat";
        flat.vertices = newVerts;
        if (hasUvs) flat.uv = newUvs;
        flat.triangles = newTris;
        flat.RecalculateNormals();
        flat.RecalculateTangents();
        flat.RecalculateBounds();
        return flat;
    }

    // GTA 6 Sculpted Beveled Land Slab with exact world-space proportional UVs & tangents
    public static Mesh GetBeveledSlabMesh(float width, float height, float depth, float bevel = 0.16f)
    {
        float qw = Mathf.Max(0.5f, Mathf.Round(width * 2f) * 0.5f);
        float qh = Mathf.Max(0.15f, Mathf.Round(height * 4f) * 0.25f);
        float qd = Mathf.Max(0.5f, Mathf.Round(depth * 2f) * 0.5f);
        string key = "BeveledSlab_" + qw.ToString("F1") + "_" + qh.ToString("F2") + "_" + qd.ToString("F1");
        Mesh m;
        if (s_MeshCache.TryGetValue(key, out m) && m != null) return m;

        float hw = qw * 0.5f;
        float hh = qh * 0.5f;
        float hd = qd * 0.5f;
        float b = Mathf.Min(bevel, Mathf.Min(hw * 0.35f, hd * 0.35f));
        float by = Mathf.Min(bevel * 0.85f, qh * 0.55f);

        float iW = hw - b;
        float iD = hd - b;
        float yTop = hh;
        float yMid = hh - by;
        float yBot = -hh;

        Vector3 t0 = new Vector3(-iW, yTop, -iD);
        Vector3 t1 = new Vector3( iW, yTop, -iD);
        Vector3 t2 = new Vector3( iW, yTop,  iD);
        Vector3 t3 = new Vector3(-iW, yTop,  iD);

        Vector3 m0 = new Vector3(-hw, yMid, -hd);
        Vector3 m1 = new Vector3( hw, yMid, -hd);
        Vector3 m2 = new Vector3( hw, yMid,  hd);
        Vector3 m3 = new Vector3(-hw, yMid,  hd);

        Vector3 b0 = new Vector3(-hw, yBot, -hd);
        Vector3 b1 = new Vector3( hw, yBot, -hd);
        Vector3 b2 = new Vector3( hw, yBot,  hd);
        Vector3 b3 = new Vector3(-hw, yBot,  hd);

        List<Vector3> verts = new List<Vector3>(64);
        List<Vector2> uvs = new List<Vector2>(64);
        List<int> tris = new List<int>(96);

        float uvScale = 1f / 4.5f;
        Vector2 PlanarUV(Vector3 p) => new Vector2((p.x + hw) * uvScale, (p.z + hd) * uvScale);

        void AddQuadPlanar(Vector3 a, Vector3 bPt, Vector3 c, Vector3 d)
        {
            int idx = verts.Count;
            verts.Add(a); verts.Add(bPt); verts.Add(c); verts.Add(d);
            uvs.Add(PlanarUV(a)); uvs.Add(PlanarUV(bPt)); uvs.Add(PlanarUV(c)); uvs.Add(PlanarUV(d));
            tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
            tris.Add(idx); tris.Add(idx + 2); tris.Add(idx + 3);
        }

        void AddSideQuad(Vector3 tl, Vector3 tr, Vector3 br, Vector3 bl, float len)
        {
            int idx = verts.Count;
            verts.Add(tl); verts.Add(tr); verts.Add(br); verts.Add(bl);
            float uSpan = len * uvScale;
            float vSpan = Mathf.Max(0.15f, (tl.y - bl.y) * uvScale);
            uvs.Add(new Vector2(0f, vSpan));
            uvs.Add(new Vector2(uSpan, vSpan));
            uvs.Add(new Vector2(uSpan, 0f));
            uvs.Add(new Vector2(0f, 0f));
            tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
            tris.Add(idx); tris.Add(idx + 2); tris.Add(idx + 3);
        }

        AddQuadPlanar(t0, t3, t2, t1);
        AddQuadPlanar(m0, m3, t3, t0);
        AddQuadPlanar(t1, t2, m2, m1);
        AddQuadPlanar(m0, t0, t1, m1);
        AddQuadPlanar(t3, m3, m2, t2);

        AddSideQuad(m3, m0, b0, b3, qd);
        AddSideQuad(m1, m2, b2, b1, qd);
        AddSideQuad(m0, m1, b1, b0, qw);
        AddSideQuad(m2, m3, b3, b2, qw);

        AddQuadPlanar(b0, b1, b2, b3);

        Mesh mesh = new Mesh();
        mesh.name = key;
        mesh.vertices = verts.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        s_MeshCache[key] = mesh;
        return mesh;
    }

    public static Mesh GetVolumetricConeMesh(int segments = 16, float topRadius = 0.25f, float bottomRadius = 1.0f, float height = 1.0f)
    {
        string key = "VolCone_" + segments + "_" + topRadius.ToString("F2") + "_" + bottomRadius.ToString("F2") + "_" + height.ToString("F2");
        Mesh m;
        if (s_MeshCache.TryGetValue(key, out m) && m != null) return m;

        Mesh mesh = new Mesh();
        mesh.name = key;
        List<Vector3> verts = new List<Vector3>();
        List<Color> cols = new List<Color>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> tris = new List<int>();

        for (int i = 0; i <= segments; i++)
        {
            float a = (i * 2f * Mathf.PI) / segments;
            float cx = Mathf.Cos(a);
            float cz = Mathf.Sin(a);

            verts.Add(new Vector3(cx * topRadius, 0f, cz * topRadius));
            cols.Add(new Color(1f, 1f, 1f, 0.24f));
            uvs.Add(new Vector2((float)i / segments, 1f));

            verts.Add(new Vector3(cx * bottomRadius, -height, cz * bottomRadius));
            cols.Add(new Color(1f, 1f, 1f, 0.0f));
            uvs.Add(new Vector2((float)i / segments, 0f));
        }

        for (int i = 0; i < segments; i++)
        {
            int t0 = i * 2;
            int b0 = t0 + 1;
            int t1 = t0 + 2;
            int b1 = t0 + 3;

            tris.Add(t0); tris.Add(t1); tris.Add(b1);
            tris.Add(t0); tris.Add(b1); tris.Add(b0);
            tris.Add(t0); tris.Add(b1); tris.Add(t1);
            tris.Add(t0); tris.Add(b0); tris.Add(b1);
        }

        mesh.vertices = verts.ToArray();
        mesh.colors = cols.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        s_MeshCache[key] = mesh;
        return mesh;
    }

    public static Mesh GetSkyDomeMesh(float radius = 420f, int lats = 16, int lons = 24)
    {
        string key = "SkyDome_" + ((int)radius) + "_" + lats + "_" + lons;
        Mesh m;
        if (s_MeshCache.TryGetValue(key, out m) && m != null) return m;

        Mesh mesh = new Mesh();
        mesh.name = key;
        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> tris = new List<int>();

        for (int lat = 0; lat <= lats; lat++)
        {
            float v = (float)lat / lats;
            float phi = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, v);
            float y = Mathf.Sin(phi) * radius;
            float r = Mathf.Cos(phi) * radius;

            for (int lon = 0; lon <= lons; lon++)
            {
                float u = (float)lon / lons;
                float theta = u * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(theta) * r, y, Mathf.Sin(theta) * r));
                uvs.Add(new Vector2(u, v));
            }
        }

        for (int lat = 0; lat < lats; lat++)
        {
            for (int lon = 0; lon < lons; lon++)
            {
                int i0 = lat * (lons + 1) + lon;
                int i1 = i0 + 1;
                int i2 = i0 + (lons + 1);
                int i3 = i2 + 1;

                tris.Add(i0); tris.Add(i1); tris.Add(i3);
                tris.Add(i0); tris.Add(i3); tris.Add(i2);
            }
        }

        mesh.vertices = verts.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        s_MeshCache[key] = mesh;
        return mesh;
    }

    public static Mesh GetArchFrondMesh()
    {
        string key = "ArchPalmFrond";
        Mesh m;
        if (s_MeshCache.TryGetValue(key, out m) && m != null) return m;

        Vector3[] verts = new Vector3[] {
            new Vector3( 0.00f,  0.00f, 0.00f),
            new Vector3(-0.30f,  0.22f, 0.65f),
            new Vector3( 0.30f,  0.22f, 0.65f),
            new Vector3(-0.36f,  0.26f, 1.40f),
            new Vector3( 0.36f,  0.26f, 1.40f),
            new Vector3(-0.20f, -0.02f, 2.15f),
            new Vector3( 0.20f, -0.02f, 2.15f),
            new Vector3( 0.00f, -0.48f, 2.75f)
        };
        int[] tris = new int[] {
            0, 1, 2,
            1, 3, 2,  2, 3, 4,
            3, 5, 4,  4, 5, 6,
            5, 7, 6,
            0, 2, 1,
            1, 2, 3,  2, 4, 3,
            3, 4, 5,  4, 6, 5,
            5, 6, 7
        };
        Vector2[] uvs = new Vector2[verts.Length];
        for (int i = 0; i < verts.Length; i++) uvs[i] = new Vector2(verts[i].x + 0.5f, verts[i].z / 2.65f);

        Mesh mesh = new Mesh();
        mesh.name = key;
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        s_MeshCache[key] = mesh;
        return mesh;
    }

    public static Mesh GetSharpCone(int segments = 10, float radius = 0.5f, float height = 1.0f)
    {
        string key = "Cone_" + segments + "_" + radius.ToString("F2") + "_" + height.ToString("F2");
        Mesh m;
        if (s_MeshCache.TryGetValue(key, out m) && m != null) return m;

        Mesh mesh = new Mesh();
        mesh.name = key;
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        Vector3 tip = new Vector3(0f, height, 0f);
        Vector3 baseCenter = Vector3.zero;

        for (int i = 0; i < segments; i++)
        {
            float a1 = (i * 2f * Mathf.PI) / segments;
            float a2 = ((i + 1) * 2f * Mathf.PI) / segments;
            Vector3 p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
            Vector3 p2 = new Vector3(Mathf.Cos(a2) * radius, 0f, Mathf.Sin(a2) * radius);

            int idx = verts.Count;
            verts.Add(tip); verts.Add(p1); verts.Add(p2);
            uvs.Add(new Vector2(0.5f, 1f));
            uvs.Add(new Vector2((float)i / segments, 0f));
            uvs.Add(new Vector2((float)(i + 1) / segments, 0f));
            tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);

            idx = verts.Count;
            verts.Add(baseCenter); verts.Add(p2); verts.Add(p1);
            uvs.Add(new Vector2(0.5f, 0.5f));
            uvs.Add(new Vector2(0.5f + Mathf.Cos(a2) * 0.5f, 0.5f + Mathf.Sin(a2) * 0.5f));
            uvs.Add(new Vector2(0.5f + Mathf.Cos(a1) * 0.5f, 0.5f + Mathf.Sin(a1) * 0.5f));
            tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
        }

        mesh.vertices = verts.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        s_MeshCache[key] = mesh;
        return mesh;
    }

    public static Mesh GetBeveledPyramid(float width = 1.0f, float height = 1.0f)
    {
        string key = "Pyramid_" + width.ToString("F2") + "_" + height.ToString("F2");
        Mesh m;
        if (s_MeshCache.TryGetValue(key, out m) && m != null) return m;

        Mesh mesh = new Mesh();
        mesh.name = key;
        float hw = width * 0.5f;
        Vector3 tip = new Vector3(0f, height, 0f);
        Vector3 c1 = new Vector3(-hw, 0f, -hw);
        Vector3 c2 = new Vector3(hw, 0f, -hw);
        Vector3 c3 = new Vector3(hw, 0f, hw);
        Vector3 c4 = new Vector3(-hw, 0f, hw);

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        void AddTri(Vector3 a, Vector3 b, Vector3 c)
        {
            int idx = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
        }

        AddTri(tip, c1, c2); AddTri(tip, c2, c3); AddTri(tip, c3, c4); AddTri(tip, c4, c1);
        AddTri(c1, c3, c2); AddTri(c1, c4, c3);

        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        s_MeshCache[key] = mesh;
        return mesh;
    }

    public static Mesh GetTorusDonut(int radialSegments = 18, int tubeSegments = 10, float mainRadius = 1.0f, float tubeRadius = 0.35f)
    {
        string key = "Torus_" + radialSegments + "_" + tubeSegments + "_" + mainRadius.ToString("F2") + "_" + tubeRadius.ToString("F2");
        Mesh m;
        if (s_MeshCache.TryGetValue(key, out m) && m != null) return m;

        Mesh mesh = new Mesh();
        mesh.name = key;
        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> tris = new List<int>();

        for (int i = 0; i <= radialSegments; i++)
        {
            float u = (i * 2f * Mathf.PI) / radialSegments;
            Vector3 center = new Vector3(Mathf.Cos(u) * mainRadius, 0f, Mathf.Sin(u) * mainRadius);

            for (int j = 0; j <= tubeSegments; j++)
            {
                float v = (j * 2f * Mathf.PI) / tubeSegments;
                Vector3 normal = new Vector3(Mathf.Cos(u) * Mathf.Cos(v), Mathf.Sin(v), Mathf.Sin(u) * Mathf.Cos(v));
                Vector3 pos = center + normal * tubeRadius;
                verts.Add(pos);
                uvs.Add(new Vector2((float)i / radialSegments, (float)j / tubeSegments));
            }
        }

        for (int i = 0; i < radialSegments; i++)
        {
            for (int j = 0; j < tubeSegments; j++)
            {
                int cur = i * (tubeSegments + 1) + j;
                int next = (i + 1) * (tubeSegments + 1) + j;
                tris.Add(cur); tris.Add(next); tris.Add(next + 1);
                tris.Add(cur); tris.Add(next + 1); tris.Add(cur + 1);
            }
        }

        mesh.vertices = verts.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        s_MeshCache[key] = mesh;
        return mesh;
    }

    public static Mesh GetPizzaWedge(float length = 1.5f, float width = 1.2f, float thickness = 0.25f, int arcSegments = 8)
    {
        string key = "Pizza_" + length.ToString("F2") + "_" + width.ToString("F2") + "_" + thickness.ToString("F2");
        Mesh m;
        if (s_MeshCache.TryGetValue(key, out m) && m != null) return m;

        Mesh mesh = new Mesh();
        mesh.name = key;
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        float halfThick = thickness * 0.5f;
        Vector3 tipTop = new Vector3(0f, halfThick, 0f);
        Vector3 tipBot = new Vector3(0f, -halfThick, 0f);

        Vector3[] arcTop = new Vector3[arcSegments + 1];
        Vector3[] arcBot = new Vector3[arcSegments + 1];

        float halfAngle = Mathf.Atan2(width * 0.5f, length);
        for (int i = 0; i <= arcSegments; i++)
        {
            float t = (float)i / arcSegments;
            float angle = Mathf.Lerp(-halfAngle, halfAngle, t);
            float x = Mathf.Sin(angle) * length;
            float z = Mathf.Cos(angle) * length;
            arcTop[i] = new Vector3(x, halfThick, z);
            arcBot[i] = new Vector3(x, -halfThick, z);
        }

        void AddTri(Vector3 a, Vector3 b, Vector3 c)
        {
            int idx = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
        }

        for (int i = 0; i < arcSegments; i++)
        {
            AddTri(tipTop, arcTop[i], arcTop[i + 1]);
            AddTri(tipBot, arcBot[i + 1], arcBot[i]);
            AddTri(arcTop[i], arcBot[i], arcBot[i + 1]);
            AddTri(arcTop[i], arcBot[i + 1], arcTop[i + 1]);
        }
        AddTri(tipTop, tipBot, arcBot[0]); AddTri(tipTop, arcBot[0], arcTop[0]);
        AddTri(tipTop, arcTop[arcSegments], arcBot[arcSegments]); AddTri(tipTop, arcBot[arcSegments], tipBot);

        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        s_MeshCache[key] = mesh;
        return mesh;
    }

    public static Mesh GetFivePointStar(float outerRadius = 0.65f, float innerRadius = 0.28f, float depth = 0.20f)
    {
        string key = "Star5_" + outerRadius.ToString("F2") + "_" + innerRadius.ToString("F2") + "_" + depth.ToString("F2");
        Mesh m;
        if (s_MeshCache.TryGetValue(key, out m) && m != null) return m;

        Mesh mesh = new Mesh();
        mesh.name = key;
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        float halfD = depth * 0.5f;
        Vector3 frontCenter = new Vector3(0f, 0f, -halfD);
        Vector3 backCenter = new Vector3(0f, 0f, halfD);

        Vector3[] frontPts = new Vector3[10];
        Vector3[] backPts = new Vector3[10];

        for (int i = 0; i < 10; i++)
        {
            float angle = (i * 36f - 90f) * Mathf.Deg2Rad;
            float r = (i % 2 == 0) ? outerRadius : innerRadius;
            float x = Mathf.Cos(angle) * r;
            float y = -Mathf.Sin(angle) * r;
            frontPts[i] = new Vector3(x, y, -halfD);
            backPts[i] = new Vector3(x, y, halfD);
        }

        void AddTri(Vector3 a, Vector3 b, Vector3 c)
        {
            int idx = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
        }

        for (int i = 0; i < 10; i++)
        {
            int next = (i + 1) % 10;
            AddTri(frontCenter, frontPts[next], frontPts[i]);
            AddTri(backCenter, backPts[i], backPts[next]);
            AddTri(frontPts[i], frontPts[next], backPts[next]);
            AddTri(frontPts[i], backPts[next], backPts[i]);
        }

        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        s_MeshCache[key] = mesh;
        return mesh;
    }

    public static Mesh GetOctagonPrism(float radius = 1.0f, float height = 0.5f, int sides = 8)
    {
        string key = "OctPrism_" + sides + "_" + radius.ToString("F2") + "_" + height.ToString("F2");
        Mesh m;
        if (s_MeshCache.TryGetValue(key, out m) && m != null) return m;

        Mesh mesh = new Mesh();
        mesh.name = key;
        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> tris = new List<int>();

        float halfH = height * 0.5f;
        Vector3 topCenter = new Vector3(0f, halfH, 0f);
        Vector3 botCenter = new Vector3(0f, -halfH, 0f);

        Vector3[] topRing = new Vector3[sides];
        Vector3[] botRing = new Vector3[sides];

        for (int i = 0; i < sides; i++)
        {
            float a = (i * 2f * Mathf.PI) / sides;
            float x = Mathf.Cos(a) * radius;
            float z = Mathf.Sin(a) * radius;
            topRing[i] = new Vector3(x, halfH, z);
            botRing[i] = new Vector3(x, -halfH, z);
        }

        void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector2 uva, Vector2 uvb, Vector2 uvc)
        {
            int idx = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            uvs.Add(uva); uvs.Add(uvb); uvs.Add(uvc);
            tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
        }

        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            Vector2 uvC = new Vector2(0.5f, 0.5f);
            Vector2 uv1 = new Vector2(0.5f + (topRing[i].x / (radius * 2f)), 0.5f + (topRing[i].z / (radius * 2f)));
            Vector2 uv2 = new Vector2(0.5f + (topRing[next].x / (radius * 2f)), 0.5f + (topRing[next].z / (radius * 2f)));
            AddTri(topCenter, topRing[i], topRing[next], uvC, uv1, uv2);
            AddTri(botCenter, botRing[next], botRing[i], uvC, uv2, uv1);

            Vector2 s1 = new Vector2((float)i / sides, 1f);
            Vector2 s2 = new Vector2((float)(i + 1) / sides, 1f);
            Vector2 s3 = new Vector2((float)(i + 1) / sides, 0f);
            Vector2 s4 = new Vector2((float)i / sides, 0f);
            AddTri(topRing[i], botRing[i], botRing[next], s1, s4, s3);
            AddTri(topRing[i], botRing[next], topRing[next], s1, s3, s2);
        }

        mesh.vertices = verts.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        s_MeshCache[key] = mesh;
        return mesh;
    }

    public static Mesh GetHexPillar(float radius = 0.6f, float height = 2.0f)
    {
        return GetOctagonPrism(radius, height, 6);
    }

    public static Mesh GetTaperedFrustum(float topRadius = 0.5f, float bottomRadius = 0.35f, float height = 1.0f, int segments = 12)
    {
        string key = "Frustum_" + segments + "_" + topRadius.ToString("F2") + "_" + bottomRadius.ToString("F2") + "_" + height.ToString("F2");
        Mesh m;
        if (s_MeshCache.TryGetValue(key, out m) && m != null) return m;

        Mesh mesh = new Mesh();
        mesh.name = key;
        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> tris = new List<int>();

        float halfH = height * 0.5f;
        Vector3 topCenter = new Vector3(0f, halfH, 0f);
        Vector3 botCenter = new Vector3(0f, -halfH, 0f);

        Vector3[] topRing = new Vector3[segments];
        Vector3[] botRing = new Vector3[segments];

        for (int i = 0; i < segments; i++)
        {
            float a = (i * 2f * Mathf.PI) / segments;
            topRing[i] = new Vector3(Mathf.Cos(a) * topRadius, halfH, Mathf.Sin(a) * topRadius);
            botRing[i] = new Vector3(Mathf.Cos(a) * bottomRadius, -halfH, Mathf.Sin(a) * bottomRadius);
        }

        void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector2 uva, Vector2 uvb, Vector2 uvc)
        {
            int idx = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            uvs.Add(uva); uvs.Add(uvb); uvs.Add(uvc);
            tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
        }

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            AddTri(topCenter, topRing[i], topRing[next], new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), new Vector2(1f, 1f));
            AddTri(botCenter, botRing[next], botRing[i], new Vector2(0.5f, 0.5f), new Vector2(1f, 0f), new Vector2(0f, 0f));

            Vector2 u1 = new Vector2((float)i / segments, 1f);
            Vector2 u2 = new Vector2((float)(i + 1) / segments, 1f);
            Vector2 u3 = new Vector2((float)(i + 1) / segments, 0f);
            Vector2 u4 = new Vector2((float)i / segments, 0f);
            AddTri(topRing[i], botRing[i], botRing[next], u1, u4, u3);
            AddTri(topRing[i], botRing[next], topRing[next], u1, u3, u2);
        }

        mesh.vertices = verts.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        s_MeshCache[key] = mesh;
        return mesh;
    }
}

// ===========================================================================
// SECTION 1.1: CROSS-PIPELINE SAFE PBR SHADER & MATERIAL FACTORY
// ===========================================================================
public class PBRMaterialSuite
{
    public Texture2D AlbedoTex;
    public Texture2D NormalTex;
    public Texture2D MetallicGlossTex;
    public Texture2D EmissiveTex;
    public Color TintColor = Color.white;
    public float BumpScale = 1.0f;
    public Color EmissionTint = Color.black;
    public float BaseMetallic = 0.0f;
}

public static class SafeMaterialFactory
{
    private static Shader s_CachedLitShader = null;
    private static Shader s_CachedUnlitShader = null;
    private static Shader s_CachedVertexColorShader = null;
    private static Shader s_CachedAdditiveShader = null;
    private static Dictionary<string, Material> s_MaterialCache = new Dictionary<string, Material>();

    public static Shader ResolveLitShader()
    {
        if (s_CachedLitShader != null) return s_CachedLitShader;
        string[] candidates = new string[] {
            "Universal Render Pipeline/Lit",
            "Standard",
            "Universal Render Pipeline/Simple Lit",
            "Mobile/Diffuse"
        };
        for (int i = 0; i < candidates.Length; i++)
        {
            Shader s = Shader.Find(candidates[i]);
            if (s != null) { s_CachedLitShader = s; return s; }
        }
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Renderer r = temp.GetComponent<Renderer>();
        if (r != null && r.sharedMaterial != null) s_CachedLitShader = r.sharedMaterial.shader;
        UnityEngine.Object.DestroyImmediate(temp);
        return s_CachedLitShader;
    }

    public static Shader ResolveUnlitShader()
    {
        if (s_CachedUnlitShader != null) return s_CachedUnlitShader;
        string[] candidates = new string[] {
            "Universal Render Pipeline/Unlit",
            "Unlit/Transparent",
            "Sprites/Default"
        };
        for (int i = 0; i < candidates.Length; i++)
        {
            Shader s = Shader.Find(candidates[i]);
            if (s != null) { s_CachedUnlitShader = s; return s; }
        }
        return ResolveLitShader();
    }

    public static Shader ResolveVertexColorAlphaShader()
    {
        if (s_CachedVertexColorShader != null) return s_CachedVertexColorShader;
        string[] candidates = new string[] {
            "Sprites/Default",
            "UI/Default",
            "Universal Render Pipeline/Particles/Unlit",
            "Particles/Standard Unlit"
        };
        for (int i = 0; i < candidates.Length; i++)
        {
            Shader s = Shader.Find(candidates[i]);
            if (s != null) { s_CachedVertexColorShader = s; return s; }
        }
        return ResolveUnlitShader();
    }

    public static Shader ResolveAdditiveShader()
    {
        if (s_CachedAdditiveShader != null) return s_CachedAdditiveShader;
        string[] candidates = new string[] {
            "Legacy Shaders/Particles/Additive",
            "Mobile/Particles/Additive",
            "Particles/Additive",
            "Universal Render Pipeline/Particles/Unlit",
            "Sprites/Default"
        };
        for (int i = 0; i < candidates.Length; i++)
        {
            Shader s = Shader.Find(candidates[i]);
            if (s != null) { s_CachedAdditiveShader = s; return s; }
        }
        return ResolveVertexColorAlphaShader();
    }

    public static void ConfigureMaterial(Material mat, Color col, Texture2D tex = null, float smoothness = 0.25f, float metallic = 0.0f, Color? emission = null, bool isTransparent = false)
    {
        if (mat == null) return;
        mat.color = col;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);

        if (tex != null)
        {
            mat.mainTexture = tex;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        }

        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);

        if (emission.HasValue && emission.Value != Color.black)
        {
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission.Value);
        }

        if (isTransparent)
        {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = 3000;
        }
    }

    public static void ConfigurePBRMaterial(Material mat, PBRMaterialSuite suite, float tileX, float tileY)
    {
        if (mat == null || suite == null) return;
        Color col = suite.TintColor;
        mat.color = col;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);

        Vector2 tiling = new Vector2(tileX, tileY);

        if (suite.AlbedoTex != null)
        {
            mat.mainTexture = suite.AlbedoTex;
            mat.mainTextureScale = tiling;
            if (mat.HasProperty("_BaseMap")) { mat.SetTexture("_BaseMap", suite.AlbedoTex); mat.SetTextureScale("_BaseMap", tiling); }
            if (mat.HasProperty("_MainTex")) { mat.SetTexture("_MainTex", suite.AlbedoTex); mat.SetTextureScale("_MainTex", tiling); }
        }

        if (suite.NormalTex != null)
        {
            mat.EnableKeyword("_NORMALMAP");
            if (mat.HasProperty("_BumpMap")) { mat.SetTexture("_BumpMap", suite.NormalTex); mat.SetTextureScale("_BumpMap", tiling); }
            if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", suite.BumpScale);
        }

        if (suite.MetallicGlossTex != null)
        {
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            if (mat.HasProperty("_MetallicGlossMap")) { mat.SetTexture("_MetallicGlossMap", suite.MetallicGlossTex); mat.SetTextureScale("_MetallicGlossMap", tiling); }
            if (mat.HasProperty("_SmoothnessTextureChannel")) mat.SetFloat("_SmoothnessTextureChannel", 0f);
            if (mat.HasProperty("_GlossMapScale")) mat.SetFloat("_GlossMapScale", 1.0f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 1.0f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 1.0f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 1.0f);
        }
        else
        {
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.35f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.35f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", suite.BaseMetallic);
        }

        if (suite.EmissiveTex != null)
        {
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionMap")) { mat.SetTexture("_EmissionMap", suite.EmissiveTex); mat.SetTextureScale("_EmissionMap", tiling); }
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", suite.EmissionTint);
        }
        else if (suite.EmissionTint != Color.black)
        {
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", suite.EmissionTint);
        }
    }

    public static Material GetCachedPBRMaterial(string baseKey, PBRMaterialSuite suite, float tileX, float tileY)
    {
        float bX = Mathf.Max(0.5f, Mathf.Round(tileX * 2f) * 0.5f);
        float bY = Mathf.Max(0.5f, Mathf.Round(tileY * 2f) * 0.5f);
        string key = "PBR_" + baseKey + "_" + bX.ToString("F1") + "x" + bY.ToString("F1");

        Material m;
        if (s_MaterialCache.TryGetValue(key, out m) && m != null) return m;

        Shader s = ResolveLitShader();
        m = (s != null) ? new Material(s) : new Material(Shader.Find("Standard"));
        m.name = key;
        ConfigurePBRMaterial(m, suite, bX, bY);
        s_MaterialCache[key] = m;
        return m;
    }

    public static Material GetCachedMaterial(string key, Color col, Texture2D tex = null, float smoothness = 0.25f, float metallic = 0.0f, Color? emission = null, bool isTransparent = false)
    {
        Material m;
        if (s_MaterialCache.TryGetValue(key, out m) && m != null) return m;

        Shader s = isTransparent ? ResolveUnlitShader() : ResolveLitShader();
        m = (s != null) ? new Material(s) : new Material(Shader.Find("Standard"));
        m.name = key;
        ConfigureMaterial(m, col, tex, smoothness, metallic, emission, isTransparent);
        s_MaterialCache[key] = m;
        return m;
    }

    public static Material GetCachedTiledMaterial(string baseKey, Texture2D tex, Color tint, float tileX, float tileY, float smoothness = 0.35f, float metallic = 0f, Color? emission = null)
    {
        float bX = Mathf.Max(0.5f, Mathf.Round(tileX * 2f) * 0.5f);
        float bY = Mathf.Max(0.5f, Mathf.Round(tileY * 2f) * 0.5f);
        string key = baseKey + "_" + bX.ToString("F1") + "x" + bY.ToString("F1");

        Material m;
        if (s_MaterialCache.TryGetValue(key, out m) && m != null) return m;

        m = GetCachedMaterial(key, tint, tex, smoothness, metallic, emission, false);
        Vector2 tiling = new Vector2(bX, bY);
        m.mainTextureScale = tiling;
        if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", tiling);
        if (m.HasProperty("_MainTex")) m.SetTextureScale("_MainTex", tiling);
        s_MaterialCache[key] = m;
        return m;
    }

    public static Material GetCachedAdditiveGlowMaterial(string key, Color tint, Texture2D tex = null)
    {
        Material m;
        if (s_MaterialCache.TryGetValue(key, out m) && m != null) return m;

        Shader s = ResolveAdditiveShader();
        m = new Material(s);
        m.name = key;
        ConfigureMaterial(m, tint, tex, 0f, 0f, null, false);
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", tint);
        m.renderQueue = 3100;
        s_MaterialCache[key] = m;
        return m;
    }

    public static Material GetCachedVertexAlphaMaterial(string key, Color tint, Texture2D tex = null, int renderQueue = 3050)
    {
        Material m;
        if (s_MaterialCache.TryGetValue(key, out m) && m != null) return m;

        Shader s = ResolveVertexColorAlphaShader();
        m = new Material(s);
        m.name = key;
        m.color = tint;
        if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
        if (tex != null)
        {
            m.mainTexture = tex;
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        }
        m.renderQueue = renderQueue;
        s_MaterialCache[key] = m;
        return m;
    }
}

// ===========================================================================
// SECTION 1.2: SEAMLESS PERIODIC NOISE & 4-CHANNEL TERRAIN PBR FACTORY (256x256)
// ===========================================================================
public static class TerrainNoiseMath
{
    public static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            int h = seed + x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= (h >> 16);
            return (h & 0x7FFFFFFF) / 2147483647.0f;
        }
    }

    public static int Hash2D(int x, int y, int seed)
    {
        unchecked
        {
            int h = seed + x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return h ^ (h >> 16);
        }
    }

    public static float Smoothstep(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / Mathf.Max(0.0001f, b - a));
        return t * t * (3f - 2f * t);
    }

    private static float Quintic(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    public static float PeriodicValueNoise(float u, float v, int period, int seed)
    {
        float px = Mathf.Repeat(u, 1f) * period;
        float py = Mathf.Repeat(v, 1f) * period;
        int x0 = Mathf.FloorToInt(px) % period;
        int y0 = Mathf.FloorToInt(py) % period;
        int x1 = (x0 + 1) % period;
        int y1 = (y0 + 1) % period;

        float fx = Quintic(px - Mathf.Floor(px));
        float fy = Quintic(py - Mathf.Floor(py));

        float v00 = Hash01(x0, y0, seed);
        float v10 = Hash01(x1, y0, seed);
        float v01 = Hash01(x0, y1, seed);
        float v11 = Hash01(x1, y1, seed);

        return Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fy);
    }

    public static float PeriodicFBM(float u, float v, int baseFreq, int octaves, int seed)
    {
        float sum = 0f;
        float amp = 0.5f;
        float norm = 0f;
        int freq = baseFreq;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * PeriodicValueNoise(u, v, freq, seed + i * 97);
            norm += amp;
            amp *= 0.5f;
            freq *= 2;
        }
        return sum / Mathf.Max(0.0001f, norm);
    }

    public static void PeriodicWorleyCellular(float u, float v, int cells, int seed, out float f1, out float f2)
    {
        float px = Mathf.Repeat(u, 1f) * cells;
        float py = Mathf.Repeat(v, 1f) * cells;
        int cx = Mathf.FloorToInt(px);
        int cy = Mathf.FloorToInt(py);
        f1 = 10f;
        f2 = 10f;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = cx + dx;
                int ny = cy + dy;
                int wx = ((nx % cells) + cells) % cells;
                int wy = ((ny % cells) + cells) % cells;

                float jx = Hash01(wx, wy, seed + 13) * 0.8f + 0.1f;
                float jy = Hash01(wx, wy, seed + 29) * 0.8f + 0.1f;
                float d = new Vector2(px - (nx + jx), py - (ny + jy)).magnitude;
                if (d < f1) { f2 = f1; f1 = d; }
                else if (d < f2) { f2 = d; }
            }
        }
    }
}

public static class ProceduralTextureFactory
{
    public static Texture2D GetAsteroidRockCliffTexture() => GetAsteroidRockTexture();
    public static Texture2D GetCheckeredDinerTilesTexture() => GetDinerTilesTexture();
    public static Texture2D GetFrostedIceTexture() => GetFrostIceTexture();
    public static Texture2D GetGlacierIceCliffTexture() => GetGlacierCliffTexture();
    public static Texture2D GetNeonSpaceCircuitTexture() => GetSpaceCircuitTexture();
    public static Texture2D GetPierWoodPlanksTexture() => GetDinerWoodDockTexture();
    public static Texture2D GetVolcanicBasaltCliffTexture() => GetVolcanicBasaltTexture();
    public static Texture2D GetVolcanicGrillGrateTexture() => GetGrillGrateTexture();

    private static Dictionary<string, Texture2D> s_TextureCache = new Dictionary<string, Texture2D>();
    private static Dictionary<string, PBRMaterialSuite> s_PBRSuiteCache = new Dictionary<string, PBRMaterialSuite>();

    public static int Hash2D(int x, int y, int seed) => TerrainNoiseMath.Hash2D(x, y, seed);

    private static int GetActiveTextureResolution()
    {
        if (Aadish3DGame.Instance != null && Aadish3DGame.Instance.CurrentQualityTier == GraphicsQualityTier.Mobile60FPS)
            return 128;
        return 256;
    }

    public static Texture2D BakeSobelNormalMap(string key, float[,] h, int size, float bumpStrength)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
        tex.name = key;
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        tex.anisoLevel = 8;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            int ym = (y - 1 + size) % size;
            int yp = (y + 1) % size;
            for (int x = 0; x < size; x++)
            {
                int xm = (x - 1 + size) % size;
                int xp = (x + 1) % size;

                float dX = (h[xp, ym] + 2f * h[xp, y] + h[xp, yp]) - (h[xm, ym] + 2f * h[xm, y] + h[xm, yp]);
                float dY = (h[xm, yp] + 2f * h[x, yp] + h[xp, yp]) - (h[xm, ym] + 2f * h[x, ym] + h[xp, ym]);
                Vector3 n = new Vector3(-dX * bumpStrength, -dY * bumpStrength, 1.0f).normalized;
                pixels[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1.0f);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply(true, false);
        return tex;
    }

    public static Texture2D BakeMetallicSmoothnessMap(string key, float[,] metallic, float[,] smoothness, int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
        tex.name = key;
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        tex.anisoLevel = 8;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float m = Mathf.Clamp01(metallic[x, y]);
                float s = Mathf.Clamp01(smoothness[x, y]);
                pixels[y * size + x] = new Color(m, m, m, s);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply(true, false);
        return tex;
    }

    private static Texture2D MakeColorTex(string name, Color[] cols, int size, bool mip = true)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, mip, false);
        tex.name = name;
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        tex.anisoLevel = 8;
        tex.SetPixels(cols);
        tex.Apply(mip, false);
        return tex;
    }

    public static PBRMaterialSuite GetWorldTerrainPBRSuite(int worldIndex, bool isCliff)
    {
        int res = GetActiveTextureResolution();
        string key = "TerrainPBR_W" + worldIndex + (isCliff ? "_Cliff_" : "_Top_") + res;
        PBRMaterialSuite suite;
        if (s_PBRSuiteCache.TryGetValue(key, out suite)) return suite;

        suite = BakeWorldTerrainPBR(worldIndex, isCliff, res);
        s_PBRSuiteCache[key] = suite;
        return suite;
    }

    public static PBRMaterialSuite GetWaferPBRSuite()
    {
        int res = GetActiveTextureResolution();
        string key = "PBR_W3_FrozenWafer_" + res;
        PBRMaterialSuite suite;
        if (s_PBRSuiteCache.TryGetValue(key, out suite)) return suite;

        Color[] albedo = new Color[res * res];
        float[,] h = new float[res, res];
        float[,] met = new float[res, res];
        float[,] sm = new float[res, res];
        Color baseW = Aadish3DGame.HexColor("#EAB308");
        Color ridge = Aadish3DGame.HexColor("#FDE68A");
        Color recess = Aadish3DGame.HexColor("#92400E");

        for (int y = 0; y < res; y++)
        {
            float v = (float)y / res;
            for (int x = 0; x < res; x++)
            {
                float u = (float)x / res;
                float d1 = Mathf.Abs(Mathf.Sin((u + v) * Mathf.PI * 8f));
                float d2 = Mathf.Abs(Mathf.Sin((u - v) * Mathf.PI * 8f));
                float ridgeMask = Mathf.Max(TerrainNoiseMath.Smoothstep(0.65f, 0.92f, d1), TerrainNoiseMath.Smoothstep(0.65f, 0.92f, d2));
                float micro = TerrainNoiseMath.PeriodicFBM(u, v, 8, 2, 711);
                h[x, y] = ridgeMask * 0.85f + micro * 0.15f;
                albedo[y * res + x] = Color.Lerp(Color.Lerp(recess, baseW, 0.55f), ridge, ridgeMask * 0.85f + micro * 0.12f);
                sm[x, y] = Mathf.Lerp(0.22f, 0.45f, ridgeMask);
            }
        }
        suite = new PBRMaterialSuite {
            AlbedoTex = MakeColorTex("Tex_W3_WaferAlb_" + res, albedo, res),
            NormalTex = BakeSobelNormalMap("Tex_W3_WaferNrm_" + res, h, res, 4.0f),
            MetallicGlossTex = BakeMetallicSmoothnessMap("Tex_W3_WaferMG_" + res, met, sm, res),
            TintColor = Color.white,
            BumpScale = 1.25f
        };
        s_PBRSuiteCache[key] = suite;
        return suite;
    }

    public static PBRMaterialSuite GetOceanOrLavaPBRSuite(int worldIndex)
    {
        int res = GetActiveTextureResolution();
        string key = "PBR_Sea_W" + worldIndex + "_" + res;
        PBRMaterialSuite suite;
        if (s_PBRSuiteCache.TryGetValue(key, out suite)) return suite;

        Color[] albedo = new Color[res * res];
        Color[] emissive = (worldIndex == 4) ? new Color[res * res] : null;
        float[,] h = new float[res, res];
        float[,] met = new float[res, res];
        float[,] sm = new float[res, res];

        for (int y = 0; y < res; y++)
        {
            float v = (float)y / res;
            for (int x = 0; x < res; x++)
            {
                float u = (float)x / res;
                float n1 = TerrainNoiseMath.PeriodicFBM(u, v, 4, 3, 920 + worldIndex);
                float wave = Mathf.Sin(u * Mathf.PI * 12f + n1 * 4f) * 0.5f + Mathf.Cos(v * Mathf.PI * 8f - n1 * 3f) * 0.5f;
                h[x, y] = wave * 0.5f + n1 * 0.5f;

                if (worldIndex == 1)
                {
                    albedo[y * res + x] = Color.Lerp(Aadish3DGame.HexColor("#0284C7"), Aadish3DGame.HexColor("#2DD4BF"), n1 * 0.7f + wave * 0.25f);
                    sm[x, y] = 0.95f;
                }
                else if (worldIndex == 2)
                {
                    albedo[y * res + x] = Color.Lerp(Aadish3DGame.HexColor("#0F172A"), Aadish3DGame.HexColor("#312E81"), n1 * 0.8f + wave * 0.2f);
                    sm[x, y] = 0.97f;
                }
                else if (worldIndex == 3)
                {
                    albedo[y * res + x] = Color.Lerp(Aadish3DGame.HexColor("#0E7490"), Aadish3DGame.HexColor("#BAE6FD"), n1);
                    sm[x, y] = 0.88f;
                }
                else if (worldIndex == 4)
                {
                    float lavaHeat = Mathf.Clamp01(n1 * 1.15f + wave * 0.25f);
                    Color cCrust = Aadish3DGame.HexColor("#270E0A");
                    Color cMagma = Color.Lerp(Aadish3DGame.HexColor("#EF4444"), Aadish3DGame.HexColor("#FDE047"), TerrainNoiseMath.Smoothstep(0.45f, 0.85f, lavaHeat));
                    albedo[y * res + x] = Color.Lerp(cCrust, cMagma, TerrainNoiseMath.Smoothstep(0.32f, 0.72f, lavaHeat));
                    emissive[y * res + x] = Color.Lerp(Color.black, cMagma * 2.4f, TerrainNoiseMath.Smoothstep(0.35f, 0.80f, lavaHeat));
                    sm[x, y] = 0.55f;
                }
                else
                {
                    albedo[y * res + x] = Color.Lerp(Aadish3DGame.HexColor("#030712"), Aadish3DGame.HexColor("#1E1B4B"), n1);
                    sm[x, y] = 0.92f;
                }
            }
        }

        suite = new PBRMaterialSuite {
            AlbedoTex = MakeColorTex("Tex_SeaAlb_W" + worldIndex + "_" + res, albedo, res),
            NormalTex = BakeSobelNormalMap("Tex_SeaNrm_W" + worldIndex + "_" + res, h, res, 1.8f),
            MetallicGlossTex = BakeMetallicSmoothnessMap("Tex_SeaMG_W" + worldIndex + "_" + res, met, sm, res),
            EmissiveTex = (emissive != null) ? MakeColorTex("Tex_SeaEmi_W" + worldIndex + "_" + res, emissive, res) : null,
            TintColor = Color.white,
            BumpScale = 0.9f,
            EmissionTint = (worldIndex == 4) ? new Color(2.2f, 1.0f, 0.35f) : Color.black
        };
        s_PBRSuiteCache[key] = suite;
        return suite;
    }

    private static PBRMaterialSuite BakeWorldTerrainPBR(int worldIndex, bool isCliff, int size)
    {
        Color[] albedo = new Color[size * size];
        Color[] emissive = (!isCliff && (worldIndex == 4 || worldIndex == 5)) || (isCliff && worldIndex == 4) ? new Color[size * size] : null;
        float[,] h = new float[size, size];
        float[,] met = new float[size, size];
        float[,] sm = new float[size, size];
        float bumpStrength = 3.2f;
        Color emissiveTint = Color.black;

        for (int y = 0; y < size; y++)
        {
            float v = (float)y / size;
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                int idx = y * size + x;
                float fbmLarge = TerrainNoiseMath.PeriodicFBM(u, v, 4, 4, 100 + worldIndex * 31 + (isCliff ? 500 : 0));
                float fbmMicro = TerrainNoiseMath.PeriodicFBM(u, v, 16, 2, 211 + worldIndex * 19);
                float cellF1, cellF2;
                TerrainNoiseMath.PeriodicWorleyCellular(u, v, isCliff ? 6 : 8, 307 + worldIndex * 23, out cellF1, out cellF2);

                if (!isCliff)
                {
                    if (worldIndex == 1)
                    {
                        float blade = TerrainNoiseMath.PeriodicValueNoise(u, v, 48, 701);
                        float puddleMask = TerrainNoiseMath.Smoothstep(0.58f, 0.68f, fbmLarge);
                        Color emerald = Aadish3DGame.HexColor("#25B84F");
                        Color sunlitOlive = Aadish3DGame.HexColor("#65D448");
                        Color deepMoss = Aadish3DGame.HexColor("#177A37");
                        Color wetEarth = Aadish3DGame.HexColor("#4B3624");

                        Color c = Color.Lerp(deepMoss, emerald, TerrainNoiseMath.Smoothstep(0.25f, 0.65f, fbmLarge));
                        c = Color.Lerp(c, sunlitOlive, TerrainNoiseMath.Smoothstep(0.48f, 0.80f, fbmLarge) * 0.72f + blade * 0.22f);
                        c = Color.Lerp(c, wetEarth * 0.78f, puddleMask * 0.65f);
                        c *= Mathf.Lerp(1.0f, 0.78f, puddleMask);

                        float dryH = 0.45f + blade * 0.42f + fbmMicro * 0.13f;
                        h[x, y] = Mathf.Lerp(dryH, 0.35f, puddleMask);
                        sm[x, y] = Mathf.Lerp(0.22f + blade * 0.12f, 0.93f, puddleMask);
                        albedo[idx] = c;
                        bumpStrength = 3.6f;
                    }
                    else if (worldIndex == 2)
                    {
                        float tileU = Mathf.Repeat(u * 4f, 1f);
                        float tileV = Mathf.Repeat(v * 4f, 1f);
                        int tx = Mathf.FloorToInt(u * 4f);
                        int ty = Mathf.FloorToInt(v * 4f);
                        bool isChecker = ((tx + ty) % 2 == 0);
                        bool isGrout = (tileU < 0.045f || tileU > 0.955f || tileV < 0.045f || tileV > 0.955f);

                        float asphaltBand = 1f - TerrainNoiseMath.Smoothstep(0.24f, 0.34f, Mathf.Abs(u - 0.5f));
                        float asphaltGrain = TerrainNoiseMath.PeriodicValueNoise(u, v, 64, 882);

                        Color tileCol = isGrout ? Aadish3DGame.HexColor("#78553E") : (isChecker ? Aadish3DGame.HexColor("#FFF3D6") : Aadish3DGame.HexColor("#FB923C"));
                        Color asphaltCol = Color.Lerp(Aadish3DGame.HexColor("#272B36"), Aadish3DGame.HexColor("#373E4D"), asphaltGrain);
                        Color baseCol = Color.Lerp(tileCol, asphaltCol, asphaltBand * 0.72f);

                        float dashV = Mathf.Repeat(v * 4f, 1f);
                        if (Mathf.Abs(u - 0.5f) < 0.055f && dashV > 0.25f && dashV < 0.75f)
                            baseCol = Color.Lerp(baseCol, new Color(0.95f, 0.97f, 1.0f), 0.75f);

                        float wetness = TerrainNoiseMath.Smoothstep(0.51f, 0.60f, fbmLarge);
                        baseCol *= Mathf.Lerp(1.0f, 0.78f, wetness);

                        float dryH = isGrout ? 0.15f : (0.65f + (asphaltGrain - 0.5f) * 0.22f * asphaltBand + (fbmMicro - 0.5f) * 0.10f);
                        h[x, y] = Mathf.Lerp(dryH, 0.42f, wetness);
                        sm[x, y] = Mathf.Lerp(0.30f, 0.96f, wetness);
                        albedo[idx] = baseCol;
                        bumpStrength = 3.4f;
                    }
                    else if (worldIndex == 3)
                    {
                        float crack = 1f - TerrainNoiseMath.Smoothstep(0.0f, 0.09f, cellF2 - cellF1);
                        float snowPocket = TerrainNoiseMath.Smoothstep(0.57f, 0.70f, fbmLarge);

                        Color iceDeep = Aadish3DGame.HexColor("#0EA5E9");
                        Color iceSurface = Aadish3DGame.HexColor("#7DD3FC");
                        Color snowWhite = Aadish3DGame.HexColor("#F1F5F9");

                        Color c = Color.Lerp(iceDeep, iceSurface, 0.45f + fbmMicro * 0.45f);
                        c = Color.Lerp(c, Color.white, crack * 0.55f);
                        c = Color.Lerp(c, snowWhite, snowPocket);

                        float dryIceH = 0.72f - crack * 0.48f + (fbmMicro - 0.5f) * 0.14f;
                        h[x, y] = Mathf.Lerp(dryIceH, 0.85f + fbmMicro * 0.12f, snowPocket);
                        sm[x, y] = Mathf.Lerp(0.94f - crack * 0.35f, 0.24f, snowPocket);
                        albedo[idx] = c;
                        bumpStrength = 3.8f;
                    }
                    else if (worldIndex == 4)
                    {
                        float gridU = Mathf.Repeat(u * 4f, 1f);
                        float gridV = Mathf.Repeat(v * 4f, 1f);
                        bool isSeam = (gridU < 0.06f || gridU > 0.94f || gridV < 0.06f || gridV > 0.94f);
                        float diag = Mathf.Abs(Mathf.Sin((u + v) * Mathf.PI * 16f));
                        float tread = TerrainNoiseMath.Smoothstep(0.84f, 0.96f, diag);
                        float oilPuddle = TerrainNoiseMath.Smoothstep(0.56f, 0.65f, fbmLarge);

                        Color steel = Aadish3DGame.HexColor("#27272A");
                        Color steelHi = Aadish3DGame.HexColor("#52525B");
                        Color lava = Color.Lerp(Aadish3DGame.HexColor("#EA580C"), Aadish3DGame.HexColor("#FDE047"), fbmMicro);

                        if (isSeam)
                        {
                            albedo[idx] = lava;
                            emissive[idx] = lava * 2.5f;
                            h[x, y] = 0.12f;
                            met[x, y] = 0.0f;
                            sm[x, y] = 0.60f;
                        }
                        else
                        {
                            Color baseCol = Color.Lerp(steel, steelHi, tread * 0.7f + fbmMicro * 0.25f) * Mathf.Lerp(1.0f, 0.78f, oilPuddle);
                            albedo[idx] = baseCol;
                            emissive[idx] = Color.black;
                            h[x, y] = Mathf.Lerp(0.65f + tread * 0.28f, 0.60f, oilPuddle);
                            met[x, y] = 0.84f;
                            sm[x, y] = Mathf.Lerp(0.45f, 0.92f, oilPuddle);
                        }
                        emissiveTint = new Color(2.6f, 1.3f, 0.55f);
                        bumpStrength = 4.2f;
                    }
                    else
                    {
                        float gu = Mathf.Repeat(u * 4f, 1f);
                        float gv = Mathf.Repeat(v * 4f, 1f);
                        bool cyanTrace = (gu < 0.045f || gv < 0.045f);
                        bool magTrace = (!cyanTrace && (Mathf.Abs(gu - gv) < 0.032f || (gu > 0.47f && gu < 0.53f && gv > 0.2f && gv < 0.8f)));

                        Color obs = Color.Lerp(Aadish3DGame.HexColor("#090D1F"), Aadish3DGame.HexColor("#171E3E"), fbmMicro);
                        Color cyan = Aadish3DGame.HexColor("#00F5D4");
                        Color magenta = Aadish3DGame.HexColor("#FF2E93");

                        if (cyanTrace)
                        {
                            albedo[idx] = cyan;
                            emissive[idx] = cyan * 2.6f;
                            h[x, y] = 0.22f;
                            sm[x, y] = 0.90f;
                            met[x, y] = 0.2f;
                        }
                        else if (magTrace)
                        {
                            albedo[idx] = magenta;
                            emissive[idx] = magenta * 2.5f;
                            h[x, y] = 0.25f;
                            sm[x, y] = 0.90f;
                            met[x, y] = 0.2f;
                        }
                        else
                        {
                            albedo[idx] = obs;
                            emissive[idx] = Color.black;
                            h[x, y] = 0.72f + (fbmMicro - 0.5f) * 0.06f;
                            sm[x, y] = 0.93f;
                            met[x, y] = 0.35f;
                        }
                        emissiveTint = new Color(2.4f, 2.2f, 2.6f);
                        bumpStrength = 3.5f;
                    }
                }
                else
                {
                    float topAO = TerrainNoiseMath.Smoothstep(0.84f, 1.0f, v);
                    if (worldIndex == 1)
                    {
                        float strata = Mathf.Sin(v * Mathf.PI * 10f + fbmLarge * 2.5f) * 0.5f + 0.5f;
                        Color clay = Aadish3DGame.HexColor("#8C5232");
                        Color lime = Aadish3DGame.HexColor("#C28E68");
                        Color crevice = Aadish3DGame.HexColor("#522E1A");
                        Color c = Color.Lerp(clay, lime, strata * 0.55f);
                        if (cellF1 < 0.28f) c = Color.Lerp(c, Aadish3DGame.HexColor("#A8896E"), 0.45f);
                        c = Color.Lerp(c, crevice, topAO * 0.65f);
                        h[x, y] = strata * 0.45f + (cellF2 - cellF1) * 0.4f + fbmMicro * 0.15f;
                        sm[x, y] = 0.20f;
                        albedo[idx] = c;
                        bumpStrength = 4.2f;
                    }
                    else if (worldIndex == 2)
                    {
                        float plankV = Mathf.Repeat(v * 4f, 1f);
                        bool plankSeam = (plankV < 0.05f || plankV > 0.95f);
                        float grain = Mathf.Sin(u * Mathf.PI * 32f + fbmLarge * 5f) * 0.5f + 0.5f;
                        bool bolt = (Mathf.Repeat(u * 4f, 1f) < 0.08f && Mathf.Abs(plankV - 0.5f) < 0.08f);
                        Color wood = Color.Lerp(Aadish3DGame.HexColor("#683919"), Aadish3DGame.HexColor("#A16233"), grain * 0.65f + fbmMicro * 0.2f);
                        Color c = plankSeam ? Aadish3DGame.HexColor("#381D0B") : (bolt ? Aadish3DGame.HexColor("#94A3B8") : wood);
                        c *= (1f - topAO * 0.48f);
                        h[x, y] = plankSeam ? 0.12f : (bolt ? 0.92f : 0.60f + grain * 0.18f);
                        met[x, y] = bolt ? 0.85f : 0.0f;
                        sm[x, y] = bolt ? 0.80f : 0.28f;
                        albedo[idx] = c;
                        bumpStrength = 3.8f;
                    }
                    else if (worldIndex == 3)
                    {
                        float bevelCell = TerrainNoiseMath.Smoothstep(0.02f, 0.14f, cellF2 - cellF1);
                        Color deep = Aadish3DGame.HexColor("#075985");
                        Color mid = Aadish3DGame.HexColor("#0284C7");
                        Color rim = Aadish3DGame.HexColor("#BAE6FD");
                        Color c = Color.Lerp(deep, mid, cellF1 * 0.8f);
                        c = Color.Lerp(rim, c, bevelCell);
                        c *= (1f - topAO * 0.35f);
                        h[x, y] = bevelCell * 0.75f + fbmMicro * 0.25f;
                        sm[x, y] = 0.86f;
                        albedo[idx] = c;
                        bumpStrength = 4.0f;
                    }
                    else if (worldIndex == 4)
                    {
                        float fissure = 1f - TerrainNoiseMath.Smoothstep(0.01f, 0.08f, cellF2 - cellF1);
                        Color rock = Color.Lerp(Aadish3DGame.HexColor("#18181B"), Aadish3DGame.HexColor("#27272A"), fbmMicro);
                        Color glow = Aadish3DGame.HexColor("#FF4500");
                        albedo[idx] = Color.Lerp(rock, glow, fissure) * (1f - topAO * 0.40f);
                        emissive[idx] = glow * (fissure * 2.1f);
                        emissiveTint = new Color(2.2f, 0.9f, 0.3f);
                        h[x, y] = (cellF2 - cellF1) * 0.7f + fbmMicro * 0.3f;
                        sm[x, y] = 0.30f;
                        bumpStrength = 4.4f;
                    }
                    else
                    {
                        float craterRim = 1f - TerrainNoiseMath.Smoothstep(0.05f, 0.35f, Mathf.Abs(cellF1 - 0.32f));
                        Color baseRock = Aadish3DGame.HexColor("#27245E");
                        Color deepCrater = Aadish3DGame.HexColor("#141136");
                        Color c = Color.Lerp(deepCrater, baseRock, TerrainNoiseMath.Smoothstep(0.18f, 0.45f, cellF1));
                        c = Color.Lerp(c, Aadish3DGame.HexColor("#4F46E5"), craterRim * 0.35f) * (1f - topAO * 0.42f);
                        h[x, y] = TerrainNoiseMath.Smoothstep(0.12f, 0.40f, cellF1) * 0.65f + craterRim * 0.25f + fbmMicro * 0.1f;
                        met[x, y] = craterRim * 0.25f;
                        sm[x, y] = 0.38f;
                        albedo[idx] = c;
                        bumpStrength = 4.0f;
                    }
                }
            }
        }

        string prefix = "W" + worldIndex + (isCliff ? "_Cliff_" : "_Top_") + size;
        return new PBRMaterialSuite {
            AlbedoTex = MakeColorTex("Tex_" + prefix + "_Alb", albedo, size),
            NormalTex = BakeSobelNormalMap("Tex_" + prefix + "_Nrm", h, size, bumpStrength),
            MetallicGlossTex = BakeMetallicSmoothnessMap("Tex_" + prefix + "_MG", met, sm, size),
            EmissiveTex = (emissive != null) ? MakeColorTex("Tex_" + prefix + "_Emi", emissive, size) : null,
            TintColor = Color.white,
            BumpScale = 1.25f,
            EmissionTint = emissiveTint
        };
    }

    public static Texture2D GetWorldGroundTexture(int world) => GetWorldTerrainPBRSuite(world, false).AlbedoTex;
    public static Texture2D GetWorldCliffTexture(int world) => GetWorldTerrainPBRSuite(world, true).AlbedoTex;

    public static Color GetWorldTopColor(int world)
    {
        switch (world)
        {
            case 1: return Aadish3DGame.HexColor("#2ECC5A");
            case 2: return Color.white;
            case 3: return Aadish3DGame.HexColor("#BAE6FD");
            case 4: return Aadish3DGame.HexColor("#3F3F46");
            case 5: return Aadish3DGame.HexColor("#1E1B4B");
            default: return Color.white;
        }
    }

    public static Color GetWorldLipColor(int world)
    {
        switch (world)
        {
            case 1: return Aadish3DGame.HexColor("#D6C29F");
            case 2: return Aadish3DGame.HexColor("#FF2E93");
            case 3: return Aadish3DGame.HexColor("#E0F2FE");
            case 4: return Aadish3DGame.HexColor("#FF5500");
            case 5: return Aadish3DGame.HexColor("#00F5D4");
            default: return Color.gray;
        }
    }

    public static Color GetWorldCliffColor(int world)
    {
        switch (world)
        {
            case 1: return Aadish3DGame.HexColor("#8C5232");
            case 2: return Aadish3DGame.HexColor("#9A5B2C");
            case 3: return Aadish3DGame.HexColor("#0369A1");
            case 4: return Aadish3DGame.HexColor("#18181B");
            case 5: return Aadish3DGame.HexColor("#312E81");
            default: return Color.white;
        }
    }

    public static Texture2D GetGrassTurfTexture() => GetWorldTerrainPBRSuite(1, false).AlbedoTex;
    public static Texture2D GetRichSoilCliffTexture() => GetWorldTerrainPBRSuite(1, true).AlbedoTex;
    public static Texture2D GetDinerTilesTexture() => GetWorldTerrainPBRSuite(2, false).AlbedoTex;
    public static Texture2D GetDinerWoodDockTexture() => GetWorldTerrainPBRSuite(2, true).AlbedoTex;
    public static Texture2D GetFrostIceTexture() => GetWorldTerrainPBRSuite(3, false).AlbedoTex;
    public static Texture2D GetGlacierCliffTexture() => GetWorldTerrainPBRSuite(3, true).AlbedoTex;
    public static Texture2D GetWaferCrissCrossTexture() => GetWaferPBRSuite().AlbedoTex;
    public static Texture2D GetGrillGrateTexture() => GetWorldTerrainPBRSuite(4, false).AlbedoTex;
    public static Texture2D GetVolcanicBasaltTexture() => GetWorldTerrainPBRSuite(4, true).AlbedoTex;
    public static Texture2D GetMoltenLavaTexture() => GetOceanOrLavaPBRSuite(4).AlbedoTex;
    public static Texture2D GetSpaceCircuitTexture() => GetWorldTerrainPBRSuite(5, false).AlbedoTex;
    public static Texture2D GetAsteroidRockTexture() => GetWorldTerrainPBRSuite(5, true).AlbedoTex;

    public static Texture2D GetSoftRadialGlowTexture()
    {
        if (s_TextureCache.ContainsKey("Tex_SoftRadialGlow")) return s_TextureCache["Tex_SoftRadialGlow"];
        int sz = 64;
        Texture2D tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
        tex.name = "Tex_SoftRadialGlow";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color[] cols = new Color[sz * sz];
        float half = sz * 0.5f;
        for (int y = 0; y < sz; y++)
            for (int x = 0; x < sz; x++)
            {
                float d = new Vector2((x - half) / half, (y - half) / half).magnitude;
                cols[y * sz + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f));
            }
        tex.SetPixels(cols); tex.Apply();
        s_TextureCache["Tex_SoftRadialGlow"] = tex;
        return tex;
    }

    public static Texture2D GetSodaStripesTexture()
    {
        if (s_TextureCache.ContainsKey("Tex_SodaStripes")) return s_TextureCache["Tex_SodaStripes"];
        int sz = 64;
        Texture2D tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
        tex.name = "Tex_SodaStripes"; tex.wrapMode = TextureWrapMode.Repeat; tex.filterMode = FilterMode.Bilinear;
        Color[] cols = new Color[sz * sz];
        Color red = Aadish3DGame.HexColor("#EF4444");
        for (int y = 0; y < sz; y++) for (int x = 0; x < sz; x++) cols[y * sz + x] = (x % 16 < 8) ? red : Color.white;
        tex.SetPixels(cols); tex.Apply();
        s_TextureCache["Tex_SodaStripes"] = tex;
        return tex;
    }

    public static Texture2D GetToastedBunTexture()
    {
        if (s_TextureCache.ContainsKey("Tex_ToastedBun")) return s_TextureCache["Tex_ToastedBun"];
        int sz = 128;
        Texture2D tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
        tex.name = "Tex_ToastedBun"; tex.wrapMode = TextureWrapMode.Repeat; tex.filterMode = FilterMode.Bilinear;
        Color[] cols = new Color[sz * sz];
        Color bun = Aadish3DGame.HexColor("#E69F45");
        Color speck = Aadish3DGame.HexColor("#D97706");
        Color sesame = Aadish3DGame.HexColor("#FEF3C7");
        for (int y = 0; y < sz; y++)
            for (int x = 0; x < sz; x++)
            {
                int h = Hash2D(x, y, 808);
                Color c = Color.Lerp(speck, bun, TerrainNoiseMath.PeriodicFBM((float)x / sz, (float)y / sz, 6, 2, 811));
                if (((h >> 3) & 31) == 0) c = sesame;
                cols[y * sz + x] = c;
            }
        tex.SetPixels(cols); tex.Apply();
        s_TextureCache["Tex_ToastedBun"] = tex;
        return tex;
    }

    public static Texture2D GetGrillMarksTexture()
    {
        if (s_TextureCache.ContainsKey("Tex_GrillMarks")) return s_TextureCache["Tex_GrillMarks"];
        int sz = 64;
        Texture2D tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
        tex.name = "Tex_GrillMarks"; tex.wrapMode = TextureWrapMode.Repeat; tex.filterMode = FilterMode.Bilinear;
        Color[] cols = new Color[sz * sz];
        Color meat = Aadish3DGame.HexColor("#5C2C16");
        Color charLine = Aadish3DGame.HexColor("#291208");
        for (int y = 0; y < sz; y++) for (int x = 0; x < sz; x++) cols[y * sz + x] = ((x + y) % 16 <= 2) ? charLine : meat;
        tex.SetPixels(cols); tex.Apply();
        s_TextureCache["Tex_GrillMarks"] = tex;
        return tex;
    }

    public static Texture2D GetHazardStripeTexture()
    {
        if (s_TextureCache.ContainsKey("Tex_HazardStripe")) return s_TextureCache["Tex_HazardStripe"];
        int sz = 64;
        Texture2D tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
        tex.name = "Tex_HazardStripe"; tex.wrapMode = TextureWrapMode.Repeat; tex.filterMode = FilterMode.Bilinear;
        Color[] cols = new Color[sz * sz];
        Color yel = Aadish3DGame.HexColor("#FACC15");
        Color blk = Aadish3DGame.HexColor("#18181B");
        for (int y = 0; y < sz; y++) for (int x = 0; x < sz; x++) cols[y * sz + x] = ((x + y) % 16 < 8) ? yel : blk;
        tex.SetPixels(cols); tex.Apply();
        s_TextureCache["Tex_HazardStripe"] = tex;
        return tex;
    }

    public static Texture2D GetCandyCaneTexture()
    {
        if (s_TextureCache.ContainsKey("Tex_CandyCane")) return s_TextureCache["Tex_CandyCane"];
        int sz = 64;
        Texture2D tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
        tex.name = "Tex_CandyCane"; tex.wrapMode = TextureWrapMode.Repeat; tex.filterMode = FilterMode.Bilinear;
        Color[] cols = new Color[sz * sz];
        Color red = Aadish3DGame.HexColor("#EF4444");
        for (int y = 0; y < sz; y++) for (int x = 0; x < sz; x++) cols[y * sz + x] = ((x + y) % 16 < 8) ? red : Color.white;
        tex.SetPixels(cols); tex.Apply();
        s_TextureCache["Tex_CandyCane"] = tex;
        return tex;
    }

    public static Texture2D GetBlobShadowTexture()
    {
        if (s_TextureCache.ContainsKey("Tex_BlobShadow")) return s_TextureCache["Tex_BlobShadow"];
        int sz = 64;
        Texture2D tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
        tex.name = "Tex_BlobShadow"; tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
        Color[] cols = new Color[sz * sz];
        float half = sz * 0.5f;
        for (int y = 0; y < sz; y++)
            for (int x = 0; x < sz; x++)
            {
                float dist = new Vector2((x - half) / half, (y - half) / half).magnitude;
                cols[y * sz + x] = new Color(0.04f, 0.06f, 0.12f, Mathf.Clamp01(1f - dist) * 0.58f);
            }
        tex.SetPixels(cols); tex.Apply();
        s_TextureCache["Tex_BlobShadow"] = tex;
        return tex;
    }

    public static Texture2D GetShockwaveRingTexture()
    {
        if (s_TextureCache.ContainsKey("Tex_ShockwaveRing")) return s_TextureCache["Tex_ShockwaveRing"];
        int sz = 64;
        Texture2D tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
        tex.name = "Tex_ShockwaveRing"; tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
        Color[] cols = new Color[sz * sz];
        float half = sz * 0.5f;
        for (int y = 0; y < sz; y++)
            for (int x = 0; x < sz; x++)
            {
                float dist = new Vector2((x - half) / half, (y - half) / half).magnitude;
                float ring = 1f - Mathf.Abs(dist - 0.85f) * 6.5f;
                cols[y * sz + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(ring));
            }
        tex.SetPixels(cols); tex.Apply();
        s_TextureCache["Tex_ShockwaveRing"] = tex;
        return tex;
    }
}

// ===========================================================================
// SECTION 1.3: LIVING LAND WIND FOLIAGE, DYNAMIC POINT LIGHT POOL & SKY ENGINE
// ===========================================================================
public class LivingLandWindSystem : MonoBehaviour
{
    public static LivingLandWindSystem Instance;

    public struct WindSwayItem
    {
        public Transform Tr;
        public Quaternion BaseRot;
        public float Phase;
        public float Amplitude;
        public float Freq;
        public float ZPos;
    }

    private List<WindSwayItem> m_Items = new List<WindSwayItem>(256);

    void Awake() { Instance = this; }

    public void RegisterItem(Transform tr, float amplitude = 6f, float freq = 2.2f)
    {
        if (tr == null) return;
        m_Items.Add(new WindSwayItem {
            Tr = tr,
            BaseRot = tr.localRotation,
            Phase = tr.position.x * 0.35f + tr.position.z * 0.25f,
            Amplitude = amplitude,
            Freq = freq,
            ZPos = tr.position.z
        });
    }

    public void ClearItems() => m_Items.Clear();

    void Update()
    {
        if (m_Items.Count == 0) return;
        float t = Time.time;
        float playerZ = (Aadish3DGame.Instance != null && Aadish3DGame.Instance.Player != null)
            ? Aadish3DGame.Instance.Player.transform.position.z : 0f;
        bool cullFar = (Aadish3DGame.Instance != null && Aadish3DGame.Instance.CurrentState == Aadish3DGame.GameState.Playing && Aadish3DGame.Instance.LevelFinishZ > 120f);

        for (int i = m_Items.Count - 1; i >= 0; i--)
        {
            var item = m_Items[i];
            if (item.Tr == null) { m_Items.RemoveAt(i); continue; }
            if (cullFar && Mathf.Abs(item.ZPos - playerZ) > 76f) continue;

            float wave = Mathf.Sin(t * item.Freq + item.Phase) * item.Amplitude;
            float wave2 = Mathf.Cos(t * (item.Freq * 0.7f) + item.Phase * 1.3f) * (item.Amplitude * 0.45f);
            item.Tr.localRotation = item.BaseRot * Quaternion.Euler(wave2, 0f, wave);
        }
    }
}

public class DynamicLightPool : MonoBehaviour
{
    public static DynamicLightPool Instance;

    private Light m_BossLight;
    private Light m_PlayerLight;
    private Light[] m_FlashLights = new Light[2];
    private float[] m_FlashTimers = new float[2];
    private float[] m_FlashMaxDurs = new float[2];
    private float[] m_FlashStartInt = new float[2];
    private Light[] m_EnvLights = new Light[4];

    private List<Transform> m_RegisteredProps = new List<Transform>();
    private List<Color> m_RegisteredColors = new List<Color>();
    private float m_EnvAssignTimer = 0f;

    void Awake()
    {
        Instance = this;
        BuildPool();
    }

    void BuildPool()
    {
        m_BossLight = MakePointLight("Pool_BossLight", Color.yellow, 26f, 0f);
        m_PlayerLight = MakePointLight("Pool_PlayerLight", new Color(0.3f, 0.7f, 1f), 11f, 0f);
        for (int i = 0; i < 2; i++) m_FlashLights[i] = MakePointLight("Pool_FlashLight_" + i, Color.white, 18f, 0f);
        for (int i = 0; i < 4; i++) m_EnvLights[i] = MakePointLight("Pool_EnvLight_" + i, Color.cyan, 14f, 0f);
    }

    Light MakePointLight(string name, Color col, float range, float intensity)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = col;
        l.range = range;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
        return l;
    }

    public void ClearEnvTargets()
    {
        m_RegisteredProps.Clear();
        m_RegisteredColors.Clear();
        for (int i = 0; i < m_EnvLights.Length; i++)
            if (m_EnvLights[i] != null) m_EnvLights[i].intensity = 0f;
    }

    public void RegisterEnvLightTarget(Transform tr, Color col)
    {
        if (tr == null) return;
        m_RegisteredProps.Add(tr);
        m_RegisteredColors.Add(col);
    }

    public void TriggerFlash(Vector3 pos, Color col, float intensity = 4.5f, float duration = 0.24f, float range = 16f)
    {
        int slot = (m_FlashTimers[0] <= m_FlashTimers[1]) ? 0 : 1;
        Light l = m_FlashLights[slot];
        if (l == null) return;
        l.transform.position = pos;
        l.color = col;
        l.range = range;
        l.intensity = intensity;
        m_FlashStartInt[slot] = intensity;
        m_FlashMaxDurs[slot] = Mathf.Max(0.05f, duration);
        m_FlashTimers[slot] = m_FlashMaxDurs[slot];
    }

    public void SetBossAura(bool active, Vector3 pos, Color col, float intensity, float range)
    {
        if (m_BossLight == null) return;
        if (!active) { m_BossLight.intensity = 0f; return; }
        m_BossLight.transform.position = pos;
        m_BossLight.color = col;
        m_BossLight.intensity = intensity;
        m_BossLight.range = range;
    }

    void Update()
    {
        int maxAllowed = 8;
        if (Aadish3DGame.Instance != null)
        {
            if (Aadish3DGame.Instance.CurrentQualityTier == GraphicsQualityTier.High) maxAllowed = 4;
            else if (Aadish3DGame.Instance.CurrentQualityTier == GraphicsQualityTier.Mobile60FPS) maxAllowed = 2;
        }

        if (m_PlayerLight != null)
        {
            var g = Aadish3DGame.Instance;
            if (g != null && g.Player != null && g.CurrentState == Aadish3DGame.GameState.Playing)
            {
                m_PlayerLight.transform.position = g.Player.transform.position + Vector3.up * 1.4f;
                if (g.EquippedSkin == 1) { m_PlayerLight.color = new Color(1f, 0.85f, 0.25f); m_PlayerLight.intensity = 2.4f; }
                else if (g.EquippedSkin == 2) { m_PlayerLight.color = new Color(0.15f, 0.95f, 1f); m_PlayerLight.intensity = 2.8f; }
                else { m_PlayerLight.color = new Color(0.25f, 0.55f, 1f); m_PlayerLight.intensity = 1.3f; }
            }
            else m_PlayerLight.intensity = 0f;
        }

        for (int i = 0; i < 2; i++)
        {
            if (m_FlashLights[i] == null) continue;
            if (i == 1 && maxAllowed <= 2) { m_FlashLights[i].intensity = 0f; continue; }
            if (m_FlashTimers[i] > 0f)
            {
                m_FlashTimers[i] -= Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(m_FlashTimers[i] / m_FlashMaxDurs[i]);
                m_FlashLights[i].intensity = m_FlashStartInt[i] * t * t;
            }
            else m_FlashLights[i].intensity = 0f;
        }

        int envSlots = (maxAllowed >= 8) ? 4 : ((maxAllowed >= 4) ? 1 : 0);
        if (envSlots == 0 || m_RegisteredProps.Count == 0)
        {
            for (int i = 0; i < m_EnvLights.Length; i++) if (m_EnvLights[i] != null) m_EnvLights[i].intensity = 0f;
            return;
        }

        m_EnvAssignTimer -= Time.unscaledDeltaTime;
        if (m_EnvAssignTimer <= 0f)
        {
            m_EnvAssignTimer = 0.18f;
            Vector3 refPos = (Aadish3DGame.Instance != null && Aadish3DGame.Instance.Player != null)
                ? Aadish3DGame.Instance.Player.transform.position
                : Vector3.zero;

            for (int slot = 0; slot < m_EnvLights.Length; slot++)
            {
                if (slot >= envSlots) { m_EnvLights[slot].intensity = 0f; continue; }
                float bestD2 = 34f * 34f;
                int bestIdx = -1;
                for (int i = 0; i < m_RegisteredProps.Count; i++)
                {
                    Transform pr = m_RegisteredProps[i];
                    if (pr == null) continue;
                    bool used = false;
                    for (int prev = 0; prev < slot; prev++)
                    {
                        if (m_EnvLights[prev].intensity > 0f && (m_EnvLights[prev].transform.position - pr.position).sqrMagnitude < 1.5f)
                        { used = true; break; }
                    }
                    if (used) continue;
                    float d2 = (pr.position - refPos).sqrMagnitude;
                    if (d2 < bestD2) { bestD2 = d2; bestIdx = i; }
                }

                if (bestIdx >= 0)
                {
                    m_EnvLights[slot].transform.position = m_RegisteredProps[bestIdx].position + Vector3.up * 0.4f;
                    m_EnvLights[slot].color = m_RegisteredColors[bestIdx];
                    m_EnvLights[slot].intensity = 2.8f;
                }
                else m_EnvLights[slot].intensity = 0f;
            }
        }
    }
}

public class AbyssSeaAnimator : MonoBehaviour
{
    public Renderer SeaRenderer;
    public Vector2 ScrollSpeed = new Vector2(0.03f, -0.05f);
    private Vector2 m_Offset = Vector2.zero;

    void Update()
    {
        if (SeaRenderer == null || SeaRenderer.sharedMaterial == null) return;
        m_Offset += ScrollSpeed * Time.deltaTime;
        m_Offset.x = Mathf.Repeat(m_Offset.x, 1f);
        m_Offset.y = Mathf.Repeat(m_Offset.y, 1f);
        Material mat = SeaRenderer.sharedMaterial;
        mat.mainTextureOffset = m_Offset;
        if (mat.HasProperty("_BaseMap")) mat.SetTextureOffset("_BaseMap", m_Offset);
        if (mat.HasProperty("_MainTex")) mat.SetTextureOffset("_MainTex", m_Offset);
        if (mat.HasProperty("_BumpMap")) mat.SetTextureOffset("_BumpMap", new Vector2(-m_Offset.x * 1.4f, m_Offset.y * 1.1f));
    }
}

public class VolumetricSweepController : MonoBehaviour
{
    public Vector3 BaseEuler;
    public float Phase;
    public bool TrackBoss = true;
    public Renderer BeamRenderer;
    private int m_WorldIndex = 1;

    public void SetWorld(int w) { m_WorldIndex = w; }

    void Update()
    {
        if (BeamRenderer != null && Aadish3DGame.Instance != null)
        {
            bool hide = (Aadish3DGame.Instance.CurrentQualityTier == GraphicsQualityTier.Mobile60FPS);
            if (BeamRenderer.enabled == hide) BeamRenderer.enabled = !hide;
            if (hide) return;

            if (Aadish3DGame.Instance.ActiveBoss != null && Aadish3DGame.Instance.InBossFight)
            {
                Color c = Aadish3DGame.Instance.ActiveBoss.IsPhase2Enraged
                    ? Color.Lerp(Aadish3DGame.HexColor("#FF2E93"), Aadish3DGame.HexColor("#EF4444"), Mathf.Sin(Time.time * 8f) * 0.5f + 0.5f)
                    : Aadish3DGame.HexColor("#FDE047");
                BeamRenderer.sharedMaterial = SafeMaterialFactory.GetCachedVertexAlphaMaterial("VolSpot_" + m_WorldIndex + (Aadish3DGame.Instance.ActiveBoss.IsPhase2Enraged ? "_P2" : "_P1"), c, null, 3050);
            }
        }

        if (TrackBoss && Aadish3DGame.Instance != null && Aadish3DGame.Instance.ActiveBoss != null && (Aadish3DGame.Instance.InBossFight || Aadish3DGame.Instance.CurrentState == Aadish3DGame.GameState.Dialogue))
        {
            Vector3 toBoss = Aadish3DGame.Instance.ActiveBoss.transform.position - transform.position;
            if (toBoss.sqrMagnitude > 0.5f)
            {
                Quaternion look = Quaternion.FromToRotation(Vector3.down, toBoss.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * 4.5f);
                return;
            }
        }
        float sweep = Mathf.Sin(Time.time * 0.9f + Phase) * 14f;
        transform.localRotation = Quaternion.Euler(BaseEuler.x, BaseEuler.y + sweep, BaseEuler.z);
    }
}

public static class CinematicAtmosphereEngine
{
    private static bool s_URPChecked = false;
    private static GameObject s_PostFXVolumeGo = null;

    public static void TrySetupURPPostProcessing()
    {
        if (s_URPChecked && s_PostFXVolumeGo != null) return;
        s_URPChecked = true;
        try
        {
            Type volType = Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
            Type profType = Type.GetType("UnityEngine.Rendering.VolumeProfile, Unity.RenderPipelines.Core.Runtime");
            if (volType == null || profType == null) return;

            if (s_PostFXVolumeGo == null)
            {
                s_PostFXVolumeGo = new GameObject("URP_CinematicPostFX");
                UnityEngine.Object.DontDestroyOnLoad(s_PostFXVolumeGo);
            }
            var vol = s_PostFXVolumeGo.GetComponent(volType) ?? s_PostFXVolumeGo.AddComponent(volType);
            PropertyInfo isGlobalProp = volType.GetProperty("isGlobal");
            if (isGlobalProp != null) isGlobalProp.SetValue(vol, true);

            object profile = ScriptableObject.CreateInstance(profType);
            PropertyInfo sharedProf = volType.GetProperty("sharedProfile");
            if (sharedProf != null) sharedProf.SetValue(vol, profile);

            MethodInfo addComp = profType.GetMethod("Add", new Type[] { typeof(Type), typeof(bool) });
            if (addComp != null)
            {
                Type bloomType = Type.GetType("UnityEngine.Rendering.Universal.Bloom, Unity.RenderPipelines.Universal.Runtime");
                if (bloomType != null)
                {
                    object bloom = addComp.Invoke(profile, new object[] { bloomType, true });
                    SetVolumeParam(bloom, "threshold", 0.90f);
                    SetVolumeParam(bloom, "intensity", 0.48f);
                }
                Type toneType = Type.GetType("UnityEngine.Rendering.Universal.Tonemapping, Unity.RenderPipelines.Universal.Runtime");
                if (toneType != null)
                {
                    object tone = addComp.Invoke(profile, new object[] { toneType, true });
                    SetVolumeParam(tone, "mode", 2);
                }
            }
        }
        catch { }
    }

    private static void SetVolumeParam(object comp, string fieldName, object val)
    {
        if (comp == null) return;
        try
        {
            FieldInfo fi = comp.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (fi == null) return;
            object param = fi.GetValue(comp);
            if (param == null) return;
            FieldInfo ov = param.GetType().GetField("overrideState", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo vf = param.GetType().GetField("value", BindingFlags.Public | BindingFlags.Instance);
            if (ov != null) ov.SetValue(param, true);
            if (vf != null)
            {
                if (vf.FieldType.IsEnum && val is int) val = Enum.ToObject(vf.FieldType, (int)val);
                vf.SetValue(param, val);
            }
        }
        catch { }
    }

    public static Mesh BuildGradientSkyDomeMesh(int worldIndex)
    {
        Mesh src = LowPolyMeshHelper.GetSkyDomeMesh(420f, 16, 24);
        Mesh clone = new Mesh();
        clone.name = "SkyDome_W" + worldIndex;
        Vector3[] verts = src.vertices;
        Vector2[] uvs = src.uv;
        Color[] cols = new Color[verts.Length];

        Color bot = Aadish3DGame.HexColor("#FB7185");
        Color mid = Aadish3DGame.HexColor("#FDBA74");
        Color top = Aadish3DGame.HexColor("#0EA5E9");

        switch (worldIndex)
        {
            case 1:
                bot = Aadish3DGame.HexColor("#FB7185");
                mid = Aadish3DGame.HexColor("#FDBA74");
                top = Aadish3DGame.HexColor("#0EA5E9");
                break;
            case 2:
                bot = Aadish3DGame.HexColor("#FB923C");
                mid = Aadish3DGame.HexColor("#EC4899");
                top = Aadish3DGame.HexColor("#1E1B4B");
                break;
            case 3:
                bot = Aadish3DGame.HexColor("#7DD3FC");
                mid = Aadish3DGame.HexColor("#38BDF8");
                top = Aadish3DGame.HexColor("#172554");
                break;
            case 4:
                bot = Aadish3DGame.HexColor("#EA580C");
                mid = Aadish3DGame.HexColor("#7F1D1D");
                top = Aadish3DGame.HexColor("#18181B");
                break;
            case 5:
                bot = Aadish3DGame.HexColor("#4C1D95");
                mid = Aadish3DGame.HexColor("#1E1B4B");
                top = Aadish3DGame.HexColor("#020410");
                break;
        }

        for (int i = 0; i < verts.Length; i++)
        {
            float v = uvs[i].y;
            if (v < 0.50f) cols[i] = bot;
            else if (v < 0.66f) cols[i] = Color.Lerp(bot, mid, (v - 0.50f) / 0.16f);
            else cols[i] = Color.Lerp(mid, top, (v - 0.66f) / 0.34f);
        }

        clone.vertices = verts;
        clone.uv = uvs;
        clone.triangles = src.triangles;
        clone.colors = cols;
        clone.RecalculateBounds();
        return clone;
    }
}

public class BossFloorTelegraph : MonoBehaviour
{
    private float m_Duration;
    private float m_Timer;
    private Vector3 m_TargetScale;
    private bool m_IsLine;

    public static BossFloorTelegraph SpawnRing(Vector3 pos, float radius, Color col, float duration)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "BossTelegraph_Ring";
        go.transform.position = new Vector3(pos.x, 1.08f, pos.z);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());

        BossFloorTelegraph tel = go.AddComponent<BossFloorTelegraph>();
        tel.m_Duration = duration;
        tel.m_TargetScale = Vector3.one * (radius * 2f);
        tel.m_IsLine = false;
        go.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedAdditiveGlowMaterial("TelRingMat_" + col.GetHashCode(), col, ProceduralTextureFactory.GetShockwaveRingTexture());
        go.transform.localScale = tel.m_TargetScale * 0.25f;
        return tel;
    }

    public static BossFloorTelegraph SpawnChargeLane(Vector3 startPos, Vector3 dir, float length, Color col, float duration)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "BossTelegraph_Lane";
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
        dir.Normalize();

        go.transform.position = new Vector3(startPos.x, 1.09f, startPos.z) + dir * (length * 0.5f);
        go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());

        BossFloorTelegraph tel = go.AddComponent<BossFloorTelegraph>();
        tel.m_Duration = duration;
        tel.m_TargetScale = new Vector3(4.2f, length, 1f);
        tel.m_IsLine = true;
        go.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedAdditiveGlowMaterial("TelLaneMat_" + col.GetHashCode(), col);
        go.transform.localScale = new Vector3(4.2f, 0.5f, 1f);
        return tel;
    }

    void Update()
    {
        m_Timer += Time.deltaTime;
        float t = Mathf.Clamp01(m_Timer / Mathf.Max(0.01f, m_Duration));
        if (m_IsLine)
            transform.localScale = new Vector3(m_TargetScale.x, Mathf.Lerp(1f, m_TargetScale.y, Mathf.Min(1f, t * 1.4f)), 1f);
        else
            transform.localScale = Vector3.Lerp(m_TargetScale * 0.25f, m_TargetScale, t);

        if (m_Timer >= m_Duration) Destroy(gameObject);
    }
}

public class BossProjectile : MonoBehaviour
{
    public Vector3 Velocity;
    public bool Homing;
    public float HomingStrength = 2.2f;
    public bool IsExpandingRing;
    public float MaxRingRadius = 14.5f;
    public float RingExpandSpeed = 14f;
    public float Lifetime = 4.0f;
    public float HitRadius = 1.15f;

    private float m_CurRingRadius = 1.5f;
    private bool m_HitPlayer = false;

    public static BossProjectile SpawnOrb(Vector3 pos, Vector3 vel, Color col, float scale = 0.9f, bool homing = false)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "BossProjectile_Orb";
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * scale;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("BossOrbMat_" + col.GetHashCode(), col, null, 0.9f, 0f, col * 1.8f);

        GameObject halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        halo.transform.SetParent(go.transform, false);
        halo.transform.localScale = Vector3.one * 2.2f;
        UnityEngine.Object.DestroyImmediate(halo.GetComponent<Collider>());
        halo.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedAdditiveGlowMaterial("OrbGlowMat_" + col.GetHashCode(), col, ProceduralTextureFactory.GetSoftRadialGlowTexture());
        halo.AddComponent<BillboardToCamera>();

        BossProjectile p = go.AddComponent<BossProjectile>();
        p.Velocity = vel;
        p.Homing = homing;
        p.HitRadius = scale * 0.85f;
        return p;
    }

    public static BossProjectile SpawnShockwaveRing(Vector3 pos, Color col, float maxRadius = 14.5f, float speed = 15f)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "BossShockwaveRing";
        go.transform.position = new Vector3(pos.x, 1.12f, pos.z);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale = Vector3.one * 3.0f;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedAdditiveGlowMaterial("ShockMat_" + col.GetHashCode(), col, ProceduralTextureFactory.GetShockwaveRingTexture());

        BossProjectile p = go.AddComponent<BossProjectile>();
        p.IsExpandingRing = true;
        p.MaxRingRadius = maxRadius;
        p.RingExpandSpeed = speed;
        p.Lifetime = (maxRadius / speed) + 0.1f;
        return p;
    }

    void Update()
    {
        if (Aadish3DGame.Instance == null || !Aadish3DGame.Instance.BossAlive)
        {
            Destroy(gameObject);
            return;
        }

        float dt = Time.deltaTime;
        Lifetime -= dt;
        if (Lifetime <= 0f) { Destroy(gameObject); return; }

        var player = Aadish3DGame.Instance.Player;

        if (IsExpandingRing)
        {
            m_CurRingRadius += RingExpandSpeed * dt;
            transform.localScale = Vector3.one * (m_CurRingRadius * 2f);
            if (!m_HitPlayer && player != null)
            {
                Vector3 pPos = player.transform.position;
                if (pPos.y <= 2.35f)
                {
                    float dXZ = new Vector2(pPos.x - transform.position.x, pPos.z - transform.position.z).magnitude;
                    if (Mathf.Abs(dXZ - m_CurRingRadius * 0.85f) <= 1.25f)
                    {
                        m_HitPlayer = true;
                        Aadish3DGame.Instance.DamagePlayer(1, transform.position);
                    }
                }
            }
            if (m_CurRingRadius >= MaxRingRadius) Destroy(gameObject);
            return;
        }

        if (Homing && player != null)
        {
            Vector3 desired = ((player.transform.position + Vector3.up * 0.8f) - transform.position).normalized * Velocity.magnitude;
            Velocity = Vector3.Slerp(Velocity, desired, HomingStrength * dt);
        }

        transform.position += Velocity * dt;
        transform.Rotate(Vector3.one * (240f * dt));

        if (player != null && Vector3.Distance(transform.position, player.transform.position + Vector3.up * 0.85f) <= HitRadius)
        {
            Aadish3DGame.Instance.DamagePlayer(1, transform.position);
            if (JuiceParticlePool.Instance != null) JuiceParticlePool.Instance.SpawnJuiceSplashParticles(transform.position, 8);
            Destroy(gameObject);
        }
    }
}

public class BillboardToCamera : MonoBehaviour
{
    void LateUpdate()
    {
        if (Aadish3DGame.Instance != null && Aadish3DGame.Instance.MainCam != null)
        {
            transform.forward = Aadish3DGame.Instance.MainCam.transform.forward;
        }
    }
}

public class DamagePopupFloater : MonoBehaviour
{
    private float m_Timer = 0f;
    private const float DURATION = 0.65f;

    public static void Spawn(Vector3 worldPos, string text, Color col)
    {
        GameObject go = new GameObject("DamagePopup");
        go.transform.position = worldPos;
        TextMesh tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.color = col;
        tm.fontSize = 36;
        tm.characterSize = 0.16f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.fontStyle = FontStyle.Bold;
        Font f = Aadish3DGame.GetSafeFont();
        if (f != null)
        {
            tm.font = f;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null && f.material != null) mr.sharedMaterial = f.material;
        }
        go.AddComponent<BillboardToCamera>();
        go.AddComponent<DamagePopupFloater>();
    }

    void Update()
    {
        m_Timer += Time.deltaTime;
        transform.position += Vector3.up * (2.6f * Time.deltaTime);
        if (m_Timer >= DURATION) Destroy(gameObject);
    }
}

public class TouchDPadJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public RectTransform Knob;
    public float MaxRadius = 65f;
    private RectTransform m_BgRt;

    void Awake() { m_BgRt = GetComponent<RectTransform>(); }

    public void OnPointerDown(PointerEventData eventData) { OnDrag(eventData); }

    public void OnDrag(PointerEventData eventData)
    {
        if (m_BgRt == null) return;
        Vector2 localPos;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(m_BgRt, eventData.position, eventData.pressEventCamera, out localPos))
        {
            Vector2 centered = localPos - (Vector2)m_BgRt.rect.center;
            Vector2 clamped = Vector2.ClampMagnitude(centered, MaxRadius);
            if (Knob != null) Knob.anchoredPosition = clamped;
            if (Aadish3DGame.Instance != null)
            {
                Aadish3DGame.Instance.TouchMoveInput = clamped / Mathf.Max(1f, MaxRadius);
            }
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (Knob != null) Knob.anchoredPosition = Vector2.zero;
        if (Aadish3DGame.Instance != null) Aadish3DGame.Instance.TouchMoveInput = Vector2.zero;
    }
}

public class UIInstantPressButton : MonoBehaviour, IPointerDownHandler
{
    public Action OnPressDown;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (OnPressDown != null) OnPressDown();
    }
}
#endregion


// ===========================================================================
// SECTION 2: PROCEDURAL HIGH-CRAFT UI SPRITE & ICON FACTORY
// ===========================================================================
#region 2. Procedural UI Sprite & Icon Factory (9-Slice Cards, Buttons, Padlocks, Stars, Crowns)
public static class ProceduralUISpriteFactory
{
    private static Dictionary<string, Sprite> _uiSpriteCache = new Dictionary<string, Sprite>();

    private static float SdRoundBox(Vector2 p, Vector2 b, float r)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x) - b.x + r, Mathf.Abs(p.y) - b.y + r);
        return Mathf.Min(Mathf.Max(q.x, q.y), 0.0f) + new Vector2(Mathf.Max(q.x, 0.0f), Mathf.Max(q.y, 0.0f)).magnitude - r;
    }

    // 1. Sprite_RoundedCard_24 (96x96, 9-slice border 26, 26, 26, 26)
    public static Sprite GetRoundedCardSprite()
    {
        if (_uiSpriteCache.ContainsKey("RoundedCard")) return _uiSpriteCache["RoundedCard"];
        int size = 96;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 half = new Vector2(size * 0.5f, size * 0.5f);
        float r = 22f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - half.x, y + 0.5f - half.y);
                float d = SdRoundBox(p, half, r);
                float a = Mathf.Clamp01(-d / 1.5f);
                if (a <= 0f) { tex.SetPixel(x, y, Color.clear); continue; }

                float bright = 1.0f;
                // Subtle 2px bright top-inner specular highlight (+15% alpha/brightness)
                if (y > 48 && d < -2f) bright += 0.15f * ((y - 48f) / 48f);
                // Clean 2.5px soft outer rim
                if (d > -3.5f && d <= 0f) bright *= 0.92f;
                tex.SetPixel(x, y, new Color(bright, bright, bright, a));
            }
        }
        tex.Apply();
        Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, new Vector4(26, 26, 26, 26));
        _uiSpriteCache["RoundedCard"] = s;
        return s;
    }

    // 2. Sprite_GlossyButton (96x96, 9-slice border 26, 26, 26, 26)
    public static Sprite GetGlossyButtonSprite()
    {
        if (_uiSpriteCache.ContainsKey("GlossyButton")) return _uiSpriteCache["GlossyButton"];
        int size = 96;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 half = new Vector2(size * 0.5f, size * 0.5f);
        float r = 24f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - half.x, y + 0.5f - half.y);
                float d = SdRoundBox(p, half, r);
                float a = Mathf.Clamp01(-d / 1.5f);
                if (a <= 0f) { tex.SetPixel(x, y, Color.clear); continue; }

                float bright = 1.0f;
                if (y >= 59) // Top 38%: glossy specular sheen (+18%)
                    bright = 1.0f + 0.18f * ((y - 59f) / 37f);
                else if (y < 14) // Bottom 14% (8px thick lip): darkened 24% 3D bevel lip
                    bright = 0.76f + 0.10f * (y / 14f);
                else
                    bright = 1.0f;

                tex.SetPixel(x, y, new Color(bright, bright, bright, a));
            }
        }
        tex.Apply();
        Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, new Vector4(26, 26, 26, 26));
        _uiSpriteCache["GlossyButton"] = s;
        return s;
    }

    // 3. Sprite_OutlineFrame_24 (96x96, 9-slice border 26, 26, 26, 26)
    public static Sprite GetOutlineFrameSprite()
    {
        if (_uiSpriteCache.ContainsKey("OutlineFrame")) return _uiSpriteCache["OutlineFrame"];
        int size = 96;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 half = new Vector2(size * 0.5f, size * 0.5f);
        float r = 22f;
        float stroke = 4.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - half.x, y + 0.5f - half.y);
                float d = SdRoundBox(p, half, r);
                float outerAlpha = Mathf.Clamp01(-d / 1.5f);
                float innerAlpha = Mathf.Clamp01(-(d + stroke) / 1.5f);
                float a = Mathf.Clamp01(outerAlpha - innerAlpha);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, new Vector4(26, 26, 26, 26));
        _uiSpriteCache["OutlineFrame"] = s;
        return s;
    }

    // 4. Sprite_ProgressTrack (96x32, 9-slice border 16, 10, 16, 10)
    public static Sprite GetProgressTrackSprite()
    {
        if (_uiSpriteCache.ContainsKey("ProgressTrack")) return _uiSpriteCache["ProgressTrack"];
        int w = 96, h = 32;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 half = new Vector2(w * 0.5f, h * 0.5f);
        float r = 15f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - half.x, y + 0.5f - half.y);
                float d = SdRoundBox(p, half, r);
                float a = Mathf.Clamp01(-d / 1.5f);
                if (a <= 0f) { tex.SetPixel(x, y, Color.clear); continue; }
                float shade = (y > h * 0.55f) ? 0.72f : 0.88f;
                tex.SetPixel(x, y, new Color(shade, shade, shade, a));
            }
        }
        tex.Apply();
        Sprite s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, new Vector4(16, 10, 16, 10));
        _uiSpriteCache["ProgressTrack"] = s;
        return s;
    }

    // 5. Sprite_ProgressFill (96x32, 9-slice border 16, 10, 16, 10)
    public static Sprite GetProgressFillSprite()
    {
        if (_uiSpriteCache.ContainsKey("ProgressFill")) return _uiSpriteCache["ProgressFill"];
        int w = 96, h = 32;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 half = new Vector2(w * 0.5f, h * 0.5f);
        float r = 15f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - half.x, y + 0.5f - half.y);
                float d = SdRoundBox(p, half, r);
                float a = Mathf.Clamp01(-d / 1.5f);
                if (a <= 0f) { tex.SetPixel(x, y, Color.clear); continue; }

                float bright = 1.0f;
                // 45 deg diagonal candy stripe highlights
                if (((x + y) % 16) < 8) bright += 0.12f;
                // Top shine line
                if (y >= h - 6) bright += 0.20f;
                tex.SetPixel(x, y, new Color(bright, bright, bright, a));
            }
        }
        tex.Apply();
        Sprite s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, new Vector4(16, 10, 16, 10));
        _uiSpriteCache["ProgressFill"] = s;
        return s;
    }

    // 6. Sprite_SoftVignette (128x128)
    public static Sprite GetSoftVignetteSprite()
    {
        if (_uiSpriteCache.ContainsKey("SoftVignette")) return _uiSpriteCache["SoftVignette"];
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        float center = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - center) / center;
                float dy = (y + 0.5f - center) / center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float a = 0f;
                if (dist > 0.58f)
                {
                    float t = Mathf.Clamp01((dist - 0.58f) / 0.42f);
                    a = Mathf.SmoothStep(0f, 0.32f, t);
                }
                tex.SetPixel(x, y, new Color(0f, 0f, 0f, a));
            }
        }
        tex.Apply();
        Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        _uiSpriteCache["SoftVignette"] = s;
        return s;
    }

    // 7. Sprite_PadlockClosed (64x64)
    public static Sprite GetPadlockClosedSprite()
    {
        if (_uiSpriteCache.ContainsKey("PadlockClosed")) return _uiSpriteCache["PadlockClosed"];
        int s = 64;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                Color col = Color.clear;

                // Shackle Arch: center (32, 42), outer radius 13, inner radius 7.5, vertical legs down to y=30
                float archDist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32f, 42f));
                bool inArch = (archDist <= 13f && archDist >= 7.5f && y >= 42);
                bool inLegs = (y >= 30 && y < 42 && ((x >= 19 && x <= 24) || (x >= 40 && x <= 45)));
                if (inArch || inLegs)
                {
                    col = new Color(0.92f, 0.95f, 0.98f, 1f);
                }

                // Lock body: width 34, height 26, center (32, 19), cornerRadius 6
                float dBody = SdRoundBox(new Vector2(x + 0.5f - 32f, y + 0.5f - 19f), new Vector2(17f, 13f), 6f);
                if (dBody <= 0f)
                {
                    float topShine = (y > 24) ? 1.05f : ((y < 10) ? 0.82f : 1.0f);
                    col = new Color(0.96f * topShine, 0.97f * topShine, 0.99f * topShine, 1f);

                    float dKeyCirc = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32f, 22f));
                    bool inKeySlot = (y >= 12 && y <= 21 && Mathf.Abs(x + 0.5f - 32f) <= Mathf.Lerp(1.6f, 2.8f, (y - 12f) / 9f));
                    if (dKeyCirc <= 3.8f || inKeySlot)
                    {
                        col = new Color(0.12f, 0.16f, 0.24f, 1f);
                    }
                }

                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        _uiSpriteCache["PadlockClosed"] = sp;
        return sp;
    }

    // 8. Sprite_PadlockOpen (64x64)
    public static Sprite GetPadlockOpenSprite()
    {
        if (_uiSpriteCache.ContainsKey("PadlockOpen")) return _uiSpriteCache["PadlockOpen"];
        int s = 64;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                Color col = Color.clear;

                float archDist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(33f, 49f));
                bool inArch = (archDist <= 13f && archDist >= 7.5f && y >= 49);
                bool inLegs = (y >= 36 && y < 49 && (x >= 40 && x <= 45));
                if (inArch || inLegs)
                {
                    col = new Color(0.92f, 0.95f, 0.98f, 1f);
                }

                float dBody = SdRoundBox(new Vector2(x + 0.5f - 32f, y + 0.5f - 19f), new Vector2(17f, 13f), 6f);
                if (dBody <= 0f)
                {
                    float topShine = (y > 24) ? 1.05f : ((y < 10) ? 0.82f : 1.0f);
                    col = new Color(0.96f * topShine, 0.97f * topShine, 0.99f * topShine, 1f);

                    float dKeyCirc = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32f, 22f));
                    bool inKeySlot = (y >= 12 && y <= 21 && Mathf.Abs(x + 0.5f - 32f) <= Mathf.Lerp(1.6f, 2.8f, (y - 12f) / 9f));
                    if (dKeyCirc <= 3.8f || inKeySlot)
                    {
                        col = new Color(0.12f, 0.16f, 0.24f, 1f);
                    }
                }

                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        _uiSpriteCache["PadlockOpen"] = sp;
        return sp;
    }

    // 9. Sprite_StarFilled (64x64)
    public static Sprite GetStarFilledSprite()
    {
        if (_uiSpriteCache.ContainsKey("StarFilled")) return _uiSpriteCache["StarFilled"];
        int s = 64;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 center = new Vector2(32f, 32f);

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - center;
                float dist = p.magnitude;
                float angle = Mathf.Atan2(p.y, p.x);
                if (angle < 0) angle += Mathf.PI * 2f;
                float segment = (Mathf.PI * 2f) / 5f;
                float localAng = Mathf.Repeat(angle + segment * 0.25f, segment) - (segment * 0.5f);
                float rStar = Mathf.Lerp(11.5f, 26f, Mathf.Cos(localAng * 2.5f));

                if (dist <= rStar + 1.2f)
                {
                    if (dist > rStar - 2.2f)
                    {
                        tex.SetPixel(x, y, new Color(0.57f, 0.25f, 0.05f, 1f));
                    }
                    else
                    {
                        float t = (y - 10f) / 44f;
                        Color grad = (t > 0.5f) ? Color.Lerp(new Color(0.98f, 0.80f, 0.08f), new Color(0.99f, 0.94f, 0.54f), (t - 0.5f) * 2f)
                                                : Color.Lerp(new Color(0.92f, 0.70f, 0.03f), new Color(0.98f, 0.80f, 0.08f), t * 2f);
                        if (Vector2.Distance(new Vector2(x, y), new Vector2(26f, 40f)) < 3.2f)
                            grad = Color.white;
                        tex.SetPixel(x, y, grad);
                    }
                }
                else
                {
                    tex.SetPixel(x, y, Color.clear);
                }
            }
        }
        tex.Apply();
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        _uiSpriteCache["StarFilled"] = sp;
        return sp;
    }

    // 10. Sprite_StarEmpty (64x64)
    public static Sprite GetStarEmptySprite()
    {
        if (_uiSpriteCache.ContainsKey("StarEmpty")) return _uiSpriteCache["StarEmpty"];
        int s = 64;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 center = new Vector2(32f, 32f);

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - center;
                float dist = p.magnitude;
                float angle = Mathf.Atan2(p.y, p.x);
                if (angle < 0) angle += Mathf.PI * 2f;
                float segment = (Mathf.PI * 2f) / 5f;
                float localAng = Mathf.Repeat(angle + segment * 0.25f, segment) - (segment * 0.5f);
                float rStar = Mathf.Lerp(11.5f, 26f, Mathf.Cos(localAng * 2.5f));

                if (dist <= rStar + 1.2f)
                {
                    if (dist > rStar - 2.5f)
                    {
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, 0.42f));
                    }
                    else
                    {
                        tex.SetPixel(x, y, new Color(0.06f, 0.09f, 0.16f, 0.42f));
                    }
                }
                else
                {
                    tex.SetPixel(x, y, Color.clear);
                }
            }
        }
        tex.Apply();
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        _uiSpriteCache["StarEmpty"] = sp;
        return sp;
    }

    // 11. Sprite_BossCrown (64x64)
    public static Sprite GetBossCrownSprite()
    {
        if (_uiSpriteCache.ContainsKey("BossCrown")) return _uiSpriteCache["BossCrown"];
        int s = 64;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                Color col = Color.clear;
                if (y >= 16 && y <= 24 && x >= 14 && x <= 50)
                {
                    col = new Color(1f, 0.84f, 0.0f, 1f);
                    if (y <= 17 || y >= 23 || x <= 15 || x >= 49)
                        col = new Color(0.70f, 0.33f, 0.04f, 1f);
                }
                float leftPeakH = Mathf.Max(0f, 44f - Mathf.Abs(x - 16f) * 2.4f);
                float midPeakH = Mathf.Max(0f, 50f - Mathf.Abs(x - 32f) * 2.1f);
                float rightPeakH = Mathf.Max(0f, 44f - Mathf.Abs(x - 48f) * 2.4f);
                float maxH = Mathf.Max(leftPeakH, Mathf.Max(midPeakH, rightPeakH));

                if (y >= 24 && y <= maxH && x >= 14 && x <= 50)
                {
                    col = new Color(1f, 0.84f, 0.0f, 1f);
                    if (y >= maxH - 2f) col = new Color(0.70f, 0.33f, 0.04f, 1f);
                }
                if (Vector2.Distance(new Vector2(x, y), new Vector2(16, 46)) <= 3.8f ||
                    Vector2.Distance(new Vector2(x, y), new Vector2(32, 52)) <= 4.2f ||
                    Vector2.Distance(new Vector2(x, y), new Vector2(48, 46)) <= 3.8f)
                {
                    col = new Color(1f, 0.92f, 0.23f, 1f);
                }
                if (Vector2.Distance(new Vector2(x, y), new Vector2(32, 20)) <= 3.6f)
                {
                    col = new Color(0.94f, 0.27f, 0.27f, 1f);
                }

                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        _uiSpriteCache["BossCrown"] = sp;
        return sp;
    }

    // 12. Sprite_Checkmark (64x64)
    public static Sprite GetCheckmarkSprite()
    {
        if (_uiSpriteCache.ContainsKey("Checkmark")) return _uiSpriteCache["Checkmark"];
        int s = 64;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 center = new Vector2(32f, 32f);

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                if (dist <= 28f)
                {
                    if (dist > 25.5f)
                        tex.SetPixel(x, y, Color.white);
                    else
                        tex.SetPixel(x, y, new Color(0.13f, 0.77f, 0.37f, 1f));
                }
                else
                {
                    tex.SetPixel(x, y, Color.clear);
                }
            }
        }
        DrawLine(tex, new Vector2(20, 32), new Vector2(28, 22), Color.white, 3.2f);
        DrawLine(tex, new Vector2(28, 22), new Vector2(44, 42), Color.white, 3.2f);

        tex.Apply();
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        _uiSpriteCache["Checkmark"] = sp;
        return sp;
    }

    // 13. Sprite_CircleBadge (64x64)
    public static Sprite GetCircleBadgeSprite()
    {
        if (_uiSpriteCache.ContainsKey("CircleBadge")) return _uiSpriteCache["CircleBadge"];
        int s = 64;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 center = new Vector2(32f, 32f);

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                if (dist <= 29f)
                {
                    float a = Mathf.Clamp01(29.5f - dist);
                    float bevel = (y < 12) ? 0.88f : 1.0f;
                    tex.SetPixel(x, y, new Color(bevel, bevel, bevel, a));
                }
                else
                {
                    tex.SetPixel(x, y, Color.clear);
                }
            }
        }
        tex.Apply();
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        _uiSpriteCache["CircleBadge"] = sp;
        return sp;
    }

    // 14. Sprite_ChevronArrow (64x64)
    public static Sprite GetChevronArrowSprite()
    {
        if (_uiSpriteCache.ContainsKey("ChevronArrow")) return _uiSpriteCache["ChevronArrow"];
        int s = 64;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                tex.SetPixel(x, y, Color.clear);
            }
        }
        DrawLine(tex, new Vector2(22, 14), new Vector2(44, 32), Color.white, 4f);
        DrawLine(tex, new Vector2(44, 32), new Vector2(22, 50), Color.white, 4f);

        tex.Apply();
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        _uiSpriteCache["ChevronArrow"] = sp;
        return sp;
    }

    // 15. Sprite_GearIcon (64x64)
    public static Sprite GetGearIconSprite()
    {
        if (_uiSpriteCache.ContainsKey("GearIcon")) return _uiSpriteCache["GearIcon"];
        int s = 64;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 center = new Vector2(32f, 32f);

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - center;
                float dist = p.magnitude;
                float angle = Mathf.Atan2(p.y, p.x);
                float teeth = Mathf.Sin(angle * 8f);
                float rOuter = 24f + teeth * 4.5f;

                if (dist <= rOuter && dist >= 9f)
                    tex.SetPixel(x, y, Color.white);
                else
                    tex.SetPixel(x, y, Color.clear);
            }
        }
        tex.Apply();
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        _uiSpriteCache["GearIcon"] = sp;
        return sp;
    }

    private static void DrawLine(Texture2D tex, Vector2 p1, Vector2 p2, Color col, float radius)
    {
        int steps = Mathf.CeilToInt(Vector2.Distance(p1, p2) * 2f);
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            Vector2 pt = Vector2.Lerp(p1, p2, t);
            int minX = Mathf.Clamp(Mathf.FloorToInt(pt.x - radius), 0, tex.width - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(pt.x + radius), 0, tex.width - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(pt.y - radius), 0, tex.height - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(pt.y + radius), 0, tex.height - 1);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (Vector2.Distance(new Vector2(x, y), pt) <= radius)
                    {
                        tex.SetPixel(x, y, col);
                    }
                }
            }
        }
    }
}

public class UIBouncyButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public float HoverScale = 1.06f;
    public float PressScale = 0.93f;
    public float SpringSpeed = 18f;
    private Vector3 m_BaseScale = Vector3.one;
    private Vector3 m_TargetScale = Vector3.one;
    private RectTransform m_Rt;
    private Vector2 m_BasePos = Vector2.zero;
    private float m_ShakeTimer = 0f;
    private float m_ShakeDuration = 0.28f;
    private Image m_LockedFlashTarget;
    private Color m_OriginalLockColor = Color.white;
    private Button m_Button;

    void Awake()
    {
        m_Rt = GetComponent<RectTransform>();
        if (m_Rt != null)
        {
            m_BaseScale = m_Rt.localScale;
            m_BasePos = m_Rt.anchoredPosition;
        }
        m_Button = GetComponent<Button>();
    }

    void Update()
    {
        if (m_Rt == null) return;
        float dt = Time.unscaledDeltaTime;

        m_Rt.localScale = Vector3.Lerp(m_Rt.localScale, m_TargetScale, dt * SpringSpeed);

        if (m_ShakeTimer > 0f)
        {
            m_ShakeTimer -= dt;
            float t = 1f - (m_ShakeTimer / m_ShakeDuration);
            float offsetX = Mathf.Sin(t * 48f) * 11f * (1f - t);
            m_Rt.anchoredPosition = m_BasePos + new Vector2(offsetX, 0f);

            if (m_ShakeTimer <= 0f)
            {
                m_Rt.anchoredPosition = m_BasePos;
                if (m_LockedFlashTarget != null) m_LockedFlashTarget.color = m_OriginalLockColor;
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (m_Button != null && !m_Button.interactable) return;
        m_TargetScale = m_BaseScale * HoverScale;
        if (Aadish3DGame.Instance != null) Aadish3DGame.Instance.PlaySFX(Aadish3DGame.SFXType.UIHover);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        m_TargetScale = m_BaseScale;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (m_Button != null && !m_Button.interactable) return;
        m_TargetScale = m_BaseScale * PressScale;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (m_Button != null && !m_Button.interactable) return;
        m_TargetScale = m_BaseScale;
        if (Aadish3DGame.Instance != null) Aadish3DGame.Instance.PlaySFX(Aadish3DGame.SFXType.Pop);
    }

    public void TriggerLockedShake(Image lockIcon = null)
    {
        m_ShakeTimer = m_ShakeDuration;
        m_LockedFlashTarget = lockIcon;
        if (lockIcon != null)
        {
            m_OriginalLockColor = lockIcon.color;
            lockIcon.color = Aadish3DGame.HexColor("#F87171");
        }
        if (Aadish3DGame.Instance != null) Aadish3DGame.Instance.PlaySFX(Aadish3DGame.SFXType.UILocked);
    }
}

public class UIPulseGlow : MonoBehaviour
{
    public float MinScale = 1.0f;
    public float MaxScale = 1.035f;
    public float PulseSpeed = 4.2f;
    private RectTransform m_Rt;
    private Graphic m_Graphic;
    private CanvasGroup m_Cg;
    private Vector3 m_BaseScale = Vector3.one;

    void Awake()
    {
        m_Rt = GetComponent<RectTransform>();
        m_Graphic = GetComponent<Graphic>();
        m_Cg = GetComponent<CanvasGroup>();
        if (m_Rt != null) m_BaseScale = m_Rt.localScale;
    }

    void Update()
    {
        float t = (Mathf.Sin(Time.unscaledTime * PulseSpeed) + 1f) * 0.5f;
        if (m_Rt != null)
        {
            float s = Mathf.Lerp(MinScale, MaxScale, t);
            m_Rt.localScale = m_BaseScale * s;
        }
        if (m_Cg != null)
        {
            m_Cg.alpha = Mathf.Lerp(0.55f, 1.0f, t);
        }
        else if (m_Graphic != null)
        {
            Color c = m_Graphic.color;
            c.a = Mathf.Lerp(0.55f, 1.0f, t);
            m_Graphic.color = c;
        }
    }
}

public class UIToastBanner : MonoBehaviour
{
    public static UIToastBanner Instance;
    private RectTransform m_Rt;
    private CanvasGroup m_Cg;
    private Text m_Text;
    private Image m_BorderImg;
    private float m_DisplayTimer = 0f;
    private float m_State = 0f;

    void Awake()
    {
        Instance = this;
        m_Rt = GetComponent<RectTransform>();
        m_Cg = GetComponent<CanvasGroup>();
        if (m_Cg == null) m_Cg = gameObject.AddComponent<CanvasGroup>();
        m_Cg.alpha = 0f;
    }

    public static void Show(string message, Color? accentBorder = null)
    {
        if (Instance == null) return;
        Instance.ShowToast(message, accentBorder);
    }

    public void ShowToast(string message, Color? accentBorder = null)
    {
        if (m_Text == null) m_Text = GetComponentInChildren<Text>();
        if (m_BorderImg == null) m_BorderImg = GetComponent<Image>();

        if (m_Text != null) m_Text.text = message;
        if (m_BorderImg != null && accentBorder.HasValue) m_BorderImg.color = accentBorder.Value;

        gameObject.SetActive(true);
        m_DisplayTimer = 2.2f;
        m_State = 1f;
        if (m_Rt != null) m_Rt.anchoredPosition = new Vector2(0f, 40f);
        if (m_Cg != null) m_Cg.alpha = 0f;
    }

    void Update()
    {
        if (m_State == 0f) return;
        float dt = Time.unscaledDeltaTime;

        if (m_State == 1f)
        {
            if (m_Cg != null) m_Cg.alpha = Mathf.MoveTowards(m_Cg.alpha, 1f, dt * 5f);
            if (m_Rt != null)
            {
                m_Rt.anchoredPosition = Vector2.Lerp(m_Rt.anchoredPosition, new Vector2(0f, 95f), dt * 14f);
                if (m_Cg != null && m_Cg.alpha >= 0.98f && Vector2.Distance(m_Rt.anchoredPosition, new Vector2(0f, 95f)) < 1f)
                {
                    m_State = 2f;
                }
            }
        }
        else if (m_State == 2f)
        {
            m_DisplayTimer -= dt;
            if (m_DisplayTimer <= 0f) m_State = 3f;
        }
        else if (m_State == 3f)
        {
            if (m_Cg != null)
            {
                m_Cg.alpha = Mathf.MoveTowards(m_Cg.alpha, 0f, dt * 3.5f);
                if (m_Cg.alpha <= 0.01f)
                {
                    m_State = 0f;
                    gameObject.SetActive(false);
                }
            }
        }
    }
}
#endregion

// ===========================================================================
// SECTION 3: PROGRESSION MANAGER (STRICT SEQUENTIAL LEVEL LOCKING 1->50)
// ===========================================================================
#region 3. Progression Manager (Strict Sequential Level Locking 1->50 & PlayerPrefs)
public static class ProgressionManager
{
    private const string KEY_HIGHEST_UNLOCKED = "BerryBounce_HighestUnlockedLevel";
    private const string KEY_CLEARED_PREFIX   = "BerryBounce_Cleared_";
    private const string KEY_STAR_PREFIX      = "BerryBounce_Star_";
    private const string KEY_BESTTIME_PREFIX  = "BerryBounce_BestTime_";
    private const string KEY_ALL_BEATEN       = "BerryBounce_AllLevelsBeaten";
    private const string KEY_EQUIPPED_SKIN    = "BerryBounce_EquippedSkin";

    public static int NewlyUnlockedLevel { get; set; } = -1;
    public static int NewlyUnlockedWorld { get; set; } = -1;

    public static int GetHighestUnlockedLevel()
    {
        return Mathf.Clamp(PlayerPrefs.GetInt(KEY_HIGHEST_UNLOCKED, 1), 1, 50);
    }

    public static bool IsLevelUnlocked(int globalLevel)
    {
        if (globalLevel <= 1) return true;
        return globalLevel <= GetHighestUnlockedLevel();
    }

    public static bool IsWorldUnlocked(int worldIndex)
    {
        if (worldIndex <= 1) return true;
        int firstLevelOfWorld = (worldIndex - 1) * 10 + 1;
        return IsLevelUnlocked(firstLevelOfWorld);
    }

    public static bool IsLevelCleared(int globalLevel)
    {
        return PlayerPrefs.GetInt(KEY_CLEARED_PREFIX + globalLevel, 0) == 1;
    }

    public static void SetStarCollected(int globalLevel)
    {
        PlayerPrefs.SetInt(KEY_STAR_PREFIX + globalLevel, 1);
        PlayerPrefs.Save();
    }

    public static bool IsStarCollected(int globalLevel)
    {
        return PlayerPrefs.GetInt(KEY_STAR_PREFIX + globalLevel, 0) == 1;
    }

    public static float GetBestTime(int globalLevel)
    {
        return PlayerPrefs.GetFloat(KEY_BESTTIME_PREFIX + globalLevel, 0f);
    }

    public static int GetTotalClearedCount()
    {
        int total = 0;
        for (int i = 1; i <= 50; i++)
        {
            if (IsLevelCleared(i)) total++;
        }
        return total;
    }

    public static int GetTotalStars()
    {
        int total = 0;
        for (int i = 1; i <= 50; i++)
        {
            if (IsStarCollected(i)) total++;
        }
        return total;
    }

    public static int GetWorldClearedCount(int worldIndex)
    {
        int count = 0;
        int startLvl = (worldIndex - 1) * 10 + 1;
        int endLvl = worldIndex * 10;
        for (int i = startLvl; i <= endLvl; i++)
        {
            if (IsLevelCleared(i)) count++;
        }
        return count;
    }

    public static int GetWorldStarCount(int worldIndex)
    {
        int count = 0;
        int startLvl = (worldIndex - 1) * 10 + 1;
        int endLvl = worldIndex * 10;
        for (int i = startLvl; i <= endLvl; i++)
        {
            if (IsStarCollected(i)) count++;
        }
        return count;
    }

    public static void CompleteLevel(int completedLevel, float clearTimeSeconds, bool starCollected)
    {
        PlayerPrefs.SetInt(KEY_CLEARED_PREFIX + completedLevel, 1);
        if (starCollected)
        {
            PlayerPrefs.SetInt(KEY_STAR_PREFIX + completedLevel, 1);
        }

        float oldBest = GetBestTime(completedLevel);
        if (oldBest <= 0f || clearTimeSeconds < oldBest)
        {
            PlayerPrefs.SetFloat(KEY_BESTTIME_PREFIX + completedLevel, clearTimeSeconds);
        }

        int currentHighest = GetHighestUnlockedLevel();
        NewlyUnlockedLevel = -1;
        NewlyUnlockedWorld = -1;

        if (completedLevel < 50)
        {
            int nextLevel = completedLevel + 1;
            if (nextLevel > currentHighest)
            {
                PlayerPrefs.SetInt(KEY_HIGHEST_UNLOCKED, nextLevel);
                NewlyUnlockedLevel = nextLevel;
                if (completedLevel % 10 == 0)
                {
                    NewlyUnlockedWorld = (completedLevel / 10) + 1;
                }
            }
        }
        else
        {
            PlayerPrefs.SetInt(KEY_ALL_BEATEN, 1);
        }

        PlayerPrefs.Save();
    }

    public static void UnlockEverythingTestMode()
    {
        PlayerPrefs.SetInt(KEY_HIGHEST_UNLOCKED, 50);
        PlayerPrefs.SetInt(KEY_ALL_BEATEN, 1);
        for (int i = 1; i <= 50; i++)
        {
            PlayerPrefs.SetInt(KEY_CLEARED_PREFIX + i, 1);
            PlayerPrefs.SetInt(KEY_STAR_PREFIX + i, 1);
        }
        PlayerPrefs.Save();
    }

    public static void ResetAllProgress()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.SetInt(KEY_HIGHEST_UNLOCKED, 1);
        PlayerPrefs.SetInt(KEY_EQUIPPED_SKIN, 0);
        PlayerPrefs.Save();
    }
}
#endregion

// ===========================================================================
// SECTION 6: LEVEL LENGTH CALCULATOR & MODULAR COURSE BUILDER
// ===========================================================================
#region 6. Level Length Calculator (Shortest-to-Longest Curve) & Modular Course Builder
public struct LevelLengthSpec
{
    public int GlobalLevel;          // 1..50
    public int WorldIndex;           // 1..5
    public int StageInWorld;         // 1..10
    public bool IsBossLevel;         // StageInWorld == 10
    public string LengthTierLabel;   // "SHORT", "SHORT+", "MEDIUM", "LONG", "VERY LONG", "MARATHON", "👑 BOSS"
    public Color TierBadgeColor;
    public float TargetCourseMeters; // Total forward Z distance (~62m at L1 -> ~378m at L49)
    public int PlatformCount;        // Monotonically grows from 6 (L1) -> 41 (L49)
    public int CheckpointCount;      // 0 (L1-3) -> 1 (L4-10) -> 2 (L11-20) -> 3 (L21-30) -> 4 (L31-40) -> 5 (L41-49)
    public int EnemyCount;           // 1 (L1) -> 18 (L49)
    public int MovingPlatformCount;  // 0 (L1-2) -> 8 (L49)
    public int SpringDonutCount;     // 0 (L1) -> 6 (L49)
    public int CrumblingWaferCount;  // 0 (W1-W2) -> 3..8 (W3-W5)
    public int HazardClusterCount;   // 0 (L1) -> 14 (L49)
    public float MaxElevationRange;  // ±1.2u (L1) -> ±18.0u (L49)
    public float BossApproachMeters; // Boss levels only: 18m (L10) -> 24m (L20) -> 30m (L30) -> 38m (L40) -> 46m (L50)
    public int BossMaxHP;            // L10=10, L20=14, L30=18, L40=22, L50=28
}

public static class LevelLengthCalculator
{
    public static LevelLengthSpec GetSpec(int globalLevel)
    {
        LevelLengthSpec spec = new LevelLengthSpec();
        spec.GlobalLevel = Mathf.Clamp(globalLevel, 1, 50);
        spec.WorldIndex = Mathf.Clamp((globalLevel - 1) / 10 + 1, 1, 5);
        spec.StageInWorld = ((globalLevel - 1) % 10) + 1;
        spec.IsBossLevel = (spec.StageInWorld == 10);

        if (spec.IsBossLevel)
        {
            spec.LengthTierLabel = "👑 BOSS";
            spec.TierBadgeColor = Aadish3DGame.HexColor("#EF4444");
            switch (spec.WorldIndex)
            {
                case 1: spec.BossApproachMeters = 18f; spec.BossMaxHP = 10; break;
                case 2: spec.BossApproachMeters = 24f; spec.BossMaxHP = 14; break;
                case 3: spec.BossApproachMeters = 30f; spec.BossMaxHP = 18; break;
                case 4: spec.BossApproachMeters = 38f; spec.BossMaxHP = 22; break;
                default: spec.BossApproachMeters = 46f; spec.BossMaxHP = 28; break;
            }
            spec.TargetCourseMeters = spec.BossApproachMeters + 40f;
            spec.PlatformCount = 6;
            spec.CheckpointCount = 1;
            spec.EnemyCount = 1;
            spec.MovingPlatformCount = 0;
            spec.SpringDonutCount = 4;
            spec.CrumblingWaferCount = 0;
            spec.HazardClusterCount = 0;
            spec.MaxElevationRange = 0f;
            return spec;
        }

        // Normal Levels (1..49, not boss)
        int rank = (spec.GlobalLevel - 1) - ((spec.GlobalLevel - 1) / 10); // 0 (L1) -> 44 (L49)
        float t = Mathf.Clamp01(rank / 44f);

        spec.TargetCourseMeters = Mathf.Round(Mathf.Lerp(62f, 378f, Mathf.Pow(t, 0.96f)));
        spec.PlatformCount = Mathf.RoundToInt(Mathf.Lerp(6f, 41f, Mathf.Pow(t, 0.94f)));
        spec.CheckpointCount = Mathf.Clamp(Mathf.FloorToInt(spec.TargetCourseMeters / 64f), 0, 5);
        spec.EnemyCount = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1f, 18f, t)), 1, 18);
        spec.MovingPlatformCount = (rank < 4) ? 0 : Mathf.RoundToInt(Mathf.Lerp(1f, 8f, (rank - 4) / 40f));
        spec.SpringDonutCount = (rank < 2) ? 0 : Mathf.RoundToInt(Mathf.Lerp(1f, 6f, (rank - 2) / 42f));
        spec.CrumblingWaferCount = (spec.WorldIndex <= 2) ? 0 : Mathf.RoundToInt(Mathf.Lerp(3f, 8f, (spec.WorldIndex - 3) / 2f));
        spec.HazardClusterCount = (globalLevel == 1) ? 0 : Mathf.RoundToInt(Mathf.Lerp(1f, 14f, t));
        spec.MaxElevationRange = Mathf.Lerp(1.2f, 20.0f, t);

        if (globalLevel <= 4)
        {
            spec.LengthTierLabel = "SHORT";
            spec.TierBadgeColor = Aadish3DGame.HexColor("#4ADE80");
        }
        else if (globalLevel <= 9)
        {
            spec.LengthTierLabel = "SHORT+";
            spec.TierBadgeColor = Aadish3DGame.HexColor("#86EFAC");
        }
        else if (globalLevel <= 19)
        {
            spec.LengthTierLabel = "MEDIUM";
            spec.TierBadgeColor = Aadish3DGame.HexColor("#FACC15");
        }
        else if (globalLevel <= 29)
        {
            spec.LengthTierLabel = "LONG";
            spec.TierBadgeColor = Aadish3DGame.HexColor("#FB923C");
        }
        else if (globalLevel <= 39)
        {
            spec.LengthTierLabel = "VERY LONG";
            spec.TierBadgeColor = Aadish3DGame.HexColor("#F87171");
        }
        else
        {
            spec.LengthTierLabel = "MARATHON";
            spec.TierBadgeColor = Aadish3DGame.HexColor("#C084FC");
        }

        return spec;
    }
}

public static class ModularCourseBuilder
{
    public static void BuildLevel(Aadish3DGame game, int level, int world)
    {
        LevelLengthSpec spec = LevelLengthCalculator.GetSpec(level);
        System.Random rnd = new System.Random(level * 7919 + world * 313);

        game.PlayerSpawnPoint = new Vector3(0f, 2f, 0f);
        game.CheckpointPoint = game.PlayerSpawnPoint;
        game.CheckpointActivated = false;
        game.ActiveCheckpoints.Clear();

        // 1. Chunk 0: Grand Start Sanctuary (8.5 x 2 x 8.5)
        game.CreateDetailedPlatform(new Vector3(0f, 0f, 0f), new Vector3(8.5f, 2f, 8.5f), world, Aadish3DGame.PlatformKind.Static, "StartSanctuary");

        float curZ = 4.25f;
        float targetZ = spec.TargetCourseMeters;
        int intermediateCount = Mathf.Max(2, spec.PlatformCount - 2);
        float stepZ = (targetZ - curZ - 10f) / intermediateCount;

        float maxSafeGap = (world == 5) ? 10.5f : 7.2f;
        float maxSafeStep = (world == 5) ? 4.8f : 2.9f;

        float nextCheckpointZ = (spec.CheckpointCount > 0) ? (targetZ / (spec.CheckpointCount + 1)) : float.MaxValue;
        int checkpointsPlaced = 0;
        int enemiesPlaced = 0;
        int movingPlaced = 0;
        int donutsPlaced = 0;
        int wafersPlaced = 0;
        bool starPlaced = false;
        float starTargetZ = targetZ * (0.55f + (float)rnd.NextDouble() * 0.25f);

        float curY = 0f;
        float curX = 0f;

        for (int i = 0; i < intermediateCount; i++)
        {
            float gap = Mathf.Clamp(stepZ * 0.35f, 2.2f, maxSafeGap * 0.78f);
            float platLength = Mathf.Max(5f, stepZ - gap);
            float platWidth = (float)rnd.NextDouble() * 3.5f + 5.5f;
            curZ += gap + (platLength * 0.5f);

            float elevationStep = ((float)rnd.NextDouble() * 2f - 1f) * (maxSafeStep * 0.65f);
            curY = Mathf.Clamp(curY + elevationStep, -spec.MaxElevationRange * 0.5f, spec.MaxElevationRange * 0.5f);

            float lateralStep = ((float)rnd.NextDouble() * 2f - 1f) * 4.5f;
            curX = Mathf.Clamp(curX + lateralStep, -7.5f, 7.5f);

            Vector3 platPos = new Vector3(curX, curY, curZ);
            Vector3 platSize = new Vector3(platWidth, 2f, platLength);

            if (checkpointsPlaced < spec.CheckpointCount && curZ >= nextCheckpointZ)
            {
                platSize = new Vector3(8.5f, 2f, 8.5f);
                platPos.x = 0f;
                game.CreateDetailedPlatform(platPos, platSize, world, Aadish3DGame.PlatformKind.Static, "CheckpointOasis_" + checkpointsPlaced);
                GameObject cp = game.CreateCheckpoint(new Vector3(0f, curY + 1.0f, curZ));
                game.ActiveCheckpoints.Add(cp.transform);
                checkpointsPlaced++;
                nextCheckpointZ += (targetZ / (spec.CheckpointCount + 1));
                curZ += platLength * 0.5f;
                continue;
            }

            bool isMoving = (movingPlaced < spec.MovingPlatformCount && rnd.NextDouble() < 0.35 && i > 1);
            bool isWafer = (!isMoving && wafersPlaced < spec.CrumblingWaferCount && rnd.NextDouble() < 0.40 && world >= 3);

            if (isMoving)
            {
                GameObject movePlat = game.CreateDetailedPlatform(platPos, platSize, world, Aadish3DGame.PlatformKind.Moving, "MovingPlat_" + i);
                MovingPlatform mp = movePlat.AddComponent<MovingPlatform>();
                mp.MoveAxis = (rnd.NextDouble() > 0.5) ? Vector3.right : Vector3.up;
                mp.Distance = 3.5f + (float)rnd.NextDouble() * 3.0f;
                mp.Speed = 1.8f + (float)rnd.NextDouble() * 1.2f;
                movingPlaced++;
            }
            else if (isWafer)
            {
                GameObject wafer = game.CreateDetailedPlatform(platPos, platSize, world, Aadish3DGame.PlatformKind.Wafer, "WaferPlat_" + i);
                wafer.AddComponent<WaferPlatform>();
                wafersPlaced++;
            }
            else
            {
                game.CreateDetailedPlatform(platPos, platSize, world, Aadish3DGame.PlatformKind.Static, "Plat_" + i);
            }

            if (donutsPlaced < spec.SpringDonutCount && rnd.NextDouble() < 0.30 && i > 0 && !isWafer)
            {
                game.CreateDonutSpring(platPos + Vector3.up * 1.15f, (world == 5) ? 28f : 24f);
                donutsPlaced++;
            }

            if (!starPlaced && curZ >= starTargetZ)
            {
                game.CreateGoldenStar(platPos + Vector3.up * 3.8f, level);
                starPlaced = true;
            }

            if (enemiesPlaced < spec.EnemyCount && !isWafer && rnd.NextDouble() < 0.65 && i > 0)
            {
                game.SpawnPatrollingEnemy(platPos + Vector3.up * 1.5f, world);
                enemiesPlaced++;
            }

            curZ += platLength * 0.5f;
        }

        if (!starPlaced)
        {
            game.CreateGoldenStar(new Vector3(0f, curY + 3.5f, curZ + 4f), level);
        }

        curZ = Mathf.Max(curZ + 6f, targetZ);
        Vector3 finishPos = new Vector3(0f, curY, curZ);
        game.CreateDetailedPlatform(finishPos, new Vector3(10f, 2f, 10f), world, Aadish3DGame.PlatformKind.Static, "FinishSanctuary");
        game.CreateMiniBerryObject(finishPos + new Vector3(0f, 1.4f, 2f), level);
        game.LevelFinishZ = curZ;

        game.CreateHazardFloor(world);
    }
}
#endregion

// ===========================================================================
// SECTION 4: DIORAMA ANIMATORS, VISUAL RIGS & JUICE PARTICLE POOL
// ===========================================================================
#region 4. Multi-World Live 3D Diorama Stage & Context-Aware Orbit Camera
public class MainMenuDioramaAnimator : MonoBehaviour
{
    public Transform BerryDadPreview;
    public List<Transform> MiniBerries = new List<Transform>();
    public Transform GoldenStar;
    public Transform DonutSpring;
    public List<Transform> DriftingClouds = new List<Transform>();
    public List<Transform> SatelliteRocks = new List<Transform>();

    // 5 World Perimeter Enemies
    public Transform PerimeterEnemy;
    public int WorldIndex = 1;
    public bool IsLockedPreview = false;
    public Transform LockedPadlockHologram;

    private float m_BerryHopTimer = 0f;
    private int m_JumpCounter = 0;
    private float m_EnemyAngle = 0f;
    private Vector3 m_BerryInitialPos = Vector3.zero;
    private Vector3 m_BerryInitialScale = Vector3.one;

    void Start()
    {
        if (BerryDadPreview != null)
        {
            m_BerryInitialPos = BerryDadPreview.localPosition;
            m_BerryInitialScale = BerryDadPreview.localScale;
        }
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float t = Time.unscaledTime;

        // 1. Berry Dad Showcase Animation
        if (BerryDadPreview != null)
        {
            int skin = Aadish3DGame.Instance != null ? Aadish3DGame.Instance.EquippedSkin : 0;
            if (skin == 2) // Flying Diamond Bird Skin: Hovering Figure-8 & Flapping Wings
            {
                float fig8X = Mathf.Sin(t * 1.5f) * 0.9f;
                float fig8Z = Mathf.Sin(t * 3.0f) * 0.45f;
                float hoverY = 1.6f + Mathf.Sin(t * 2.5f) * 0.4f;
                BerryDadPreview.localPosition = m_BerryInitialPos + new Vector3(fig8X, hoverY, fig8Z);
                BerryDadPreview.localRotation = Quaternion.Euler(Mathf.Sin(t * 3f) * 8f, t * 25f, Mathf.Cos(t * 1.5f) * 12f);
            }
            else // Normal & Golden Skins: Playful Hops & Flips
            {
                m_BerryHopTimer += dt;
                float hopDuration = (WorldIndex == 5) ? 2.2f : 1.35f; // Floatier low-gravity arc in World 5!
                float progress = m_BerryHopTimer % hopDuration;

                if (progress < 0.20f) // Anticipation squash
                {
                    BerryDadPreview.localScale = new Vector3(m_BerryInitialScale.x * 1.22f, m_BerryInitialScale.y * 0.78f, m_BerryInitialScale.z * 1.22f);
                    BerryDadPreview.localPosition = m_BerryInitialPos;
                    BerryDadPreview.localRotation = Quaternion.identity;
                }
                else if (progress < 0.85f) // High hop arc
                {
                    float p = (progress - 0.20f) / 0.65f;
                    float arcY = Mathf.Sin(p * Mathf.PI) * ((WorldIndex == 5) ? 2.4f : 1.55f);
                    BerryDadPreview.localPosition = m_BerryInitialPos + Vector3.up * arcY;
                    BerryDadPreview.localScale = new Vector3(m_BerryInitialScale.x * 0.88f, m_BerryInitialScale.y * 1.15f, m_BerryInitialScale.z * 0.88f);

                    if (m_JumpCounter % 3 == 2)
                    {
                        BerryDadPreview.localRotation = Quaternion.Euler(p * 360f, 0f, 0f);
                    }
                    else
                    {
                        BerryDadPreview.localRotation = Quaternion.Euler(0f, Mathf.Sin(p * Mathf.PI * 2f) * 20f, 0f);
                    }
                }
                else // Landing squash & rebound
                {
                    float p = (progress - 0.85f) / 0.15f;
                    float sq = Mathf.Sin(p * Mathf.PI) * 0.28f;
                    BerryDadPreview.localScale = new Vector3(m_BerryInitialScale.x * (1f + sq), m_BerryInitialScale.y * (1f - sq), m_BerryInitialScale.z * (1f + sq));
                    BerryDadPreview.localPosition = m_BerryInitialPos;
                    BerryDadPreview.localRotation = Quaternion.identity;

                    if (progress + dt >= hopDuration)
                    {
                        m_JumpCounter++;
                    }
                }
            }
        }

        // 2. 6 Cheering Mini-Berries Staggered Waddle Jumps
        for (int i = 0; i < MiniBerries.Count; i++)
        {
            Transform mb = MiniBerries[i];
            if (mb == null) continue;
            float phase = t * 4.5f + (i * 1.05f);
            float mbHop = Mathf.Abs(Mathf.Sin(phase)) * 0.38f;
            float mbWaddle = Mathf.Sin(phase * 0.5f) * 12f;
            mb.localPosition = new Vector3(mb.localPosition.x, 0.55f + mbHop, mb.localPosition.z);
            mb.localRotation = Quaternion.Euler(0f, -Mathf.Atan2(mb.localPosition.z, mb.localPosition.x) * Mathf.Rad2Deg + 90f + mbWaddle, mbWaddle * 0.5f);
        }

        // 3. World Perimeter Signature Enemy
        if (PerimeterEnemy != null)
        {
            m_EnemyAngle += dt * 0.45f;
            float rad = 4.15f;
            float ex = Mathf.Cos(m_EnemyAngle) * rad;
            float ez = Mathf.Sin(m_EnemyAngle) * rad;
            float faceAngle = (-m_EnemyAngle * Mathf.Rad2Deg) + 180f;

            if (WorldIndex == 2) // Soda cup hop
            {
                float hop = Mathf.Abs(Mathf.Sin(t * 6f)) * 0.5f;
                PerimeterEnemy.localPosition = new Vector3(ex, 0.55f + hop, ez);
            }
            else if (WorldIndex == 5) // Drone hover
            {
                float hover = Mathf.Sin(t * 3f) * 0.25f;
                PerimeterEnemy.localPosition = new Vector3(ex, 1.2f + hover, ez);
            }
            else
            {
                PerimeterEnemy.localPosition = new Vector3(ex, 0.55f, ez);
            }
            PerimeterEnemy.localRotation = Quaternion.Euler(0f, faceAngle, 0f);
        }

        // 4. Golden Star & Halo Orbit
        if (GoldenStar != null)
        {
            GoldenStar.Rotate(Vector3.up * 65f * dt, Space.World);
            GoldenStar.position += Vector3.up * (Mathf.Sin(t * 2.8f) * 0.0035f);
        }

        // 5. Bouncy Donut Spring Gentle Breathing
        if (DonutSpring != null)
        {
            float sBreath = 1f + Mathf.Sin(t * 3.5f) * 0.04f;
            DonutSpring.localScale = new Vector3(sBreath, 1f / sBreath, sBreath);
        }

        // 6. Drifting Clouds Lazy Orbit
        for (int c = 0; c < DriftingClouds.Count; c++)
        {
            Transform cloud = DriftingClouds[c];
            if (cloud == null) continue;
            cloud.RotateAround(transform.position, Vector3.up, (2.5f + c * 0.4f) * dt);
        }

        // 7. Satellite Pebble Rocks Floating Oscillation
        for (int r = 0; r < SatelliteRocks.Count; r++)
        {
            Transform rock = SatelliteRocks[r];
            if (rock == null) continue;
            rock.Rotate(new Vector3(15f, 25f, 10f) * dt, Space.Self);
        }

        // 8. Translucent Rotating 3D Golden Padlock Hologram
        if (LockedPadlockHologram != null)
        {
            LockedPadlockHologram.Rotate(Vector3.up * 45f * dt, Space.World);
            float padBob = Mathf.Sin(t * 2.4f) * 0.15f;
            LockedPadlockHologram.localPosition = new Vector3(0f, 3.8f + padBob, 0f);
        }
    }
}

public class EnemyVisualAnimator : MonoBehaviour
{
    public Aadish3DGame.EnemyType Type;
    public int KindIndex = 1;
    public Transform VisualRig;
    public Transform StrawTransform;
    public Transform StrawPivot;
    public Transform LeftFoot;
    public Transform RightFoot;
    public Transform GroundShadow;
    public Transform ThrusterRing;
    public List<Transform> FootLeft = new List<Transform>();
    public List<Transform> FootRight = new List<Transform>();

    private Vector3 m_BaseScale = Vector3.one;

    void Start()
    {
        if (VisualRig != null) m_BaseScale = VisualRig.localScale;
        if (VisualRig == null && transform.childCount > 0)
        {
            Transform child = transform.Find("VisualRig") ?? transform.GetChild(0);
            if (child != null) VisualRig = child;
        }
        if (KindIndex == 1) Type = Aadish3DGame.EnemyType.BurgerPatrol;
        else if (KindIndex == 2) Type = Aadish3DGame.EnemyType.SodaCup;
        else if (KindIndex == 3) Type = Aadish3DGame.EnemyType.SundaeMonster;
        else if (KindIndex == 4) Type = Aadish3DGame.EnemyType.HotdogGuard;
        else if (KindIndex == 5) Type = Aadish3DGame.EnemyType.PizzaDrone;
    }

    void Update()
    {
        float t = Time.time;
        float dt = Time.deltaTime;

        if (Aadish3DGame.Instance != null && Aadish3DGame.Instance.Player != null && Aadish3DGame.Instance.CurrentState == Aadish3DGame.GameState.Playing && Aadish3DGame.Instance.LevelFinishZ > 120f)
        {
            if (Mathf.Abs(transform.position.z - Aadish3DGame.Instance.Player.transform.position.z) > 78f) return;
        }
        if (Aadish3DGame.Instance != null && Aadish3DGame.Instance.Player != null && Aadish3DGame.Instance.CurrentState == Aadish3DGame.GameState.Playing && Aadish3DGame.Instance.LevelFinishZ > 120f)
        {
            if (Mathf.Abs(transform.position.z - Aadish3DGame.Instance.Player.transform.position.z) > 78f) return;
        }
        if (Aadish3DGame.Instance != null && Aadish3DGame.Instance.Player != null && Aadish3DGame.Instance.CurrentState == Aadish3DGame.GameState.Playing && Aadish3DGame.Instance.LevelFinishZ > 120f)
        {
            if (Mathf.Abs(transform.position.z - Aadish3DGame.Instance.Player.transform.position.z) > 78f) return;
        }
        Transform activeRig = VisualRig != null ? VisualRig : (transform.childCount > 0 ? transform.GetChild(0) : null);
        if (activeRig == null) return;

        switch (Type)
        {
            case Aadish3DGame.EnemyType.BurgerPatrol:
                float waddleRoll = Mathf.Sin(t * 8f) * 7.5f;
                float stepBounce = Mathf.Abs(Mathf.Sin(t * 8f)) * 0.12f;
                activeRig.localPosition = Vector3.up * stepBounce;
                activeRig.localRotation = Quaternion.Euler(0f, 0f, waddleRoll);
                if (LeftFoot != null) LeftFoot.localRotation = Quaternion.Euler(Mathf.Sin(t * 8f) * 25f, 0f, 0f);
                if (RightFoot != null) RightFoot.localRotation = Quaternion.Euler(-Mathf.Sin(t * 8f) * 25f, 0f, 0f);
                for (int i = 0; i < FootLeft.Count; i++)
                    if (FootLeft[i] != null) FootLeft[i].localRotation = Quaternion.Euler(Mathf.Sin(t * 8f) * 25f, 0f, 0f);
                for (int i = 0; i < FootRight.Count; i++)
                    if (FootRight[i] != null) FootRight[i].localRotation = Quaternion.Euler(-Mathf.Sin(t * 8f) * 25f, 0f, 0f);
                break;

            case Aadish3DGame.EnemyType.SodaCup:
                float hopCycle = (t * 2.8f) % 1.0f;
                if (hopCycle < 0.25f)
                {
                    activeRig.localScale = new Vector3(m_BaseScale.x * 1.25f, m_BaseScale.y * 0.75f, m_BaseScale.z * 1.25f);
                    activeRig.localPosition = Vector3.zero;
                }
                else if (hopCycle < 0.85f)
                {
                    float p = (hopCycle - 0.25f) / 0.6f;
                    float h = Mathf.Sin(p * Mathf.PI) * 1.1f;
                    activeRig.localPosition = Vector3.up * h;
                    activeRig.localScale = new Vector3(m_BaseScale.x * 0.88f, m_BaseScale.y * 1.18f, m_BaseScale.z * 0.88f);
                }
                else
                {
                    activeRig.localPosition = Vector3.zero;
                    activeRig.localScale = m_BaseScale;
                }
                Transform straw = StrawPivot != null ? StrawPivot : StrawTransform;
                if (straw != null)
                {
                    float strawWobble = Mathf.Sin(t * 12f) * 14f;
                    straw.localRotation = Quaternion.Euler(strawWobble, 0f, strawWobble * 0.5f);
                }
                break;

            case Aadish3DGame.EnemyType.SundaeMonster:
                float skateLean = Mathf.Sin(t * 4.5f) * 11f;
                activeRig.localRotation = Quaternion.Euler(8f, 0f, skateLean);
                activeRig.localPosition = Vector3.up * (Mathf.Sin(t * 9f) * 0.05f);
                break;

            case Aadish3DGame.EnemyType.HotdogGuard:
                float dashPitch = 12f + Mathf.Sin(t * 10f) * 4f;
                activeRig.localRotation = Quaternion.Euler(dashPitch, 0f, 0f);
                activeRig.localPosition = Vector3.up * (Mathf.Abs(Mathf.Sin(t * 10f)) * 0.08f);
                break;

            case Aadish3DGame.EnemyType.PizzaDrone:
                float waveY = Mathf.Sin(t * 3.2f) * 0.28f;
                float bankRoll = Mathf.Sin(t * 1.8f) * 12f;
                activeRig.localPosition = Vector3.up * waveY;
                activeRig.localRotation = Quaternion.Euler(Mathf.Cos(t * 2f) * 6f, 0f, bankRoll);
                if (ThrusterRing != null)
                {
                    ThrusterRing.localRotation = Quaternion.Euler(0f, t * 180f, 0f);
                }
                break;
        }
    }
}

public class JuiceParticlePool : MonoBehaviour
{
    public static JuiceParticlePool Instance;

    public class PooledItem
    {
        public GameObject Go;
        public Transform Tr;
        public Renderer Ren;
        public Vector3 Vel;
        public Vector3 AngVel;
        public float Lifetime;
        public float MaxLifetime;
        public Vector3 InitialScale;
        public bool Shrink;
        public bool Active;
    }

    private List<PooledItem> m_Pool = new List<PooledItem>();
    private const int POOL_CAP = 260;

    private int GetActiveCap()
    {
        if (Aadish3DGame.Instance == null) return 260;
        if (Aadish3DGame.Instance.CurrentQualityTier == GraphicsQualityTier.Mobile60FPS) return 110;
        if (Aadish3DGame.Instance.CurrentQualityTier == GraphicsQualityTier.High) return 180;
        return 260;
    }

    void Awake()
    {
        Instance = this;
        InitPool();
    }

    void InitPool()
    {
        for (int i = 0; i < POOL_CAP; i++)
        {
            GameObject p = GameObject.CreatePrimitive(i % 2 == 0 ? PrimitiveType.Cube : PrimitiveType.Sphere);
            p.name = "JuicePart_" + i;
            p.transform.SetParent(transform, false);
            DestroyImmediate(p.GetComponent<Collider>());

            PooledItem item = new PooledItem
            {
                Go = p,
                Tr = p.transform,
                Ren = p.GetComponent<Renderer>(),
                Active = false
            };
            p.SetActive(false);
            m_Pool.Add(item);
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) dt = Time.unscaledDeltaTime;
        for (int i = 0; i < m_Pool.Count; i++)
        {
            PooledItem item = m_Pool[i];
            if (!item.Active) continue;

            item.Lifetime -= dt;
            if (item.Lifetime <= 0f)
            {
                item.Active = false;
                item.Go.SetActive(false);
                continue;
            }

            item.Vel.y -= 16f * dt;
            item.Tr.position += item.Vel * dt;
            item.Tr.Rotate(item.AngVel * dt, Space.Self);

            if (item.Shrink)
            {
                float t = Mathf.Clamp01(item.Lifetime / item.MaxLifetime);
                item.Tr.localScale = item.InitialScale * t;
            }
        }
    }

    public void SpawnJuiceSplashParticles(Vector3 pos, int count = 12)
    {
        Color[] cols = new Color[] {
            new Color(0.96f, 0.25f, 0.37f),
            new Color(0.98f, 0.70f, 0.52f),
            new Color(0.99f, 0.89f, 0.54f),
            new Color(1f, 1f, 1f, 0.9f)
        };
        SpawnBurst(pos, cols, count, 6.5f, 0.7f, 0.3f, false);
    }

    public void SpawnConfettiBurst(Vector3 pos, int count = 25)
    {
        Color[] cols = new Color[] {
            new Color(0.96f, 0.25f, 0.37f),
            new Color(0.23f, 0.51f, 0.96f),
            new Color(0.98f, 0.75f, 0.14f),
            new Color(0.13f, 0.77f, 0.37f),
            new Color(0.66f, 0.33f, 0.97f),
            new Color(1f, 1f, 1f)
        };
        SpawnBurst(pos, cols, count, 7.5f, 1.1f, 0.32f, true);
    }

    public void SpawnPuffCloud(Vector3 pos, int count = 8)
    {
        Color[] cols = new Color[] {
            new Color(1f, 1f, 1f, 0.85f),
            new Color(0.90f, 0.94f, 1f, 0.75f)
        };
        SpawnBurst(pos, cols, count, 3.5f, 0.6f, 0.45f, true);
    }

    public void SpawnSkinSparkle(Vector3 pos, int skin = 1)
    {
        Color[] cols = (skin == 1)
            ? new Color[] { new Color(1f, 0.85f, 0.2f), new Color(1f, 0.95f, 0.6f) }
            : new Color[] { new Color(0.3f, 0.8f, 1f), new Color(0.8f, 0.95f, 1f), new Color(1f, 1f, 1f) };
        SpawnBurst(pos, cols, 10, 4.0f, 0.75f, 0.25f, true);
    }

    public void SpawnDoubleJumpRing(Vector3 feetPos, int skin = 0)
    {
        int cap = GetActiveCap();
        // Expanding white cloud puff ring beneath feet
        Color[] puffCols = new Color[] {
            new Color(1f, 1f, 1f, 0.92f),
            new Color(0.90f, 0.95f, 1f, 0.85f)
        };
        int puffCount = 10;
        for (int i = 0; i < puffCount; i++)
        {
            PooledItem item = GetNextAvailable(cap);
            if (item == null) break;
            float ang = i * (360f / puffCount) * Mathf.Deg2Rad;
            Vector3 radial = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));

            Color c = puffCols[i % puffCols.Length];
            item.Ren.sharedMaterial = SafeMaterialFactory.GetCachedMaterial("ParticleMat_" + c.GetHashCode(), c);
            item.Tr.position = feetPos + radial * 0.35f + Vector3.up * 0.10f;
            item.InitialScale = Vector3.one * 0.38f;
            item.Tr.localScale = item.InitialScale;
            item.MaxLifetime = 0.45f;
            item.Lifetime = item.MaxLifetime;
            item.Shrink = true;
            item.Vel = radial * 4.8f + Vector3.down * 0.4f;
            item.AngVel = UnityEngine.Random.insideUnitSphere * 180f;
            item.Active = true;
            item.Go.SetActive(true);
        }

        // Skin-matched juice / sparkle droplets (Blue=Normal, Gold=Golden, Diamond Cyan=Bird)
        Color[] juiceCols;
        if (skin == 1)
            juiceCols = new Color[] { new Color(1f, 0.84f, 0.08f), new Color(1f, 0.94f, 0.45f) };
        else if (skin == 2)
            juiceCols = new Color[] { new Color(0.22f, 0.78f, 0.99f), new Color(0.92f, 0.98f, 1f) };
        else
            juiceCols = new Color[] { new Color(0.12f, 0.36f, 1f), new Color(0.22f, 0.74f, 0.97f) };

        int dropletCount = 8;
        for (int j = 0; j < dropletCount; j++)
        {
            PooledItem item = GetNextAvailable(cap);
            if (item == null) break;
            float ang = (j * (360f / dropletCount) + 22.5f) * Mathf.Deg2Rad;
            Vector3 radial = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));

            Color c = juiceCols[j % juiceCols.Length];
            item.Ren.sharedMaterial = SafeMaterialFactory.GetCachedMaterial("ParticleMat_" + c.GetHashCode(), c);
            item.Tr.position = feetPos + radial * 0.25f + Vector3.up * 0.15f;
            item.InitialScale = Vector3.one * 0.26f;
            item.Tr.localScale = item.InitialScale;
            item.MaxLifetime = 0.52f;
            item.Lifetime = item.MaxLifetime;
            item.Shrink = true;
            item.Vel = radial * 5.6f + Vector3.up * 1.2f;
            item.AngVel = UnityEngine.Random.insideUnitSphere * 300f;
            item.Active = true;
            item.Go.SetActive(true);
        }
    }

    public void SpawnBurst(Vector3 pos, Color[] colors, int count = 12, float speed = 5.5f, float lifetime = 0.8f, float size = 0.35f, bool isUpward = true)
    {
        if (colors == null || colors.Length == 0) colors = new Color[] { Color.white };
        int cap = GetActiveCap();
        for (int i = 0; i < count; i++)
        {
            PooledItem item = GetNextAvailable(cap);
            if (item == null) break;

            Color c = colors[i % colors.Length];
            Material mat = SafeMaterialFactory.GetCachedMaterial("ParticleMat_" + c.GetHashCode(), c);
            item.Ren.sharedMaterial = mat;

            item.Tr.position = pos + UnityEngine.Random.insideUnitSphere * 0.25f;
            item.InitialScale = Vector3.one * (size * UnityEngine.Random.Range(0.7f, 1.3f));
            item.Tr.localScale = item.InitialScale;
            item.MaxLifetime = lifetime * UnityEngine.Random.Range(0.75f, 1.25f);
            item.Lifetime = item.MaxLifetime;
            item.Shrink = true;

            Vector3 dir = UnityEngine.Random.insideUnitSphere;
            if (isUpward) dir = (dir + Vector3.up * 1.6f).normalized;
            else dir = dir.normalized;

            item.Vel = dir * (speed * UnityEngine.Random.Range(0.7f, 1.3f));
            item.AngVel = UnityEngine.Random.insideUnitSphere * 240f;

            item.Active = true;
            item.Go.SetActive(true);
        }
    }

    private PooledItem GetNextAvailable(int cap)
    {
        int lim = Mathf.Min(cap, m_Pool.Count);
        for (int i = 0; i < lim; i++)
        {
            if (!m_Pool[i].Active) return m_Pool[i];
        }
        return (lim > 0) ? m_Pool[0] : null;
    }
}
#endregion

// ===========================================================================
// SECTION 0 & 5: MAIN GAME MANAGER, GTA-6 LAND SCULPTOR, HUD & LEVEL LIFECYCLE
// ===========================================================================
#region 0. Core State, Constants & Safe Runtime Bootstrap
public class Aadish3DGame : MonoBehaviour
{
    public static Aadish3DGame Instance;

    public GraphicsQualityTier CurrentQualityTier = GraphicsQualityTier.UltraGTA6;
    public bool TouchControlsEnabled = false;
    public int TouchModeSetting = 0; // 0=Auto, 1=On, 2=Off

    public static Material GetSafeMaterial(Color col, string name = "Mat")
    {
        return SafeMaterialFactory.GetCachedMaterial(name, col);
    }

    public static Material GetSafeMaterial(string name, Color col, Texture2D tex = null, float gloss = 0.5f, float metal = 0f, Color? emission = null, bool transparent = false)
    {
        return SafeMaterialFactory.GetCachedMaterial(name, col, tex, gloss, metal, emission, transparent);
    }

    public Vector2 TouchMoveInput = Vector2.zero;

    public void TriggerScreenShake(float duration = 0.25f, float intensity = 0.35f)
    {
        if (CamCtrl != null) CamCtrl.Shake(duration, intensity);
    }

    public void SetStarCollected(int level)
    {
        ProgressionManager.SetStarCollected(level);
    }

    public void SpawnJuiceSplashParticles(Vector3 pos, int count = 12)
    {
        if (ParticlePool != null) ParticlePool.SpawnJuiceSplashParticles(pos, count);
    }

    public void SpawnConfettiBurst(Vector3 pos, int count = 25)
    {
        if (ParticlePool != null) ParticlePool.SpawnConfettiBurst(pos, count);
    }

    public void SpawnPuffCloud(Vector3 pos, int count = 8)
    {
        if (ParticlePool != null) ParticlePool.SpawnPuffCloud(pos, count);
    }

    public void SpawnSkinSparkle(Vector3 pos, int skin = 1)
    {
        if (ParticlePool != null) ParticlePool.SpawnSkinSparkle(pos, skin);
    }

    public enum PlatformKind { Static, Moving, Wafer, DonutSpring, BossArena }
    public enum EnemyType { BurgerPatrol, SodaCup, SundaeMonster, HotdogGuard, PizzaDrone }
    public enum GameState { MainMenu, WorldSelect, LevelSelect, Playing, Paused, Dialogue, EndingCelebration, HowToPlay, Settings }
    public GameState CurrentState = GameState.MainMenu;

    public int CurrentWorld = 1;
    public int CurrentLevel = 1;
    public int EquippedSkin = 0;
    public int UnlockedLevel = 1;
    public bool AllLevelsBeaten = false;
    public bool SoundEnabled = true;
    public float SoundVolume = 0.85f;

    public float LevelStartTime = 0f;
    public float LevelFinishZ = 62f;
    public Vector3 PlayerSpawnPoint = Vector3.zero;
    public Vector3 CheckpointPoint = Vector3.zero;
    public bool CheckpointActivated = false;
    public List<Transform> ActiveCheckpoints = new List<Transform>();

    // Player Health & Screen FX
    public int PlayerMaxHP = 3;
    public int PlayerHP = 3;
    private float m_DamageFlashAlpha = 0f;

    public bool InBossFight = false;
    public bool BossAlive = false;
    public bool BossDialogueDone = false;
    public BossController ActiveBoss;
    public int BossCurrentHP = 10;
    public int BossMaxHP = 10;
    public string BossName = "KING CHEESEBURG";

    // References
    public Camera MainCam;
    public CameraController CamCtrl;
    public Light KeyLight;
    public Light RimLight;
    public BerryDadController Player;
    public Transform StageRoot;
    public JuiceParticlePool ParticlePool;
    public LivingLandWindSystem WindSystem;
    public DynamicLightPool LightPool;
    public Transform SkyDomeTransform;
    public MeshFilter SkyDomeMeshFilter;

    public List<EnemyController> ActiveEnemies = new List<EnemyController>();
    public List<JuiceBullet> ActiveBullets = new List<JuiceBullet>();

    // Audio
    public AudioSource MusicSource;
    public AudioSource SFXSource;
    public enum SFXType { Jump, DoubleJump, FlapWhoosh, Shoot, Hit, Pop, StarDing, VoiceBlip, HurtFall, VictoryFanfare, UIHover, UILocked, WorldWhoosh, UnlockFanfare }
    private Dictionary<SFXType, AudioClip> m_SFXClips = new Dictionary<SFXType, AudioClip>();
    private AudioClip m_ChiptuneBGM;

    // UI Panels
    public Canvas MainCanvas;
    public GameObject PanelMainMenu;
    public GameObject PanelWorldSelect;
    public GameObject PanelLevelSelect;
    public GameObject PanelHUD;
    public GameObject PanelVictory;
    public GameObject PanelPause;
    public GameObject PanelHowToPlay;
    public GameObject PanelSettings;
    public GameObject PanelDialogue;
    public GameObject PanelEndingCelebration;
    public GameObject PanelTouchControls;

    // HUD Elements
    public Text HudLevelText;
    public Text HudStarText;
    public Text HudHealthText;
    public GameObject BossHealthContainer;
    public RectTransform BossHealthBarFill;
    public RectTransform BossChipDamageFill;
    public Image BossHealthFillImg;
    public Text BossHealthBarLabel;
    public Text BossPhaseBadgeText;
    public Image ScreenFXVignetteImg;
    public RectTransform OffscreenBossIndicator;
    private float m_BossChipRatio = 1f;

    // Level Distance Progress Bar
    public GameObject ProgressBarRoot;
    public Image ProgressBarFillImage;
    public Text ProgressBarReadoutText;
    public RectTransform ProgressBarAvatarPin;
    public Image ProgressBarAvatarPinImg;
    public RectTransform ProgressBarStarPin;
    public Image ProgressBarStarPinImg;
    public RectTransform ProgressBarMiniBerryPin;
    public Image ProgressBarMiniBerryPinImg;
    public List<RectTransform> ProgressBarCheckpointPins = new List<RectTransform>();
    public List<Image> ProgressBarCheckpointPinImgs = new List<Image>();

    // Victory Screen Elements
    public Text VictoryTitleText;
    public Text VictoryMiniBerryText;
    public Text VictoryTimeText;
    public GameObject VictoryNewRecordPill;
    public Image VictoryStarImage;
    public Text VictoryStarText;
    public GameObject VictoryUnlockBanner;
    public Text VictoryUnlockTitle;
    public Text VictoryUnlockSubtext;
    public Image VictoryUnlockIcon;
    public GameObject VictoryWorldBanner;
    public Text VictoryWorldTitle;
    public GameObject VictorySkinBanner;
    public Text VictorySkinTitle;

    // World Select Elements
    public Text WorldSelectCampaignTallyText;
    public Text WorldHeroTitle;
    public Text WorldHeroSubtitle;
    public GameObject WorldHeroActionBtn;
    public Text WorldHeroActionText;
    public Image WorldHeroActionIcon;
    public List<GameObject> WorldCards = new List<GameObject>();
    public int FocusedWorldIndex = 1;

    // Level Select Elements
    public Text LevelSelectWorldTitleText;
    public Text LevelSelectCampaignTallyText;
    public List<GameObject> LevelDeckCards = new List<GameObject>();

    // Settings Elements
    public List<Button> SkinButtons = new List<Button>();
    public List<Text> SkinButtonTexts = new List<Text>();
    private int m_ResetTapCount = 0;
    private Text m_ResetBtnText;
    private Text m_GraphicsBtnText;
    private Text m_TouchBtnText;

    // Dialogue Elements
    public Text DialogueSpeakerText;
    public Text DialogueBodyText;
    public GameObject DialogueNextIndicator;
    public struct DialogueLine
    {
        public string Speaker;
        public Color SpeakerColor;
        public string Text;
        public DialogueLine(string s, Color c, string t) { Speaker = s; SpeakerColor = c; Text = t; }
    }
    public Dictionary<int, List<DialogueLine>> LevelDialogues = new Dictionary<int, List<DialogueLine>>();
    public Dictionary<int, List<DialogueLine>> BossPreFightDialogues = new Dictionary<int, List<DialogueLine>>();
    public List<DialogueLine> CurrentConversation;
    public int CurrentDialogueIndex = 0;
    public Action OnDialogueComplete;

    public static readonly string[] BerryNames = new string[50] {
        "Pip", "Mochi", "Billy", "Lulu", "Nugget", "Sprout", "DJ Berry", "Pickles", "Waffles", "Binky",
        "Barnaby", "Twinkle", "Peanut", "Zippy", "Bubbles", "Gizmo", "Pancake", "Button", "Scooter", "Captain Bob",
        "Frosty", "Snowdrop", "Glacier", "Minty", "Chilly", "Slushie", "Yeti Jr.", "Blizzard", "Icicle", "Sherbet",
        "Sparky", "Sizzle", "Pepper", "Blaze", "Nacho", "Cinder", "Smokey", "Toast", "Ember", "Sir Chomps",
        "Cosmo", "Astro", "Nova", "Clover", "Radar", "Orbit", "Starlight", "Comet", "Galaxy", "Berry Prime"
    };

    public static readonly string[] BerryHexColors = new string[50] {
        "#38BDF8", "#F472B6", "#FBBF24", "#A78BFA", "#34D399", "#4ADE80", "#EC4899", "#A3E635", "#F59E0B", "#F43F5E",
        "#60A5FA", "#E879F9", "#FB923C", "#2DD4BF", "#818CF8", "#C084FC", "#FCD34D", "#4BB543", "#38BDF8", "#F59E0B",
        "#BAE6FD", "#E0F2FE", "#7DD3FC", "#6EE7B7", "#93C5FD", "#A5F3FC", "#C7D2FE", "#DDD6FE", "#E9D5FF", "#FBCFE8",
        "#F87171", "#FB923C", "#FBBF24", "#EF4444", "#F97316", "#DC2626", "#EA580C", "#D97706", "#B45309", "#991B1B",
        "#818CF8", "#A78BFA", "#C084FC", "#E879F9", "#38BDF8", "#22D3EE", "#FACC15", "#F472B6", "#4ADE80", "#FFD700"
    };

    public GameObject MainMenuDioramaRoot;
    public int CurrentDioramaWorldIndex = -1;
    public bool CurrentDioramaLockedState = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void RuntimeBootstrap()
    {
        GameObject existing = GameObject.Find("BerryBounce3D_Manager");
        if (existing != null) DestroyImmediate(existing);

        GameObject gameManager = new GameObject("BerryBounce3D_Manager");
        Aadish3DGame instance = gameManager.AddComponent<Aadish3DGame>();
        instance.InitializeGame();
    }

    void Awake()
    {
        Instance = this;
    }

    public void InitializeGame()
    {
        LoadProgression();
        CinematicAtmosphereEngine.TrySetupURPPostProcessing();
        SetupAudio();
        InitializeDialogues();
        SetupSceneLightingAndCamera();
        ApplyGraphicsQualityTier();
        SetupUI();

        GameObject poolGo = new GameObject("JuiceParticlePool");
        ParticlePool = poolGo.AddComponent<JuiceParticlePool>();

        GameObject windGo = new GameObject("LivingLandWindSystem");
        WindSystem = windGo.AddComponent<LivingLandWindSystem>();

        GameObject lightPoolGo = new GameObject("DynamicLightPool");
        LightPool = lightPoolGo.AddComponent<DynamicLightPool>();

        GoToMainMenu();
    }

    void SetupSceneLightingAndCamera()
    {
        GameObject camGo = GameObject.FindWithTag("MainCamera");
        if (camGo == null) camGo = new GameObject("MainCamera");
        MainCam = camGo.GetComponent<Camera>();
        if (MainCam == null) MainCam = camGo.AddComponent<Camera>();
        camGo.tag = "MainCamera";
        MainCam.clearFlags = CameraClearFlags.SolidColor;
        MainCam.backgroundColor = HexColor("#5BC0FF");
        MainCam.fieldOfView = 60f;
        MainCam.nearClipPlane = 0.3f;
        MainCam.farClipPlane = 1200f;

        CamCtrl = camGo.GetComponent<CameraController>();
        if (CamCtrl == null) CamCtrl = camGo.AddComponent<CameraController>();
        CamCtrl.SetMenuCameraState(MenuCameraState.MainMenu);

        EnsureSingleAudioListener(MainCam.GetComponent<AudioListener>());

        GameObject lightGo = GameObject.Find("DirectionalLight");
        if (lightGo == null) lightGo = new GameObject("DirectionalLight");
        KeyLight = lightGo.GetComponent<Light>();
        if (KeyLight == null) KeyLight = lightGo.AddComponent<Light>();
        KeyLight.type = LightType.Directional;
        KeyLight.color = new Color(1f, 0.96f, 0.88f);
        KeyLight.intensity = 1.38f;
        KeyLight.shadows = LightShadows.Soft;
        KeyLight.shadowStrength = 0.72f;
        KeyLight.shadowBias = 0.03f;
        KeyLight.shadowNormalBias = 0.4f;
        lightGo.transform.rotation = Quaternion.Euler(44f, -35f, 0f);

        GameObject rimGo = GameObject.Find("RimLight");
        if (rimGo == null) rimGo = new GameObject("RimLight");
        RimLight = rimGo.GetComponent<Light>();
        if (RimLight == null) RimLight = rimGo.AddComponent<Light>();
        RimLight.type = LightType.Directional;
        RimLight.color = new Color(0.85f, 0.72f, 1f);
        RimLight.intensity = 0.38f;
        RimLight.shadows = LightShadows.None;
        rimGo.transform.rotation = Quaternion.Euler(25f, 145f, 0f);

        // Multi-stop Vertex Color Sky Dome
        GameObject skyGo = new GameObject("CinematicSkyDome");
        SkyDomeTransform = skyGo.transform;
        SkyDomeMeshFilter = skyGo.AddComponent<MeshFilter>();
        SkyDomeMeshFilter.sharedMesh = CinematicAtmosphereEngine.BuildGradientSkyDomeMesh(1);
        MeshRenderer skyMr = skyGo.AddComponent<MeshRenderer>();
        skyMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        skyMr.receiveShadows = false;
        skyMr.sharedMaterial = SafeMaterialFactory.GetCachedVertexAlphaMaterial("SkyDomeMat", Color.white, null, 950);
    }

    public void ApplyGraphicsQualityTier()
    {
        try
        {
            if (CurrentQualityTier == GraphicsQualityTier.UltraGTA6)
            {
                QualitySettings.pixelLightCount = 8;
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.shadowResolution = ShadowResolution.High;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                if (KeyLight != null) KeyLight.shadows = LightShadows.Soft;
            }
            else if (CurrentQualityTier == GraphicsQualityTier.High)
            {
                QualitySettings.pixelLightCount = 4;
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.shadowResolution = ShadowResolution.Medium;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                if (KeyLight != null) KeyLight.shadows = LightShadows.Soft;
            }
            else
            {
                QualitySettings.pixelLightCount = 2;
                QualitySettings.shadows = ShadowQuality.HardOnly;
                QualitySettings.shadowResolution = ShadowResolution.Low;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                if (KeyLight != null) KeyLight.shadows = LightShadows.Hard;
            }
        }
        catch { }

        if (m_GraphicsBtnText != null)
        {
            string label = (CurrentQualityTier == GraphicsQualityTier.UltraGTA6) ? "GRAPHICS: ULTRA (GTA-6)" :
                           (CurrentQualityTier == GraphicsQualityTier.High) ? "GRAPHICS: HIGH" : "GRAPHICS: 60 FPS MOBILE";
            m_GraphicsBtnText.text = label;
        }
    }

    public void CycleGraphicsQualityTier()
    {
        int next = ((int)CurrentQualityTier + 1) % 3;
        CurrentQualityTier = (GraphicsQualityTier)next;
        SaveProgression();
        ApplyGraphicsQualityTier();
        if (CurrentState != GameState.Playing && CurrentDioramaWorldIndex > 0)
        {
            int curW = CurrentDioramaWorldIndex;
            bool curL = CurrentDioramaLockedState;
            CurrentDioramaWorldIndex = -1;
            ShowMenuDioramaForWorld(curW, curL);
        }
        UIToastBanner.Show("Graphics Quality: " + ((CurrentQualityTier == GraphicsQualityTier.UltraGTA6) ? "ULTRA (GTA-6)" : ((CurrentQualityTier == GraphicsQualityTier.High) ? "HIGH" : "60 FPS MOBILE")), HexColor("#38BDF8"));
    }

    public void CycleTouchControlsSetting()
    {
        TouchModeSetting = (TouchModeSetting + 1) % 3;
        UpdateTouchControlsVisibility();
        SaveProgression();
        string txt = (TouchModeSetting == 0) ? "TOUCH D-PAD: AUTO" : (TouchModeSetting == 1 ? "TOUCH D-PAD: ON" : "TOUCH D-PAD: OFF");
        if (m_TouchBtnText != null) m_TouchBtnText.text = txt;
        UIToastBanner.Show(txt, HexColor("#10B981"));
    }

    private void UpdateTouchControlsVisibility()
    {
        bool autoTouch = Application.isMobilePlatform;
        TouchControlsEnabled = (TouchModeSetting == 1) || (TouchModeSetting == 0 && autoTouch);
        if (PanelTouchControls != null)
        {
            PanelTouchControls.SetActive(TouchControlsEnabled && CurrentState == GameState.Playing);
        }
    }

    public static void EnsureSingleAudioListener(AudioListener keepListener = null)
    {
#if UNITY_6000_0_OR_NEWER
        AudioListener[] listeners = FindObjectsByType<AudioListener>();
#elif UNITY_2023_1_OR_NEWER
        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
#else
        AudioListener[] listeners = FindObjectsOfType<AudioListener>();
#endif
        bool kept = false;
        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i] == keepListener && !kept) { kept = true; continue; }
            if (!kept) { kept = true; continue; }
            Destroy(listeners[i]);
        }
        if (!kept && Camera.main != null)
        {
            Camera.main.gameObject.AddComponent<AudioListener>();
        }
    }

    public void LoadProgression()
    {
        UnlockedLevel = ProgressionManager.GetHighestUnlockedLevel();
        EquippedSkin = PlayerPrefs.GetInt("BerryBounce_EquippedSkin", 0);
        AllLevelsBeaten = PlayerPrefs.GetInt("BerryBounce_AllLevelsBeaten", 0) == 1;
        SoundEnabled = PlayerPrefs.GetInt("BerryBounce_SoundEnabled", 1) == 1;
        SoundVolume = PlayerPrefs.GetFloat("BerryBounce_SoundVolume", 0.85f);
        CurrentQualityTier = (GraphicsQualityTier)Mathf.Clamp(PlayerPrefs.GetInt("BerryBounce_GraphicsTier", Application.isMobilePlatform ? 1 : 0), 0, 2);
        TouchModeSetting = Mathf.Clamp(PlayerPrefs.GetInt("BerryBounce_TouchMode", 0), 0, 2);
    }

    public void SaveProgression()
    {
        PlayerPrefs.SetInt("BerryBounce_EquippedSkin", EquippedSkin);
        PlayerPrefs.SetInt("BerryBounce_SoundEnabled", SoundEnabled ? 1 : 0);
        PlayerPrefs.SetFloat("BerryBounce_SoundVolume", SoundVolume);
        PlayerPrefs.SetInt("BerryBounce_GraphicsTier", (int)CurrentQualityTier);
        PlayerPrefs.SetInt("BerryBounce_TouchMode", TouchModeSetting);
        PlayerPrefs.Save();
    }
#endregion

// ===========================================================================
// SECTION 9: AUDIO SYNTHESIZER & SFX
// ===========================================================================
#region 9. Pooled Visual Juice Particles & Procedural Chiptune Audio
    void SetupAudio()
    {
        GameObject audioGo = new GameObject("AudioController");
        audioGo.transform.SetParent(transform);
        MusicSource = audioGo.AddComponent<AudioSource>();
        MusicSource.loop = true;
        MusicSource.volume = SoundVolume * 0.45f;
        MusicSource.mute = !SoundEnabled;

        SFXSource = audioGo.AddComponent<AudioSource>();
        SFXSource.loop = false;
        SFXSource.volume = SoundVolume;
        SFXSource.mute = !SoundEnabled;

        GenerateSFXClips();
        GenerateChiptuneMusic();
    }

    void GenerateSFXClips()
    {
        m_SFXClips[SFXType.Jump] = CreateSineSweepClip(240f, 580f, 0.15f);
        m_SFXClips[SFXType.DoubleJump] = CreateDoubleJumpBoingWhooshClip();
        m_SFXClips[SFXType.FlapWhoosh] = CreateNoiseWhooshClip(0.20f);
        m_SFXClips[SFXType.Shoot] = CreateSineSweepClip(780f, 320f, 0.10f);
        m_SFXClips[SFXType.Hit] = CreateNoiseBlipClip(0.12f);
        m_SFXClips[SFXType.Pop] = CreateSineSweepClip(600f, 950f, 0.08f);
        m_SFXClips[SFXType.StarDing] = CreateArpeggioClip(new float[] { 523.25f, 659.25f, 783.99f, 1046.50f }, 0.35f);
        m_SFXClips[SFXType.VoiceBlip] = CreateSineSweepClip(380f, 480f, 0.06f);
        m_SFXClips[SFXType.HurtFall] = CreateSineSweepClip(420f, 140f, 0.35f);
        m_SFXClips[SFXType.VictoryFanfare] = CreateArpeggioClip(new float[] { 440f, 554.37f, 659.25f, 880f, 1108.73f }, 0.65f);
        m_SFXClips[SFXType.UIHover] = CreateSineSweepClip(680f, 820f, 0.04f);
        m_SFXClips[SFXType.UILocked] = CreateSineSweepClip(180f, 110f, 0.22f);
        m_SFXClips[SFXType.WorldWhoosh] = CreateNoiseWhooshClip(0.32f);
        m_SFXClips[SFXType.UnlockFanfare] = CreateArpeggioClip(new float[] { 587.33f, 739.99f, 880f, 1174.66f }, 0.55f);
    }

    public void PlaySFX(SFXType type)
    {
        if (!SoundEnabled || SFXSource == null) return;
        AudioClip clip;
        if (m_SFXClips.TryGetValue(type, out clip))
        {
            SFXSource.PlayOneShot(clip, SoundVolume);
        }
    }

    public void PlayBGM(float pitch = 1.0f)
    {
        if (MusicSource == null) return;
        MusicSource.pitch = pitch;
        if (!SoundEnabled) return;
        if (m_ChiptuneBGM != null && !MusicSource.isPlaying)
        {
            MusicSource.clip = m_ChiptuneBGM;
            MusicSource.Play();
        }
    }

    public void StopBGM()
    {
        if (MusicSource != null) MusicSource.Stop();
    }

    private AudioClip CreateSineSweepClip(float startFreq, float endFreq, float duration)
    {
        int sampleRate = 22050;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            float freq = Mathf.Lerp(startFreq, endFreq, t);
            float env = 1f - t;
            samples[i] = Mathf.Sin(2f * Mathf.PI * freq * (i / (float)sampleRate)) * env * 0.55f;
        }
        AudioClip clip = AudioClip.Create("SFX_SineSweep", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private AudioClip CreateDoubleJumpBoingWhooshClip()
    {
        int sampleRate = 22050;
        float duration = 0.21f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float phase = 0f;
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            // Higher-pitched elastic Boing sweep with playful spring bounce + airy Whoosh
            float springWobble = Mathf.Sin(t * Mathf.PI * 5f) * 65f * (1f - t);
            float freq = Mathf.Lerp(410f, 960f, Mathf.Sqrt(t)) + springWobble;
            phase += (2f * Mathf.PI * freq) / sampleRate;
            float envBoing = Mathf.Sin(t * Mathf.PI) * (1f - t * 0.35f);
            float boing = (Mathf.Sin(phase) + Mathf.Sin(phase * 2f) * 0.25f) * envBoing * 0.48f;
            float envWhoosh = Mathf.Sin(Mathf.Clamp01(t * 1.25f) * Mathf.PI) * (1f - t);
            float whoosh = (UnityEngine.Random.value * 2f - 1f) * envWhoosh * 0.22f;
            samples[i] = Mathf.Clamp(boing + whoosh, -0.95f, 0.95f);
        }
        AudioClip clip = AudioClip.Create("SFX_DoubleJumpBoingWhoosh", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private AudioClip CreateNoiseWhooshClip(float duration)
    {
        int sampleRate = 22050;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            float env = Mathf.Sin(t * Mathf.PI);
            samples[i] = (UnityEngine.Random.value * 2f - 1f) * env * 0.45f;
        }
        AudioClip clip = AudioClip.Create("SFX_Whoosh", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private AudioClip CreateNoiseBlipClip(float duration)
    {
        int sampleRate = 22050;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            samples[i] = (UnityEngine.Random.value * 2f - 1f) * (1f - t) * 0.65f;
        }
        AudioClip clip = AudioClip.Create("SFX_Hit", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private AudioClip CreateArpeggioClip(float[] freqs, float duration)
    {
        int sampleRate = 22050;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        int noteCount = freqs.Length;
        int samplesPerNote = sampleCount / noteCount;

        for (int i = 0; i < sampleCount; i++)
        {
            int noteIdx = Mathf.Clamp(i / samplesPerNote, 0, noteCount - 1);
            float freq = freqs[noteIdx];
            float noteT = (float)(i % samplesPerNote) / samplesPerNote;
            float env = 1f - (noteT * 0.7f);
            samples[i] = Mathf.Sin(2f * Mathf.PI * freq * (i / (float)sampleRate)) * env * 0.55f;
        }
        AudioClip clip = AudioClip.Create("SFX_Arp", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void GenerateChiptuneMusic()
    {
        int sampleRate = 22050;
        float duration = 12.0f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float[] melody = new float[] { 261.63f, 329.63f, 392.00f, 523.25f, 440.00f, 349.23f, 392.00f, 329.63f };
        int noteLen = sampleCount / (melody.Length * 3);

        for (int i = 0; i < sampleCount; i++)
        {
            int note = (i / noteLen) % melody.Length;
            float freq = melody[note];
            float wave = (Mathf.Sin(2f * Mathf.PI * freq * (i / (float)sampleRate)) > 0f) ? 0.22f : -0.22f;
            float bass = (Mathf.Sin(2f * Mathf.PI * (freq * 0.5f) * (i / (float)sampleRate)) > 0f) ? 0.15f : -0.15f;
            samples[i] = wave + bass;
        }
        m_ChiptuneBGM = AudioClip.Create("BGM_Chiptune", sampleCount, 1, sampleRate, false);
        m_ChiptuneBGM.SetData(samples, 0);
    }
#endregion

// ===========================================================================
// SECTION 6: 50 DIALOGUES DATABASE
// ===========================================================================
void InitializeDialogues()
    {
        LevelDialogues[1] = new List<DialogueLine>() {
            new DialogueLine("Pip", HexColor("#38BDF8"), "Dad! You found me! I was chasing a very fast bumblebee!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Pip, you ran across three floating lettuce islands for a bug?!"),
            new DialogueLine("Pip", HexColor("#38BDF8"), "It looked like it knew where the secret cookies were hidden!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "No more bee chases. It is nap time, little berry."),
        };
        LevelDialogues[2] = new List<DialogueLine>() {
            new DialogueLine("Mochi", HexColor("#F472B6"), "Daddy! Look, I am balancing on this giant tomato!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Mochi, get down before you turn into ketchup!"),
            new DialogueLine("Mochi", HexColor("#F472B6"), "Aww, but ketchup is delicious with fries!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "You are a raspberry, not a condiment! Let us go home."),
        };
        LevelDialogues[3] = new List<DialogueLine>() {
            new DialogueLine("Billy", HexColor("#FBBF24"), "Dad! I was trying to climb into that cloud up there!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Clouds are made of water vapor, Billy, not cotton candy!"),
            new DialogueLine("Billy", HexColor("#FBBF24"), "Wait... so I cannot take a bite out of it?!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Absolutely not. Now take my hand before the wind blows you away."),
        };
        LevelDialogues[4] = new List<DialogueLine>() {
            new DialogueLine("Lulu", HexColor("#A78BFA"), "Papa! Look at this patrolling cheeseburger! Can I pet him?"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Lulu, that cheeseburger has angry eyebrows and pickles!"),
            new DialogueLine("Lulu", HexColor("#A78BFA"), "He just wants a tummy rub, Dad!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "He wants to squish you! Good thing I have juice bullets."),
        };
        LevelDialogues[5] = new List<DialogueLine>() {
            new DialogueLine("Nugget", HexColor("#34D399"), "Dad! I bounced on that pink donut spring thirty times!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "My heart almost stopped watching you fly fifty feet in the air!"),
            new DialogueLine("Nugget", HexColor("#34D399"), "Wheee! Let us do a synchronized double bounce together!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "My back hurts just looking at that spring. Bedtime for you!"),
        };
        LevelDialogues[6] = new List<DialogueLine>() {
            new DialogueLine("Sprout", HexColor("#4ADE80"), "Daddy! I am hiding from bath time! You can never find me!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Sprout, I am literally standing right in front of you."),
            new DialogueLine("Sprout", HexColor("#4ADE80"), "Darn it! My leafy camouflage failed again!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "You smell like salad dressing. Bath tub, right now."),
        };
        LevelDialogues[7] = new List<DialogueLine>() {
            new DialogueLine("DJ Berry", HexColor("#EC4899"), "Yo Dad! Hear that beat? The platform movements are totally in 4/4 time!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "I do not care about the rhythm, son, the platform is over a bottomless pit!"),
            new DialogueLine("DJ Berry", HexColor("#EC4899"), "Drop the bass, Papa Berry!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "I am dropping you straight into your crib."),
        };
        LevelDialogues[8] = new List<DialogueLine>() {
            new DialogueLine("Pickles", HexColor("#10B981"), "Dad! I challenged a patrolling cucumber to a staring contest!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Pickles, cucumbers do not even have eyelids!"),
            new DialogueLine("Pickles", HexColor("#10B981"), "I know! That is why I have been blinking for forty minutes!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Come here, you goofy little berry."),
        };
        LevelDialogues[9] = new List<DialogueLine>() {
            new DialogueLine("Waffles", HexColor("#F59E0B"), "Daddy! I found the checkpoint flag! I waved it like a royal banner!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Good job, Waffles! You actually stayed near the flag for once!"),
            new DialogueLine("Waffles", HexColor("#F59E0B"), "Yup! And then I heard a giant stomping noise ahead..."),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Stay behind me. King Cheeseburg is up next!"),
        };
        LevelDialogues[10] = new List<DialogueLine>() {
            new DialogueLine("Binky", HexColor("#60A5FA"), "DAD! You totally squashed that giant cheeseburger king!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Binky! Are you hurt? Did that greasy monarch scare you?"),
            new DialogueLine("Binky", HexColor("#60A5FA"), "Nope! I threw sesame seeds at his crown while you shot him!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "That is my brave berry! One world down, four to go!"),
        };
        LevelDialogues[11] = new List<DialogueLine>() {
            new DialogueLine("Gizmo", HexColor("#F97316"), "Dad! Look at this bubbling fry oil! Can we go swimming?"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "NO! That oil is 375 degrees! You will turn into berry jam!"),
            new DialogueLine("Gizmo", HexColor("#F97316"), "Warm berry jam on toast sounds kinda cozy though..."),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Not when it is YOU! Off the dock, immediately!"),
        };
        LevelDialogues[12] = new List<DialogueLine>() {
            new DialogueLine("Pebble", HexColor("#EAB308"), "Daddy, can I have five berry coins for the vending machine?"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Pebble, there are no vending machines in Deep Fry Docks!"),
            new DialogueLine("Pebble", HexColor("#EAB308"), "Then what did I just feed five shiny buttons into?!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "That was a hungry mechanical conveyor belt! Step away!"),
        };
        LevelDialogues[13] = new List<DialogueLine>() {
            new DialogueLine("Noodle", HexColor("#84CC16"), "Papa! Look how bendy these wooden planks are!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Noodle, stop bouncing on the edge, the wood is rotten!"),
            new DialogueLine("Noodle", HexColor("#84CC16"), "Wheee! I am flexible like a limp spaghetti noodle!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Well I am stiff as a broom handle from all this stress. Come here!"),
        };
        LevelDialogues[14] = new List<DialogueLine>() {
            new DialogueLine("Scoot", HexColor("#06B6D4"), "Dad! I raced three crispy french fries across the bridge!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "And did you win, Scoot?"),
            new DialogueLine("Scoot", HexColor("#06B6D4"), "I totally dusted them! But then they got salty and chased me!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Literally salty. That is why I blasted them with juice."),
        };
        LevelDialogues[15] = new List<DialogueLine>() {
            new DialogueLine("Pipsqueak", HexColor("#FB7185"), "Daddy! I am so tiny that the wind almost carried me across the ocean!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "That is why you are supposed to hold my hand, Pipsqueak!"),
            new DialogueLine("Pipsqueak", HexColor("#FB7185"), "But flying like a dandelion seed was so much fun!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Into my pocket you go. No more accidental aeronautics."),
        };
        LevelDialogues[16] = new List<DialogueLine>() {
            new DialogueLine("Barnaby", HexColor("#8B5CF6"), "Dad! Did you know French fries are actually from Belgium?"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Barnaby, this is hardly the time for potato history lectures!"),
            new DialogueLine("Barnaby", HexColor("#8B5CF6"), "Knowledge is power, Father. Even on a slippery grease dock!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Well, right now knowledge is getting you home for supper."),
        };
        LevelDialogues[17] = new List<DialogueLine>() {
            new DialogueLine("Tater", HexColor("#FBBF24"), "Daddy! Can we adopt that walking french fry as a family pet?"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Tater, he tried to poke my eye out with his pointy potato tip!"),
            new DialogueLine("Tater", HexColor("#FBBF24"), "We can train him to do backflips in the backyard!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "We have fifty kids already. No potato pets allowed."),
        };
        LevelDialogues[18] = new List<DialogueLine>() {
            new DialogueLine("Penny", HexColor("#EC4899"), "Dad! I found a shiny golden star spinning right above the oil!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Did you grab it safely, Penny?"),
            new DialogueLine("Penny", HexColor("#EC4899"), "I left it for you because you have the super heroic double jump!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Flattery will get you an extra bedtime story tonight. Let us go!"),
        };
        LevelDialogues[19] = new List<DialogueLine>() {
            new DialogueLine("Zippy", HexColor("#14B8A6"), "Dad! I saw a giant fry carton wearing an admiral hat!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "That would be Captain Crispy Fry. He runs these docks."),
            new DialogueLine("Zippy", HexColor("#14B8A6"), "He told me he makes the crunchiest snacks in the seven seas!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "He is about to find out how soggy fries get against berry juice!"),
        };
        LevelDialogues[20] = new List<DialogueLine>() {
            new DialogueLine("Cosmo", HexColor("#6366F1"), "DADDY! You sank Captain Crispy Fry!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Cosmo! He had too much grease in his sails!"),
            new DialogueLine("Cosmo", HexColor("#6366F1"), "He dropped his pirate hat! Can I keep it, Dad?"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Wear it proudly, kiddo. Next stop: Sundae Summit!"),
        };
        LevelDialogues[21] = new List<DialogueLine>() {
            new DialogueLine("Boba", HexColor("#3B82F6"), "Brrr! Daddy, my little berry toes are completely frozen!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "That is because you are standing barefoot on a glacier, Boba!"),
            new DialogueLine("Boba", HexColor("#3B82F6"), "Can we build a campfire out of waffle cookies?"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Cookies don't burn, they melt into crumbs. Into my coat, hurry!"),
        };
        LevelDialogues[22] = new List<DialogueLine>() {
            new DialogueLine("Fritter", HexColor("#FB923C"), "Dad! Watch my sick ice skating tricks!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Fritter, you just slipped on your rear end and slid thirty yards!"),
            new DialogueLine("Fritter", HexColor("#FB923C"), "I call that maneuver 'The Grumpy Dad Glide'!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Very funny. Now stand up before you get brain freeze down below."),
        };
        LevelDialogues[23] = new List<DialogueLine>() {
            new DialogueLine("Jellybean", HexColor("#D946EF"), "Papa! Look at these crumbling wafer platforms!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Jellybean, why did you step on all three of them?!"),
            new DialogueLine("Jellybean", HexColor("#D946EF"), "I love the crunchy sound when they snap in half!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "You are giving your poor father gray raspberry bumps."),
        };
        LevelDialogues[24] = new List<DialogueLine>() {
            new DialogueLine("Twiggy", HexColor("#10B981"), "Daddy! I licked the checkpoint flag pole!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Twiggy! Tell me your tongue is NOT stuck to the metal!"),
            new DialogueLine("Twiggy", HexColor("#10B981"), "Mphhh... ith shtuck thuper tight, Dada..."),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Hold still while I breathe warm blueberry air on it. One, two, three!"),
        };
        LevelDialogues[25] = new List<DialogueLine>() {
            new DialogueLine("Buttons", HexColor("#0EA5E9"), "Dad! I made a snowman that looks exactly like you!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Why does it have a scowl and angry leafy eyebrows?"),
            new DialogueLine("Buttons", HexColor("#0EA5E9"), "Because that is your signature Dad look! It is iconic!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "I do not scowl... okay fine, maybe a little. Let us move on."),
        };
        LevelDialogues[26] = new List<DialogueLine>() {
            new DialogueLine("Dumpling", HexColor("#F43F5E"), "Daddy, is the ice cream on the summit strawberry or vanilla?"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Dumpling, it is frozen snow and frost, not dessert!"),
            new DialogueLine("Dumpling", HexColor("#F43F5E"), "Well, I ate some anyway, and now my forehead hurts!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Classic brain freeze. Press your tongue to the roof of your mouth."),
        };
        LevelDialogues[27] = new List<DialogueLine>() {
            new DialogueLine("Snickers", HexColor("#E11D48"), "Dad! What do you call a funny snowman?"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "I am dodging icicles here, Snickers, please don't..."),
            new DialogueLine("Snickers", HexColor("#E11D48"), "A snow-FUNNY! Get it?! Ahahaha!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "That was terrible. But at least you are safe. Come on."),
        };
        LevelDialogues[28] = new List<DialogueLine>() {
            new DialogueLine("Buster", HexColor("#84CC16"), "Dad! I challenged a yeti to an arm wrestling match!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Buster, your arms are one inch long!"),
            new DialogueLine("Buster", HexColor("#84CC16"), "Yeah, but I have incredible berry determination!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Let us use that determination to get to the next level."),
        };
        LevelDialogues[29] = new List<DialogueLine>() {
            new DialogueLine("Chewy", HexColor("#F59E0B"), "Daddy, the peak of this mountain looks like three giant ice cream scoops!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "That is the Frosty Cone Monster, Chewy. He is not a dessert."),
            new DialogueLine("Chewy", HexColor("#F59E0B"), "He looked really mad when I asked for rainbow sprinkles..."),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Stay behind me. Time to defrost this monster with juice bullets!"),
        };
        LevelDialogues[30] = new List<DialogueLine>() {
            new DialogueLine("Bingo", HexColor("#4F46E5"), "BINGO! You melted the giant ice cream cone!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Bingo! You didn't get buried in strawberry scoops, did you?"),
            new DialogueLine("Bingo", HexColor("#4F46E5"), "Nope! I surfed down the vanilla avalanche on a wafer board!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Thirty kids rescued! Only twenty more to find!"),
        };
        LevelDialogues[31] = new List<DialogueLine>() {
            new DialogueLine("Puff", HexColor("#EF4444"), "Whew! Daddy, it is super toasty warm in this factory!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "That is liquid magma flowing underneath our feet, Puff!"),
            new DialogueLine("Puff", HexColor("#EF4444"), "I brought marshmallows! We can roast them on sticks!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "No roasting marshmallows over industrial factory lava!"),
        };
        LevelDialogues[32] = new List<DialogueLine>() {
            new DialogueLine("Ziggy", HexColor("#F97316"), "Dad! These metal grates are making clanky robot sounds!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Ziggy, keep moving, those grates heat up every few seconds!"),
            new DialogueLine("Ziggy", HexColor("#F97316"), "Hot potato, hot potato! My feet are doing the salsa!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Jump onto the moving platform with me, quickly!"),
        };
        LevelDialogues[33] = new List<DialogueLine>() {
            new DialogueLine("Biscuit", HexColor("#D97706"), "Daddy, did you clean your room before coming here?"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Excuse me?! I am the parent here, Biscuit!"),
            new DialogueLine("Biscuit", HexColor("#D97706"), "Mom told me you left your sock on the coffee table!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "That was a tactical resting spot for my sock. Let us move."),
        };
        LevelDialogues[34] = new List<DialogueLine>() {
            new DialogueLine("Marshmallow", HexColor("#FB7185"), "Dad, why are the fast food enemies so cranky?"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Because they have been grilling in this factory for ten years straight!"),
            new DialogueLine("Marshmallow", HexColor("#FB7185"), "They should try taking a nice bubble bath like us!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Water makes buns soggy, sweetie. Now hop into my backpack."),
        };
        LevelDialogues[35] = new List<DialogueLine>() {
            new DialogueLine("Waldo", HexColor("#DC2626"), "Dad! Guess where I am hiding!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Waldo, you are wearing bright red stripes inside a dark metal factory!"),
            new DialogueLine("Waldo", HexColor("#DC2626"), "Does that mean I blend in with the fire?"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "It means I spotted you from fifty yards away. Come here!"),
        };
        LevelDialogues[36] = new List<DialogueLine>() {
            new DialogueLine("Dot", HexColor("#F43F5E"), "Daddy! Watch out for those spinning spike poles!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "I saw them, Dot! My timing on these jumps is immaculate!"),
            new DialogueLine("Dot", HexColor("#F43F5E"), "You did bump your leaf hat on the ceiling though!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "That was a planned aerodynamic header. Nothing to worry about."),
        };
        LevelDialogues[37] = new List<DialogueLine>() {
            new DialogueLine("Squirt", HexColor("#0284C7"), "Dad! Can you teach me how to shoot juice bullets like you?"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "When you are older and finish all your broccoli, Squirt."),
            new DialogueLine("Squirt", HexColor("#0284C7"), "Pew! Pew! Pop! Look, I am blasting imaginary hotdogs!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Great form, kiddo. Just don't aim at your brother."),
        };
        LevelDialogues[38] = new List<DialogueLine>() {
            new DialogueLine("Peaches", HexColor("#FB923C"), "Daddy, why does the factory smell like smoky mustard?"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Because Sir Sizzle Hotdog is patrolling nearby!"),
            new DialogueLine("Peaches", HexColor("#FB923C"), "Ooh, I love mustard! But he shoots angry fireballs!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Which is why we keep our distance and jump over him!"),
        };
        LevelDialogues[39] = new List<DialogueLine>() {
            new DialogueLine("Crumb", HexColor("#CA8A04"), "Dad! Look at this giant conveyor belt! It goes so fast!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Run against the direction, Crumb, do not let it push you into the lava!"),
            new DialogueLine("Crumb", HexColor("#CA8A04"), "I feel like a little berry treadmill champion!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Grab my leaf hat! Pull yourself up, hurry!"),
        };
        LevelDialogues[40] = new List<DialogueLine>() {
            new DialogueLine("Banjo", HexColor("#7C3AED"), "YEE-HAW! Daddy extinguished Sir Sizzle Hotdog!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Banjo! That spicy sausage threw everything he had at us!"),
            new DialogueLine("Banjo", HexColor("#7C3AED"), "His mustard armor was no match for your royal berry juice!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Forty kids safe! Only the final world remains: Cosmic Drive-Thru!"),
        };
        LevelDialogues[41] = new List<DialogueLine>() {
            new DialogueLine("Tiki", HexColor("#06B6D4"), "Whoaaa! Dad! Look at me! I am floating like an astronaut!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Low gravity is wild, Tiki! Look how high we can jump!"),
            new DialogueLine("Tiki", HexColor("#06B6D4"), "I did four backflips and did not even scrape my knee!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Just be careful where you land, space platforms are slippery!"),
        };
        LevelDialogues[42] = new List<DialogueLine>() {
            new DialogueLine("Echo", HexColor("#8B5CF6"), "Dad... Dad... Dad... (Echo... Echo... Echo...)"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Echo, sound waves do not travel through the vacuum of space!"),
            new DialogueLine("Echo", HexColor("#8B5CF6"), "I was making the sound effects with my own mouth, Papa!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Hah! You got me there, kiddo. Float on over here."),
        };
        LevelDialogues[43] = new List<DialogueLine>() {
            new DialogueLine("Sparky", HexColor("#FACC15"), "Daddy! These glowing space stars are so shiny!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "They light up the whole cosmic runway, Sparky!"),
            new DialogueLine("Sparky", HexColor("#FACC15"), "Can I put one in a jar and take it to school for show and tell?"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "If you collect all fifty, we unlock the Flying Diamond Bird Skin!"),
        };
        LevelDialogues[44] = new List<DialogueLine>() {
            new DialogueLine("Clover", HexColor("#10B981"), "Dad! I am the supreme space pirate of the Milky Way!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Clover, you are wearing a tinfoil hat and holding a celery stick!"),
            new DialogueLine("Clover", HexColor("#10B981"), "This is an intergalactic ray blaster, Father, surrender your juice!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "I surrender twenty cuddles and a warm cup of cocoa. Deal?"),
        };
        LevelDialogues[45] = new List<DialogueLine>() {
            new DialogueLine("Radar", HexColor("#38BDF8"), "Beep... beep... Berry Dad detected on scanner!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Radar, your built-in dad detector is working flawlessly!"),
            new DialogueLine("Radar", HexColor("#38BDF8"), "Incoming warning: Galactic Mega-Pizza spotted in sector five!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "We are closing in on the mother ship. Five levels to go!"),
        };
        LevelDialogues[46] = new List<DialogueLine>() {
            new DialogueLine("Orbit", HexColor("#A855F7"), "Wheee! I am in stable orbit around this glowing asteroid!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Orbit, break out of your trajectory before the asteroid crumbles!"),
            new DialogueLine("Orbit", HexColor("#A855F7"), "Engaging thrusters! (Pfffft!) Safe landing achieved!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Phew! My blood pressure is higher than this orbit."),
        };
        LevelDialogues[47] = new List<DialogueLine>() {
            new DialogueLine("Nova", HexColor("#EC4899"), "Daddy, the stars out here look like glowing sugar crystals!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "They are pretty, aren't they, Nova?"),
            new DialogueLine("Nova", HexColor("#EC4899"), "They look like your shiny raspberry bumps when you are happy!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Aw, thank you, sweetheart. Let us keep moving."),
        };
        LevelDialogues[48] = new List<DialogueLine>() {
            new DialogueLine("Comet", HexColor("#F59E0B"), "Dad! I hitched a ride on a passing shooting star!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Comet! You are completely fearless and it terrifies me!"),
            new DialogueLine("Comet", HexColor("#F59E0B"), "Faster than the speed of light, Daddy!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Well, now we are on foot. Hold my leaf hat."),
        };
        LevelDialogues[49] = new List<DialogueLine>() {
            new DialogueLine("Astro", HexColor("#6366F1"), "Papa! Look at the mothership right above us! It is a giant pizza!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "That is the Galactic Mega-Pizza UFO! Our final showdown!"),
            new DialogueLine("Astro", HexColor("#6366F1"), "Starlight is trapped inside the alien dome, Dad! Save our baby!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Nobody messes with my fifty berries. Get ready, Astro!"),
        };
        LevelDialogues[50] = new List<DialogueLine>() {
            new DialogueLine("Starlight", HexColor("#FDE047"), "DADDY! You shot down the Galactic Mega-Pizza UFO!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "STARLIGHT! My fiftieth baby! You are safe!"),
            new DialogueLine("Starlight", HexColor("#FDE047"), "Look behind you, Papa! All forty-nine of my brothers and sisters are here!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "EVERY SINGLE MINI-BERRY IS HOME! Let us celebrate!"),
        };

        // Boss Pre-Fight Dialogues
        BossPreFightDialogues[10] = new List<DialogueLine>() {
            new DialogueLine("King Cheeseburg", HexColor("#FBBF24"), "MWAHAHA! I am King Cheeseburg, supreme ruler of the Salad Suburbs!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Listen here, burger breath! It is past my kid's bedtime! Give Binky back!"),
            new DialogueLine("King Cheeseburg", HexColor("#FBBF24"), "Never! You berries will be garnish on my royal platter! PREPARE TO GET SMASHED!"),
        };
        BossPreFightDialogues[20] = new List<DialogueLine>() {
            new DialogueLine("Captain Crispy Fry", HexColor("#EF4444"), "YARRR! Who dares trespass upon the Deep Fry Docks?!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "I am Berry Dad! You took Cosmo, and I am not leaving without him!"),
            new DialogueLine("Captain Crispy Fry", HexColor("#EF4444"), "Shiver me spuds! Walk the plank into the boiling fry oil! CHARGE!"),
        };
        BossPreFightDialogues[30] = new List<DialogueLine>() {
            new DialogueLine("Frosty Cone Monster", HexColor("#38BDF8"), "FREEZE, LITTLE RASPBERRY! Sundae Summit belongs to the ice cream clan!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "Your brain-freezing days are over, cone-head! Hand over Bingo!"),
            new DialogueLine("Frosty Cone Monster", HexColor("#38BDF8"), "I shall turn you into frozen blueberry sherbet! CHILL OUT!"),
        };
        BossPreFightDialogues[40] = new List<DialogueLine>() {
            new DialogueLine("Sir Sizzle Hotdog", HexColor("#EA580C"), "SIZZLE AND BURN! Welcome to my Grill Factory, juicy dad!"),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "You have kept Banjo here long enough, hotdog! Turn off the burners!"),
            new DialogueLine("Sir Sizzle Hotdog", HexColor("#EA580C"), "My mustard armor is impenetrable! Taste my flaming firepower!"),
        };
        BossPreFightDialogues[50] = new List<DialogueLine>() {
            new DialogueLine("Galactic Mega-Pizza UFO", HexColor("#A855F7"), "BZZT... WE ARE THE COSMIC PIZZA EMPIRE. YOUR BABIES WILL POWER OUR ENGINE."),
            new DialogueLine("Berry Dad", HexColor("#1E5BFF"), "You made the biggest mistake in the galaxy messing with a Grumpy Dad!"),
            new DialogueLine("Galactic Mega-Pizza UFO", HexColor("#A855F7"), "CALCULATING THREAT LEVEL... ZERO PERCENT! COMMENCE LASER BARRAGE!"),
        };
    }

// ===========================================================================
// SECTION 4: DYNAMIC MULTI-WORLD LIVE 3D DIORAMA ENGINE
// ===========================================================================
#region 4. Multi-World Live 3D Diorama Stage & Context-Aware Orbit Camera
    public void SpawnMainMenuDiorama()
    {
        ShowMenuDioramaForWorld(1, false);
    }

    public void ShowMenuDioramaForWorld(int worldIndex, bool isLockedPreview = false)
    {
        if (CurrentDioramaWorldIndex == worldIndex && CurrentDioramaLockedState == isLockedPreview && MainMenuDioramaRoot != null)
        {
            return;
        }

        CurrentDioramaWorldIndex = worldIndex;
        CurrentDioramaLockedState = isLockedPreview;

        if (MainMenuDioramaRoot != null)
        {
            Destroy(MainMenuDioramaRoot);
            MainMenuDioramaRoot = null;
        }
        if (WindSystem != null) WindSystem.ClearItems();
        if (LightPool != null) LightPool.ClearEnvTargets();

        PlaySFX(SFXType.WorldWhoosh);

        MainMenuDioramaRoot = new GameObject("Diorama_World_" + worldIndex);
        MainMenuDioramaRoot.transform.position = Vector3.zero;

        MainMenuDioramaAnimator animComp = MainMenuDioramaRoot.AddComponent<MainMenuDioramaAnimator>();
        animComp.WorldIndex = worldIndex;
        animComp.IsLockedPreview = isLockedPreview;

        switch (worldIndex)
        {
            case 1: BuildWorld1Diorama(MainMenuDioramaRoot, animComp); break;
            case 2: BuildWorld2Diorama(MainMenuDioramaRoot, animComp); break;
            case 3: BuildWorld3Diorama(MainMenuDioramaRoot, animComp); break;
            case 4: BuildWorld4Diorama(MainMenuDioramaRoot, animComp); break;
            case 5: BuildWorld5Diorama(MainMenuDioramaRoot, animComp); break;
        }

        SpawnDioramaCommonElements(MainMenuDioramaRoot, animComp, worldIndex, isLockedPreview);
        ApplyWorldAtmosphere(worldIndex, isLockedPreview);

        if (ParticlePool != null)
        {
            ParticlePool.SpawnConfettiBurst(new Vector3(0f, 2f, 0f), 24);
        }

        foreach (Collider col in MainMenuDioramaRoot.GetComponentsInChildren<Collider>())
        {
            if (col.GetComponent<DonutSpringComp>() == null)
            {
                Destroy(col);
            }
        }
    }

    private void SpawnDioramaCommonElements(GameObject root, MainMenuDioramaAnimator animComp, int worldIndex, bool isLocked)
    {
        GameObject dad = new GameObject("Diorama_BerryDad");
        dad.transform.SetParent(root.transform, false);
        dad.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        BuildBerryCharacterModel(dad, EquippedSkin);
        animComp.BerryDadPreview = dad.transform;

        Color[] miniCols = new Color[] {
            HexColor("#1E5BFF"), HexColor("#EC4899"), HexColor("#FACC15"),
            HexColor("#312E81"), HexColor("#6EE7B7"), HexColor("#FB923C")
        };
        for (int m = 0; m < 6; m++)
        {
            float ang = m * (Mathf.PI * 2f / 6f);
            float rad = 2.45f;
            Vector3 mbPos = new Vector3(Mathf.Cos(ang) * rad, 0.55f, Mathf.Sin(ang) * rad);
            GameObject mb = CreateDioramaMiniBerry("MiniBerry_Menu_" + m, mbPos, miniCols[m], 0.55f);
            mb.transform.SetParent(root.transform, false);
            animComp.MiniBerries.Add(mb.transform);
        }

        GameObject starObj = new GameObject("Diorama_GoldenStar");
        starObj.transform.SetParent(root.transform, false);
        starObj.transform.localPosition = new Vector3(0f, 3.35f, 0.5f);
        MeshFilter mfStar = starObj.AddComponent<MeshFilter>();
        mfStar.sharedMesh = LowPolyMeshHelper.GetFivePointStar(1.25f, 0.55f, 0.28f);
        MeshRenderer mrStar = starObj.AddComponent<MeshRenderer>();
        mrStar.sharedMaterial = SafeMaterialFactory.GetCachedMaterial("GoldenStarMat", HexColor("#FFD700"), null, 0.90f, 0.70f, HexColor("#FFB700"));

        GameObject halo = new GameObject("HaloRing");
        halo.transform.SetParent(starObj.transform, false);
        halo.transform.localPosition = Vector3.zero;
        halo.transform.localRotation = Quaternion.Euler(65f, 0f, 25f);
        MeshFilter mfHalo = halo.AddComponent<MeshFilter>();
        mfHalo.sharedMesh = LowPolyMeshHelper.GetTorusDonut(18, 8, 1.15f, 0.08f);
        MeshRenderer mrHalo = halo.AddComponent<MeshRenderer>();
        mrHalo.sharedMaterial = mrStar.sharedMaterial;
        animComp.GoldenStar = starObj.transform;

        GameObject spring = CreateDonutSpring(new Vector3(2.6f, 0.55f, -1.8f), 24f);
        spring.transform.SetParent(root.transform, true);
        animComp.DonutSpring = spring.transform;

        // Animated ocean below diorama (except Space moon sky)
        if (worldIndex <= 4)
        {
            GameObject sea = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sea.name = "Diorama_OceanSea";
            sea.transform.SetParent(root.transform, false);
            sea.transform.position = new Vector3(0f, -14f, 0f);
            sea.transform.localScale = new Vector3(260f, 0.5f, 260f);
            DestroyImmediate(sea.GetComponent<Collider>());
            PBRMaterialSuite seaSuite = ProceduralTextureFactory.GetOceanOrLavaPBRSuite(worldIndex);
            sea.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedPBRMaterial("DioramaSea_W" + worldIndex, seaSuite, 22f, 22f);
            var sa = sea.AddComponent<AbyssSeaAnimator>();
            sa.SeaRenderer = sea.GetComponent<Renderer>();
        }

        // Slanted Volumetric Sun God Rays on Sunny & Diner Worlds
        if ((worldIndex == 1 || worldIndex == 2) && CurrentQualityTier != GraphicsQualityTier.Mobile60FPS)
        {
            for (int r = 0; r < 2; r++)
            {
                GameObject ray = new GameObject("VolumetricSunRay_" + r);
                ray.transform.SetParent(root.transform, false);
                ray.transform.position = new Vector3(-8f + r * 14f, 18f, 4f);
                ray.transform.rotation = Quaternion.Euler(14f, 0f, -24f);
                MeshFilter mfR = ray.AddComponent<MeshFilter>();
                mfR.sharedMesh = LowPolyMeshHelper.GetVolumetricConeMesh(16, 1.2f, 5.5f, 26f);
                MeshRenderer mrR = ray.AddComponent<MeshRenderer>();
                Color rayCol = (worldIndex == 1) ? new Color(1f, 0.90f, 0.65f, 0.16f) : new Color(1f, 0.50f, 0.78f, 0.15f);
                mrR.sharedMaterial = SafeMaterialFactory.GetCachedVertexAlphaMaterial("SunRayMat_" + worldIndex, rayCol, null, 3050);
            }
        }

        for (int c = 0; c < 6; c++)
        {
            float ang = c * (Mathf.PI * 2f / 6f);
            float dist = 14f + (c % 2) * 4f;
            float cy = 2.0f + Mathf.Sin(c * 1.5f) * 2.5f;
            GameObject cloud = new GameObject("DioramaCloud_" + c);
            cloud.transform.SetParent(root.transform, false);
            cloud.transform.localPosition = new Vector3(Mathf.Cos(ang) * dist, cy, Mathf.Sin(ang) * dist);

            for (int s = 0; s < 4; s++)
            {
                GameObject sph = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sph.transform.SetParent(cloud.transform, false);
                sph.transform.localPosition = new Vector3((s - 1.5f) * 0.9f, (s % 2) * 0.35f, UnityEngine.Random.Range(-0.3f, 0.3f));
                sph.transform.localScale = Vector3.one * UnityEngine.Random.Range(1.2f, 1.9f);
                DestroyImmediate(sph.GetComponent<Collider>());
                sph.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("CloudMat", Color.white, null, 0.1f);
            }
            animComp.DriftingClouds.Add(cloud.transform);
        }

        if (isLocked)
        {
            GameObject padlockHolo = new GameObject("Diorama_LockedPadlock");
            padlockHolo.transform.SetParent(root.transform, false);
            padlockHolo.transform.localPosition = new Vector3(0f, 3.8f, 0f);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(padlockHolo.transform, false);
            body.transform.localScale = new Vector3(1.4f, 1.1f, 0.6f);
            DestroyImmediate(body.GetComponent<Collider>());
            Color goldHolo = new Color(1f, 0.85f, 0.2f, 0.85f);
            body.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("PadlockHoloMat", goldHolo, null, 0.85f, 0.7f, goldHolo);

            GameObject shackle = new GameObject("Shackle");
            shackle.transform.SetParent(padlockHolo.transform, false);
            shackle.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            MeshFilter mfS = shackle.AddComponent<MeshFilter>();
            mfS.sharedMesh = LowPolyMeshHelper.GetTorusDonut(16, 8, 0.55f, 0.16f);
            MeshRenderer mrS = shackle.AddComponent<MeshRenderer>();
            mrS.sharedMaterial = body.GetComponent<Renderer>().sharedMaterial;

            animComp.LockedPadlockHologram = padlockHolo.transform;
        }
    }

    private void BuildWorld1Diorama(GameObject root, MainMenuDioramaAnimator animComp)
    {
        CreateDioramaIslandBase(root, 1);
        BuildTropicalPalmTree(root.transform, new Vector3(-3.1f, 0.55f, 2.3f), 1.05f);
        BuildLushOakTree(root.transform, new Vector3(2.9f, 0.55f, 2.6f), 0.78f);
        BuildStylizedPineTree(root.transform, new Vector3(-2.6f, 0.55f, -2.5f), 0.72f);
        BuildFloweringBerryBush(root.transform, new Vector3(2.8f, 0.55f, -2.3f), 0.85f);
        for (int g = 0; g < 6; g++)
        {
            float ga = g * 1.05f;
            SpawnGrassTuft(root.transform, new Vector3(Mathf.Cos(ga) * 3.4f, 0.55f, Mathf.Sin(ga) * 3.4f));
        }

        GameObject burger = new GameObject("Diorama_Burger");
        burger.transform.SetParent(root.transform, false);
        burger.transform.localPosition = new Vector3(4.15f, 0.55f, 0f);
        BuildBurgerModel(burger);
        animComp.PerimeterEnemy = burger.transform;
    }

    private void BuildWorld2Diorama(GameObject root, MainMenuDioramaAnimator animComp)
    {
        CreateDioramaIslandBase(root, 2);
        SpawnArtDecoNeonStreetlamp(root.transform, new Vector3(-3.3f, 0.55f, 2.5f), HexColor("#FF2E93"));
        SpawnArtDecoNeonStreetlamp(root.transform, new Vector3(3.3f, 0.55f, 2.5f), HexColor("#00F5D4"));

        GameObject soda = new GameObject("Diorama_SodaCup");
        soda.transform.SetParent(root.transform, false);
        soda.transform.localPosition = new Vector3(4.15f, 0.55f, 0f);
        EnemyVisualAnimator eva = soda.AddComponent<EnemyVisualAnimator>();
        eva.Type = EnemyType.SodaCup;
        BuildSodaCupEnemy(soda, eva);
        animComp.PerimeterEnemy = soda.transform;
    }

    private void BuildWorld3Diorama(GameObject root, MainMenuDioramaAnimator animComp)
    {
        CreateDioramaIslandBase(root, 3);

        for (int i = 0; i < 8; i++)
        {
            float ang = i * (Mathf.PI * 2f / 8f);
            float rad = 4.6f;
            GameObject icicle = new GameObject("Icicle_" + i);
            icicle.transform.SetParent(root.transform, false);
            icicle.transform.localPosition = new Vector3(Mathf.Cos(ang) * rad, -1.2f, Mathf.Sin(ang) * rad);
            icicle.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
            MeshFilter mf = icicle.AddComponent<MeshFilter>();
            mf.sharedMesh = LowPolyMeshHelper.GetSharpCone(8, 0.25f, 1.8f);
            MeshRenderer mr = icicle.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SafeMaterialFactory.GetCachedMaterial("IcicleMat", HexColor("#BAE6FD"), null, 0.90f);
        }

        for (int i = 0; i < 4; i++)
        {
            GameObject spire = new GameObject("CrystalSpire_" + i);
            spire.transform.SetParent(root.transform, false);
            spire.transform.localPosition = new Vector3(-2.8f + (i % 2) * 1.2f, 0.6f, 2.2f + (i / 2) * 1.2f);
            MeshFilter mf = spire.AddComponent<MeshFilter>();
            mf.sharedMesh = LowPolyMeshHelper.GetHexPillar(0.35f, 1.4f + i * 0.3f);
            MeshRenderer mr = spire.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SafeMaterialFactory.GetCachedMaterial("CrystalMat", HexColor("#38BDF8"), null, 0.92f, 0f, HexColor("#0284C7"));
        }

        GameObject sundae = new GameObject("Diorama_Sundae");
        sundae.transform.SetParent(root.transform, false);
        sundae.transform.localPosition = new Vector3(4.15f, 0.55f, 0f);
        EnemyVisualAnimator eva = sundae.AddComponent<EnemyVisualAnimator>();
        eva.Type = EnemyType.SundaeMonster;
        BuildSundaeEnemy(sundae, eva);
        animComp.PerimeterEnemy = sundae.transform;
    }

    private void BuildWorld4Diorama(GameObject root, MainMenuDioramaAnimator animComp)
    {
        CreateDioramaIslandBase(root, 4);

        GameObject vent = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        vent.transform.SetParent(root.transform, false);
        vent.transform.localPosition = new Vector3(-2.6f, 0.6f, 2.4f);
        vent.transform.localScale = new Vector3(0.8f, 0.2f, 0.8f);
        DestroyImmediate(vent.GetComponent<Collider>());
        vent.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("LavaVentMat", HexColor("#FF4500"), null, 0.8f, 0f, HexColor("#FF4500") * 2f);

        GameObject hotdog = new GameObject("Diorama_Hotdog");
        hotdog.transform.SetParent(root.transform, false);
        hotdog.transform.localPosition = new Vector3(4.15f, 0.55f, 0f);
        EnemyVisualAnimator eva = hotdog.AddComponent<EnemyVisualAnimator>();
        eva.Type = EnemyType.HotdogGuard;
        BuildHotdogEnemy(hotdog, eva);
        animComp.PerimeterEnemy = hotdog.transform;
    }

    private void BuildWorld5Diorama(GameObject root, MainMenuDioramaAnimator animComp)
    {
        CreateDioramaIslandBase(root, 5);
        SpawnNeonPylon(root.transform, new Vector3(-3.0f, 0.55f, 2.4f), HexColor("#00F5D4"));
        SpawnNeonPylon(root.transform, new Vector3(3.0f, 0.55f, 2.4f), HexColor("#FF2E93"));

        GameObject drone = new GameObject("Diorama_PizzaDrone");
        drone.transform.SetParent(root.transform, false);
        drone.transform.localPosition = new Vector3(4.15f, 1.2f, 0f);
        EnemyVisualAnimator eva = drone.AddComponent<EnemyVisualAnimator>();
        eva.Type = EnemyType.PizzaDrone;
        BuildPizzaDroneEnemy(drone, eva);
        animComp.PerimeterEnemy = drone.transform;
    }

    private void CreateDioramaIslandBase(GameObject root, int worldIndex)
    {
        PBRMaterialSuite topSuite = ProceduralTextureFactory.GetWorldTerrainPBRSuite(worldIndex, false);
        PBRMaterialSuite cliffSuite = ProceduralTextureFactory.GetWorldTerrainPBRSuite(worldIndex, true);
        Color lipColor = ProceduralTextureFactory.GetWorldLipColor(worldIndex);

        GameObject top = new GameObject("Diorama_TopCap");
        top.transform.SetParent(root.transform, false);
        top.transform.localPosition = new Vector3(0f, 0.28f, 0f);
        MeshFilter mfTop = top.AddComponent<MeshFilter>();
        mfTop.sharedMesh = LowPolyMeshHelper.GetOctagonPrism(5.6f, 0.55f, 8);
        MeshRenderer mrTop = top.AddComponent<MeshRenderer>();
        mrTop.sharedMaterial = SafeMaterialFactory.GetCachedPBRMaterial("DioramaTop_W" + worldIndex, topSuite, 2.5f, 2.5f);

        GameObject lip = new GameObject("Diorama_Lip");
        lip.transform.SetParent(root.transform, false);
        lip.transform.localPosition = new Vector3(0f, -0.10f, 0f);
        MeshFilter mfLip = lip.AddComponent<MeshFilter>();
        mfLip.sharedMesh = LowPolyMeshHelper.GetOctagonPrism(5.3f, 0.24f, 8);
        MeshRenderer mrLip = lip.AddComponent<MeshRenderer>();
        Color? em = (worldIndex == 2 || worldIndex == 4 || worldIndex == 5) ? (Color?)lipColor : null;
        mrLip.sharedMaterial = SafeMaterialFactory.GetCachedMaterial("Diorama_Lip_" + lipColor.GetHashCode(), lipColor, null, 0.65f, 0.1f, em);

        GameObject cliff = new GameObject("Diorama_Cliff");
        cliff.transform.SetParent(root.transform, false);
        cliff.transform.localPosition = new Vector3(0f, -1.10f, 0f);
        MeshFilter mfCliff = cliff.AddComponent<MeshFilter>();
        mfCliff.sharedMesh = LowPolyMeshHelper.GetOctagonPrism(4.9f, 1.85f, 8);
        MeshRenderer mrCliff = cliff.AddComponent<MeshRenderer>();
        mrCliff.sharedMaterial = SafeMaterialFactory.GetCachedPBRMaterial("DioramaCliff_W" + worldIndex, cliffSuite, 2.5f, 1.0f);

        GameObject anc = new GameObject("Diorama_Anchor");
        anc.transform.SetParent(root.transform, false);
        anc.transform.localPosition = new Vector3(0f, -2.85f, 0f);
        MeshFilter mfAnc = anc.AddComponent<MeshFilter>();
        mfAnc.sharedMesh = LowPolyMeshHelper.GetOctagonPrism(3.7f, 2.2f, 8);
        MeshRenderer mrAnc = anc.AddComponent<MeshRenderer>();
        mrAnc.sharedMaterial = mrCliff.sharedMaterial;
    }

    public void RefreshDioramaPlayerSkin()
    {
        if (MainMenuDioramaRoot == null) return;
        MainMenuDioramaAnimator animComp = MainMenuDioramaRoot.GetComponent<MainMenuDioramaAnimator>();
        if (animComp == null) return;

        if (animComp.BerryDadPreview != null)
        {
            Destroy(animComp.BerryDadPreview.gameObject);
        }
        GameObject dad = new GameObject("Diorama_BerryDad");
        dad.transform.SetParent(MainMenuDioramaRoot.transform, false);
        dad.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        BuildBerryCharacterModel(dad, EquippedSkin);
        animComp.BerryDadPreview = dad.transform;

        PlaySFX(SFXType.StarDing);
        if (ParticlePool != null)
        {
            ParticlePool.SpawnConfettiBurst(dad.transform.position + Vector3.up * 1.5f, 20);
        }
    }

    private GameObject CreateDioramaMiniBerry(string name, Vector3 pos, Color fruitCol, float scale)
    {
        GameObject mb = new GameObject(name);
        mb.transform.position = pos;
        mb.transform.localScale = Vector3.one * scale;

        for (int i = 0; i < 7; i++)
        {
            float a = i * (Mathf.PI * 2f / 6f);
            Vector3 offset = (i == 6) ? new Vector3(0f, 0.6f, 0f) : new Vector3(Mathf.Cos(a) * 0.35f, 0.35f, Mathf.Sin(a) * 0.35f);
            GameObject bump = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bump.transform.SetParent(mb.transform, false);
            bump.transform.localPosition = offset;
            bump.transform.localScale = Vector3.one * 0.55f;
            DestroyImmediate(bump.GetComponent<Collider>());
            bump.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("MiniBerryMat_" + fruitCol.GetHashCode(), fruitCol, null, 0.85f);
        }

        for (int l = 0; l < 4; l++)
        {
            float la = l * 90f * Mathf.Deg2Rad;
            GameObject leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leaf.transform.SetParent(mb.transform, false);
            leaf.transform.localPosition = new Vector3(Mathf.Cos(la) * 0.22f, 0.95f, Mathf.Sin(la) * 0.22f);
            leaf.transform.localScale = new Vector3(0.20f, 0.08f, 0.28f);
            leaf.transform.localRotation = Quaternion.Euler(25f, l * 90f, 0f);
            DestroyImmediate(leaf.GetComponent<Collider>());
            leaf.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("LeafMat", HexColor("#4CD936"));
        }

        GameObject eyeL = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eyeL.transform.SetParent(mb.transform, false);
        eyeL.transform.localPosition = new Vector3(-0.16f, 0.50f, 0.38f);
        eyeL.transform.localScale = Vector3.one * 0.18f;
        DestroyImmediate(eyeL.GetComponent<Collider>());
        eyeL.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("EyeBlack", Color.black, null, 0.92f);

        GameObject eyeR = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eyeR.transform.SetParent(mb.transform, false);
        eyeR.transform.localPosition = new Vector3(0.16f, 0.50f, 0.38f);
        eyeR.transform.localScale = Vector3.one * 0.18f;
        DestroyImmediate(eyeR.GetComponent<Collider>());
        eyeR.GetComponent<Renderer>().sharedMaterial = eyeL.GetComponent<Renderer>().sharedMaterial;

        return mb;
    }

    public void ApplyWorldAtmosphere(int worldIndex, bool isLocked = false)
    {
        if (MainCam == null) return;

        Color skyColor = HexColor("#5BC0FF");
        Color lightColor = new Color(1f, 0.95f, 0.84f);
        Color rimColor = new Color(0.98f, 0.65f, 0.55f);
        Color ambSky = HexColor("#38BDF8");
        Color ambEq = HexColor("#FDBA74");
        Color ambGnd = HexColor("#1E3A2F");
        float lightIntensity = 1.40f;

        switch (worldIndex)
        {
            case 1:
                skyColor = HexColor("#38BDF8");
                lightColor = new Color(1f, 0.94f, 0.80f);
                rimColor = HexColor("#FB7185");
                ambSky = HexColor("#38BDF8");
                ambEq = HexColor("#FDBA74");
                ambGnd = HexColor("#1A3A2A");
                break;
            case 2:
                skyColor = HexColor("#EC4899");
                lightColor = new Color(1f, 0.84f, 0.68f);
                rimColor = HexColor("#00F5D4");
                ambSky = HexColor("#7E22CE");
                ambEq = HexColor("#EC4899");
                ambGnd = HexColor("#1E1B4B");
                lightIntensity = 1.25f;
                break;
            case 3:
                skyColor = HexColor("#7DD3FC");
                lightColor = new Color(0.92f, 0.97f, 1f);
                rimColor = HexColor("#2DD4BF");
                ambSky = HexColor("#1E3A8A");
                ambEq = HexColor("#7DD3FC");
                ambGnd = HexColor("#0C4A6E");
                break;
            case 4:
                skyColor = HexColor("#7F1D1D");
                lightColor = new Color(1f, 0.72f, 0.52f);
                rimColor = HexColor("#FACC15");
                ambSky = HexColor("#450A0A");
                ambEq = HexColor("#EA580C");
                ambGnd = HexColor("#18181B");
                break;
            case 5:
                skyColor = HexColor("#070B1E");
                lightColor = new Color(0.88f, 0.82f, 1f);
                rimColor = HexColor("#00F5D4");
                ambSky = HexColor("#1E1B4B");
                ambEq = HexColor("#6D28D9");
                ambGnd = HexColor("#090D1F");
                lightIntensity = 1.20f;
                break;
        }

        if (isLocked) lightIntensity *= 0.72f;

        MainCam.backgroundColor = skyColor;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambSky;
        RenderSettings.ambientEquatorColor = ambEq;
        RenderSettings.ambientGroundColor = ambGnd;

        RenderSettings.fog = true;
        RenderSettings.fogColor = ambEq;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.0042f;

        if (KeyLight != null)
        {
            KeyLight.color = lightColor;
            KeyLight.intensity = lightIntensity;
        }
        if (RimLight != null)
        {
            RimLight.color = rimColor;
            RimLight.intensity = 0.42f;
        }
        if (SkyDomeMeshFilter != null)
        {
            SkyDomeMeshFilter.sharedMesh = CinematicAtmosphereEngine.BuildGradientSkyDomeMesh(worldIndex);
        }
    }
#endregion

// ===========================================================================
// SECTION 5: WORLD SELECT, LEVEL SELECT, HUD & SETTINGS UI
// ===========================================================================
#region 5. Main Menu, World Select Carousel & Level Select 5x2 Grid UI
    void SetupUI()
    {
        GameObject canvasGo = new GameObject("MainCanvas");
        MainCanvas = canvasGo.AddComponent<Canvas>();
        MainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<GraphicRaycaster>();
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject eventGo = GameObject.Find("EventSystem");
        if (eventGo == null)
        {
            eventGo = new GameObject("EventSystem");
            eventGo.AddComponent<EventSystem>();
            eventGo.AddComponent<StandaloneInputModule>();
        }

        // Non-raycasted Screen FX Vignette (Damage Crimson Pulse & Phase 2 Glow)
        GameObject fxGo = new GameObject("ScreenFXVignette");
        fxGo.transform.SetParent(MainCanvas.transform, false);
        RectTransform fxRt = fxGo.AddComponent<RectTransform>();
        fxRt.anchorMin = Vector2.zero; fxRt.anchorMax = Vector2.one; fxRt.sizeDelta = Vector2.zero;
        ScreenFXVignetteImg = fxGo.AddComponent<Image>();
        ScreenFXVignetteImg.sprite = ProceduralUISpriteFactory.GetSoftVignetteSprite();
        ScreenFXVignetteImg.color = new Color(0.95f, 0.1f, 0.15f, 0f);
        ScreenFXVignetteImg.raycastTarget = false;

        GameObject toastGo = new GameObject("UIToastBanner");
        toastGo.transform.SetParent(MainCanvas.transform, false);
        RectTransform tRt = toastGo.AddComponent<RectTransform>();
        tRt.anchorMin = new Vector2(0.5f, 0f); tRt.anchorMax = new Vector2(0.5f, 0f); tRt.pivot = new Vector2(0.5f, 0f);
        tRt.sizeDelta = new Vector2(740f, 62f);
        tRt.anchoredPosition = new Vector2(0f, 40f);

        Image tImg = toastGo.AddComponent<Image>();
        tImg.sprite = ProceduralUISpriteFactory.GetRoundedCardSprite();
        tImg.type = Image.Type.Sliced;
        tImg.color = new Color(0.06f, 0.09f, 0.16f, 0.92f);

        toastGo.AddComponent<UIToastBanner>();

        GameObject tTextGo = new GameObject("Text");
        tTextGo.transform.SetParent(toastGo.transform, false);
        RectTransform ttRt = tTextGo.AddComponent<RectTransform>();
        ttRt.anchorMin = Vector2.zero; ttRt.anchorMax = Vector2.one; ttRt.sizeDelta = Vector2.zero;
        Text tt = tTextGo.AddComponent<Text>();
        tt.font = GetSafeFont();
        tt.fontSize = 24;
        tt.color = Color.white;
        tt.alignment = TextAnchor.MiddleCenter;
        tt.fontStyle = FontStyle.Bold;

        BuildMainMenuUI();
        BuildWorldSelectUI();
        BuildLevelSelectUI();
        BuildHudUI();
        BuildVictoryUI();
        BuildPauseUI();
        BuildHowToPlayUI();
        BuildSettingsUI();
        BuildDialogueUI();
        BuildEndingCelebrationUI();
        BuildTouchControlsUI();
    }

    private RectTransform m_TitleAnimRt;

    void BuildMainMenuUI()
    {
        PanelMainMenu = new GameObject("Panel_MainMenu");
        PanelMainMenu.transform.SetParent(MainCanvas.transform, false);
        RectTransform rt = PanelMainMenu.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;

        Image bg = PanelMainMenu.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0f);

        GameObject vigGo = new GameObject("SoftVignette");
        vigGo.transform.SetParent(PanelMainMenu.transform, false);
        RectTransform vigRt = vigGo.AddComponent<RectTransform>();
        vigRt.anchorMin = Vector2.zero; vigRt.anchorMax = Vector2.one; vigRt.sizeDelta = Vector2.zero;
        Image vigImg = vigGo.AddComponent<Image>();
        vigImg.sprite = ProceduralUISpriteFactory.GetSoftVignetteSprite();
        vigImg.color = Color.white;
        vigImg.raycastTarget = false;

        GameObject titleGo = new GameObject("TitleContainer");
        titleGo.transform.SetParent(PanelMainMenu.transform, false);
        m_TitleAnimRt = titleGo.AddComponent<RectTransform>();
        m_TitleAnimRt.anchoredPosition = new Vector2(0f, 390f);
        m_TitleAnimRt.sizeDelta = new Vector2(1280f, 130f);

        Text title = CreateUIText(titleGo.transform, "BERRY BOUNCE 3D", 84, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(1280f, 120f), HexColor("#38BDF8"));
        title.fontStyle = FontStyle.Bold;

        GameObject subPill = CreateCardPanel(PanelMainMenu.transform, new Vector2(860f, 48f), new Vector2(0f, 315f), new Color(0.06f, 0.09f, 0.16f, 0.65f), HexColor("#FBBF24"), 2.0f);
        CreateUIText(subPill.transform, "Made by BerryBounce3D.xyz", 22, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(840f, 44f), HexColor("#FDE047"));

        GameObject starBadge = CreateCardPanel(PanelMainMenu.transform, new Vector2(320f, 54f), Vector2.zero, new Color(0.06f, 0.09f, 0.16f, 0.78f), HexColor("#F59E0B"), 2.5f);
        RectTransform sbRt = starBadge.GetComponent<RectTransform>();
        sbRt.anchorMin = new Vector2(1f, 1f); sbRt.anchorMax = new Vector2(1f, 1f); sbRt.pivot = new Vector2(1f, 1f);
        sbRt.anchoredPosition = new Vector2(-36f, -32f);
        MainMenuStarCountText = CreateUIText(starBadge.transform, "Stars: " + ProgressionManager.GetTotalStars() + " / 50 ★", 23, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(300f, 46f), HexColor("#FDE047"));

        GameObject barGo = new GameObject("ActionBar");
        barGo.transform.SetParent(PanelMainMenu.transform, false);
        RectTransform barRt = barGo.AddComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0.5f, 0f); barRt.anchorMax = new Vector2(0.5f, 0f); barRt.pivot = new Vector2(0.5f, 0f);
        barRt.anchoredPosition = new Vector2(0f, 42f);
        barRt.sizeDelta = new Vector2(1100f, 90f);

        CreateUIButton(barGo.transform, "PLAY GAME ▶", new Vector2(260f, 74f), new Vector2(-375f, 0f), HexColor("#10B981"), () => GoToWorldSelect(), 26);
        CreateUIButton(barGo.transform, "HOW TO PLAY", new Vector2(250f, 74f), new Vector2(-120f, 0f), HexColor("#0284C7"), () => OpenHowToPlay(), 24);
        CreateUIButton(barGo.transform, "SETTINGS", new Vector2(230f, 74f), new Vector2(125f, 0f), HexColor("#8B5CF6"), () => OpenSettings(), 24);
        CreateUIButton(barGo.transform, "QUIT", new Vector2(170f, 74f), new Vector2(340f, 0f), HexColor("#EF4444"), () => Application.Quit(), 24);
    }

    public Text MainMenuStarCountText;

    void BuildWorldSelectUI()
    {
        PanelWorldSelect = new GameObject("Panel_WorldSelect");
        PanelWorldSelect.transform.SetParent(MainCanvas.transform, false);
        RectTransform rt = PanelWorldSelect.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;

        Image bg = PanelWorldSelect.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0f);

        GameObject vigGo = new GameObject("SoftVignette");
        vigGo.transform.SetParent(PanelWorldSelect.transform, false);
        RectTransform vigRt = vigGo.AddComponent<RectTransform>();
        vigRt.anchorMin = Vector2.zero; vigRt.anchorMax = Vector2.one; vigRt.sizeDelta = Vector2.zero;
        Image vigImg = vigGo.AddComponent<Image>();
        vigImg.sprite = ProceduralUISpriteFactory.GetSoftVignetteSprite();
        vigImg.color = Color.white;
        vigImg.raycastTarget = false;

        GameObject topBar = new GameObject("TopNavBar");
        topBar.transform.SetParent(PanelWorldSelect.transform, false);
        RectTransform topRt = topBar.AddComponent<RectTransform>();
        topRt.anchorMin = new Vector2(0f, 1f); topRt.anchorMax = new Vector2(1f, 1f); topRt.pivot = new Vector2(0.5f, 1f);
        topRt.anchoredPosition = new Vector2(0f, -32f);
        topRt.sizeDelta = new Vector2(-64f, 90f);

        GameObject backBtn = CreateUIButton(topBar.transform, "◀ MAIN MENU", new Vector2(220f, 62f), new Vector2(0f, 0f), HexColor("#475569"), () => GoToMainMenu(), 22);
        RectTransform backRt = backBtn.GetComponent<RectTransform>();
        backRt.anchorMin = new Vector2(0f, 0.5f); backRt.anchorMax = new Vector2(0f, 0.5f); backRt.pivot = new Vector2(0f, 0.5f);

        GameObject titleBox = new GameObject("TitleBox");
        titleBox.transform.SetParent(topBar.transform, false);
        RectTransform tbRt = titleBox.AddComponent<RectTransform>();
        tbRt.anchoredPosition = Vector2.zero;
        tbRt.sizeDelta = new Vector2(700f, 85f);
        CreateUIText(titleBox.transform, "SELECT WORLD", 38, TextAnchor.UpperCenter, new Vector2(0f, 12f), new Vector2(600f, 45f), Color.white);
        WorldSelectCampaignTallyText = CreateUIText(titleBox.transform, "LEVELS CLEARED: 0 / 50   •   ★ STARS: 0 / 50", 20, TextAnchor.LowerCenter, new Vector2(0f, -16f), new Vector2(600f, 32f), HexColor("#FDE047"));

        GameObject setBtn = CreateUIButton(topBar.transform, "⚙ SETTINGS", new Vector2(235f, 62f), new Vector2(0f, 0f), HexColor("#6366F1"), () => OpenSettings(), 22);
        RectTransform setRt = setBtn.GetComponent<RectTransform>();
        setRt.anchorMin = new Vector2(1f, 0.5f); setRt.anchorMax = new Vector2(1f, 0.5f); setRt.pivot = new Vector2(1f, 0.5f);

        GameObject prevBtn = CreateUIButton(PanelWorldSelect.transform, "◀", new Vector2(68f, 68f), new Vector2(36f, 100f), HexColor("#1E293B"), () => SelectPreviousWorld(), 28);
        RectTransform prevRt = prevBtn.GetComponent<RectTransform>();
        prevRt.anchorMin = new Vector2(0f, 0.5f); prevRt.anchorMax = new Vector2(0f, 0.5f); prevRt.pivot = new Vector2(0f, 0.5f);

        GameObject nextBtn = CreateUIButton(PanelWorldSelect.transform, "▶", new Vector2(68f, 68f), new Vector2(-36f, 100f), HexColor("#1E293B"), () => SelectNextWorld(), 28);
        RectTransform nextRt = nextBtn.GetComponent<RectTransform>();
        nextRt.anchorMin = new Vector2(1f, 0.5f); nextRt.anchorMax = new Vector2(1f, 0.5f); nextRt.pivot = new Vector2(1f, 0.5f);

        GameObject heroStrip = CreateCardPanel(PanelWorldSelect.transform, new Vector2(860f, 112f), new Vector2(0f, 245f), new Color(0.06f, 0.09f, 0.16f, 0.78f), HexColor("#22C55E"), 3f);
        RectTransform hsRt = heroStrip.GetComponent<RectTransform>();
        hsRt.anchorMin = new Vector2(0.5f, 0f); hsRt.anchorMax = new Vector2(0.5f, 0f); hsRt.pivot = new Vector2(0.5f, 0f);

        WorldHeroTitle = CreateUIText(heroStrip.transform, "WORLD 1 — SALAD SUBURBS", 28, TextAnchor.UpperLeft, new Vector2(-120f, 22f), new Vector2(560f, 40f), HexColor("#4ADE80"));
        WorldHeroSubtitle = CreateUIText(heroStrip.transform, "Sunny Meadows  •  Length: Short → Medium (62m-124m)  •  Boss: King Cheeseburg", 18, TextAnchor.LowerLeft, new Vector2(-120f, -22f), new Vector2(560f, 40f), Color.white);

        WorldHeroActionBtn = CreateUIButton(heroStrip.transform, "ENTER WORLD 1 ▶", new Vector2(250f, 68f), new Vector2(280f, 0f), HexColor("#22C55E"), () => EnterFocusedWorld(), 22);
        WorldHeroActionText = WorldHeroActionBtn.GetComponentInChildren<Text>();

        GameObject carousel = new GameObject("BottomWorldCarousel");
        carousel.transform.SetParent(PanelWorldSelect.transform, false);
        RectTransform cRt = carousel.AddComponent<RectTransform>();
        cRt.anchorMin = new Vector2(0.5f, 0f); cRt.anchorMax = new Vector2(0.5f, 0f); cRt.pivot = new Vector2(0.5f, 0f);
        cRt.anchoredPosition = new Vector2(0f, 28f);
        cRt.sizeDelta = new Vector2(1500f, 195f);

        WorldCards.Clear();
        string[] wNames = new string[] { "Salad Suburbs", "Deep Fry Docks", "Sundae Summit", "Grill Factory", "Cosmic Drive-Thru" };
        Color[] wColors = new Color[] { HexColor("#22C55E"), HexColor("#FB923C"), HexColor("#38BDF8"), HexColor("#EF4444"), HexColor("#A855F7") };

        for (int i = 0; i < 5; i++)
        {
            int w = i + 1;
            float xPos = (i - 2) * 300f;
            GameObject card = CreateCardPanel(carousel.transform, new Vector2(284f, 182f), new Vector2(xPos, 0f), new Color(0.06f, 0.09f, 0.16f, 0.80f), wColors[i], 3f);
            card.name = "WorldCard_" + w;

            GameObject hBanner = new GameObject("HeaderBanner");
            hBanner.transform.SetParent(card.transform, false);
            RectTransform hbRt = hBanner.AddComponent<RectTransform>();
            hbRt.anchoredPosition = new Vector2(0f, 58f);
            hbRt.sizeDelta = new Vector2(264f, 38f);
            Image hbImg = hBanner.AddComponent<Image>();
            hbImg.sprite = ProceduralUISpriteFactory.GetGlossyButtonSprite();
            hbImg.type = Image.Type.Sliced;
            hbImg.color = wColors[i];

            Text hbText = CreateUIText(hBanner.transform, "WORLD " + w, 20, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(240f, 34f), Color.white);
            hbText.fontStyle = FontStyle.Bold;

            GameObject crownGo = new GameObject("CrownIcon");
            crownGo.transform.SetParent(hBanner.transform, false);
            RectTransform crt = crownGo.AddComponent<RectTransform>();
            crt.anchoredPosition = new Vector2(102f, 0f);
            crt.sizeDelta = new Vector2(30f, 30f);
            Image crImg = crownGo.AddComponent<Image>();
            crImg.sprite = ProceduralUISpriteFactory.GetBossCrownSprite();
            crImg.gameObject.SetActive(ProgressionManager.IsLevelCleared(w * 10));

            CreateUIText(card.transform, wNames[i], 21, TextAnchor.MiddleCenter, new Vector2(0f, 12f), new Vector2(260f, 36f), Color.white);

            GameObject progGo = new GameObject("MiniProgress");
            progGo.transform.SetParent(card.transform, false);
            RectTransform prt = progGo.AddComponent<RectTransform>();
            prt.anchoredPosition = new Vector2(0f, -42f);
            prt.sizeDelta = new Vector2(260f, 40f);
            CreateUIText(progGo.transform, "Levels: " + ProgressionManager.GetWorldClearedCount(w) + "/10  •  ★ " + ProgressionManager.GetWorldStarCount(w) + "/10", 17, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(250f, 36f), HexColor("#FDE047"));

            Button cBtn = card.AddComponent<Button>();
            int capturedW = w;
            cBtn.onClick.AddListener(() => {
                if (FocusedWorldIndex != capturedW) FocusWorld(capturedW);
                else EnterFocusedWorld();
            });
            card.AddComponent<UIBouncyButton>();
            WorldCards.Add(card);
        }
    }

    public void FocusWorld(int worldIndex)
    {
        FocusedWorldIndex = Mathf.Clamp(worldIndex, 1, 5);
        bool isUnlocked = ProgressionManager.IsWorldUnlocked(FocusedWorldIndex);

        ShowMenuDioramaForWorld(FocusedWorldIndex, !isUnlocked);
        if (CamCtrl != null) CamCtrl.SetMenuCameraState(MenuCameraState.WorldSelect);

        string[] wNames = new string[] { "SALAD SUBURBS", "DEEP FRY DOCKS", "SUNDAE SUMMIT", "GRILL FACTORY", "COSMIC DRIVE-THRU" };
        Color[] wColors = new Color[] { HexColor("#4ADE80"), HexColor("#FB923C"), HexColor("#38BDF8"), HexColor("#F87171"), HexColor("#C084FC") };
        string[] wFlavors = new string[] {
            "GTA-6 Tropical Coast  •  Length: Short → Medium (62m-124m)  •  Boss: King Cheeseburg",
            "Vice Neon Boardwalk  •  Length: Medium (132m-192m)  •  Boss: Captain Crispy Fry",
            "Mirror Glacier Peaks  •  Length: Long (200m-254m)  •  Boss: Frosty Cone Monster",
            "Molten Lava Forges  •  Length: Very Long (262m-316m)  •  Boss: Sir Sizzle Hotdog",
            "Low-Gravity Cosmic Highway!  •  Length: Marathon (324m-378m)  •  Boss: Mega-Pizza UFO"
        };

        WorldHeroTitle.text = "WORLD " + FocusedWorldIndex + " — " + wNames[FocusedWorldIndex - 1];
        WorldHeroTitle.color = wColors[FocusedWorldIndex - 1];
        WorldHeroSubtitle.text = wFlavors[FocusedWorldIndex - 1];

        if (isUnlocked)
        {
            WorldHeroActionText.text = "ENTER WORLD " + FocusedWorldIndex + " ▶";
            WorldHeroActionBtn.GetComponent<Image>().color = wColors[FocusedWorldIndex - 1];
        }
        else
        {
            WorldHeroActionText.text = "LOCKED 🔒";
            WorldHeroActionBtn.GetComponent<Image>().color = HexColor("#475569");
        }

        for (int i = 0; i < WorldCards.Count; i++)
        {
            int w = i + 1;
            RectTransform crt = WorldCards[i].GetComponent<RectTransform>();
            if (w == FocusedWorldIndex)
            {
                crt.localScale = Vector3.one * 1.05f;
                crt.anchoredPosition = new Vector2(crt.anchoredPosition.x, 14f);
            }
            else
            {
                crt.localScale = Vector3.one;
                crt.anchoredPosition = new Vector2(crt.anchoredPosition.x, 0f);
            }
        }

        if (WorldSelectCampaignTallyText != null)
        {
            WorldSelectCampaignTallyText.text = "LEVELS CLEARED: " + ProgressionManager.GetTotalClearedCount() + " / 50   •   ★ STARS: " + ProgressionManager.GetTotalStars() + " / 50";
        }
    }

    public void SelectPreviousWorld() => FocusWorld((FocusedWorldIndex > 1) ? FocusedWorldIndex - 1 : 5);
    public void SelectNextWorld() => FocusWorld((FocusedWorldIndex < 5) ? FocusedWorldIndex + 1 : 1);

    public void EnterFocusedWorld()
    {
        if (ProgressionManager.IsWorldUnlocked(FocusedWorldIndex))
        {
            CurrentWorld = FocusedWorldIndex;
            GoToLevelSelect(CurrentWorld);
        }
        else
        {
            if (WorldHeroActionBtn != null)
            {
                UIBouncyButton bb = WorldHeroActionBtn.GetComponent<UIBouncyButton>();
                if (bb != null) bb.TriggerLockedShake();
            }
            UIToastBanner.Show("🔒 Defeat World " + (FocusedWorldIndex - 1) + " Boss (Level " + ((FocusedWorldIndex - 1) * 10) + ") to unlock World " + FocusedWorldIndex + "!", HexColor("#EF4444"));
        }
    }

    void BuildLevelSelectUI()
    {
        PanelLevelSelect = new GameObject("Panel_LevelSelect");
        PanelLevelSelect.transform.SetParent(MainCanvas.transform, false);
        RectTransform rt = PanelLevelSelect.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;

        Image bg = PanelLevelSelect.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0f);

        GameObject vigGo = new GameObject("SoftVignette");
        vigGo.transform.SetParent(PanelLevelSelect.transform, false);
        RectTransform vigRt = vigGo.AddComponent<RectTransform>();
        vigRt.anchorMin = Vector2.zero; vigRt.anchorMax = Vector2.one; vigRt.sizeDelta = Vector2.zero;
        Image vigImg = vigGo.AddComponent<Image>();
        vigImg.sprite = ProceduralUISpriteFactory.GetSoftVignetteSprite();
        vigImg.color = Color.white;
        vigImg.raycastTarget = false;

        GameObject topBar = new GameObject("TopLevelHeader");
        topBar.transform.SetParent(PanelLevelSelect.transform, false);
        RectTransform topRt = topBar.AddComponent<RectTransform>();
        topRt.anchorMin = new Vector2(0f, 1f); topRt.anchorMax = new Vector2(1f, 1f); topRt.pivot = new Vector2(0.5f, 1f);
        topRt.anchoredPosition = new Vector2(0f, -32f);
        topRt.sizeDelta = new Vector2(-64f, 96f);

        GameObject backBtn = CreateUIButton(topBar.transform, "◀ WORLDS", new Vector2(210f, 62f), Vector2.zero, HexColor("#475569"), () => GoToWorldSelect(), 22);
        RectTransform bRt = backBtn.GetComponent<RectTransform>();
        bRt.anchorMin = new Vector2(0f, 0.5f); bRt.anchorMax = new Vector2(0f, 0.5f); bRt.pivot = new Vector2(0f, 0.5f);

        GameObject bannerCenter = CreateCardPanel(topBar.transform, new Vector2(620f, 80f), Vector2.zero, new Color(0.06f, 0.09f, 0.16f, 0.85f), HexColor("#38BDF8"), 2.5f);
        LevelSelectWorldTitleText = CreateUIText(bannerCenter.transform, "WORLD 1: SALAD SUBURBS", 28, TextAnchor.UpperCenter, new Vector2(0f, 14f), new Vector2(600f, 40f), Color.white);
        LevelSelectCampaignTallyText = CreateUIText(bannerCenter.transform, "Levels Cleared: 0 / 10   •   ★ Stars: 0 / 10", 18, TextAnchor.LowerCenter, new Vector2(0f, -14f), new Vector2(600f, 32f), HexColor("#FDE047"));

        GameObject playNextBtn = CreateUIButton(topBar.transform, "PLAY NEXT ▶", new Vector2(245f, 62f), Vector2.zero, HexColor("#22C55E"), () => PlayNextInCurrentWorld(), 22);
        RectTransform pnRt = playNextBtn.GetComponent<RectTransform>();
        pnRt.anchorMin = new Vector2(1f, 0.5f); pnRt.anchorMax = new Vector2(1f, 0.5f); pnRt.pivot = new Vector2(1f, 0.5f);

        GameObject swL = CreateUIButton(PanelLevelSelect.transform, "◀ W", new Vector2(64f, 64f), new Vector2(28f, 0f), HexColor("#1E293B"), () => SwitchToRelativeWorld(-1), 22);
        RectTransform swLRt = swL.GetComponent<RectTransform>();
        swLRt.anchorMin = new Vector2(0f, 0.5f); swLRt.anchorMax = new Vector2(0f, 0.5f); swLRt.pivot = new Vector2(0f, 0.5f);

        GameObject swR = CreateUIButton(PanelLevelSelect.transform, "W ▶", new Vector2(64f, 64f), new Vector2(-28f, 0f), HexColor("#1E293B"), () => SwitchToRelativeWorld(1), 22);
        RectTransform swRRt = swR.GetComponent<RectTransform>();
        swRRt.anchorMin = new Vector2(1f, 0.5f); swRRt.anchorMax = new Vector2(1f, 0.5f); swRRt.pivot = new Vector2(1f, 0.5f);

        GameObject deck = CreateCardPanel(PanelLevelSelect.transform, new Vector2(1460f, 600f), new Vector2(0f, -10f), new Color(0.06f, 0.09f, 0.16f, 0.68f), HexColor("#38BDF8"), 3.5f);

        LevelDeckCards.Clear();
        for (int i = 0; i < 10; i++)
        {
            int stageNum = i + 1;
            int col = i % 5;
            int row = i / 5;
            float x = (col - 2) * 276f;
            float y = (row == 0) ? 120f : -120f;

            GameObject card = CreateCardPanel(deck.transform, new Vector2(252f, 212f), new Vector2(x, y), new Color(0.10f, 0.14f, 0.24f, 0.92f), HexColor("#38BDF8"), 2.5f);
            card.name = "LevelCard_" + stageNum;
            LevelDeckCards.Add(card);
        }

        GameObject botFooter = CreateCardPanel(PanelLevelSelect.transform, new Vector2(1100f, 52f), new Vector2(0f, 26f), new Color(0.06f, 0.09f, 0.16f, 0.82f), HexColor("#F59E0B"), 2.0f);
        RectTransform bfRt = botFooter.GetComponent<RectTransform>();
        bfRt.anchorMin = new Vector2(0.5f, 0f); bfRt.anchorMax = new Vector2(0.5f, 0f); bfRt.pivot = new Vector2(0.5f, 0f);
        CreateUIText(botFooter.transform, "🏆 Golden Berry Skin: " + ProgressionManager.GetTotalClearedCount() + "/50 Levels   |   💎 Flying Diamond Bird Skin: " + ProgressionManager.GetTotalStars() + "/50 ★ Stars", 20, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(1080f, 44f), HexColor("#FDE047"));
    }

    public void RefreshLevelSelectGrid()
    {
        if (LevelDeckCards == null || LevelDeckCards.Count < 10) return;

        string[] wNames = new string[] { "SALAD SUBURBS", "DEEP FRY DOCKS", "SUNDAE SUMMIT", "GRILL FACTORY", "COSMIC DRIVE-THRU" };
        Color[] wColors = new Color[] { HexColor("#4ADE80"), HexColor("#FB923C"), HexColor("#38BDF8"), HexColor("#F87171"), HexColor("#C084FC") };

        LevelSelectWorldTitleText.text = "WORLD " + CurrentWorld + ": " + wNames[CurrentWorld - 1];
        LevelSelectWorldTitleText.color = wColors[CurrentWorld - 1];
        LevelSelectCampaignTallyText.text = "Levels Cleared: " + ProgressionManager.GetWorldClearedCount(CurrentWorld) + " / 10   •   ★ Stars: " + ProgressionManager.GetWorldStarCount(CurrentWorld) + " / 10";

        for (int i = 0; i < 10; i++)
        {
            int stageNum = i + 1;
            int globalLvl = (CurrentWorld - 1) * 10 + stageNum;
            GameObject card = LevelDeckCards[i];
            LevelLengthSpec spec = LevelLengthCalculator.GetSpec(globalLvl);

            foreach (Transform child in card.transform)
            {
                if (child.name != "InnerFill") Destroy(child.gameObject);
            }

            bool isUnlocked = ProgressionManager.IsLevelUnlocked(globalLvl);
            bool isCleared = ProgressionManager.IsLevelCleared(globalLvl);
            bool hasStar = ProgressionManager.IsStarCollected(globalLvl);

            CreateUIText(card.transform, "LEVEL " + globalLvl, 20, TextAnchor.UpperLeft, new Vector2(-45f, 84f), new Vector2(130f, 32f), Color.white);

            GameObject tierPill = new GameObject("TierPill");
            tierPill.transform.SetParent(card.transform, false);
            RectTransform tpRt = tierPill.AddComponent<RectTransform>();
            tpRt.anchoredPosition = new Vector2(64f, 84f);
            tpRt.sizeDelta = new Vector2(95f, 28f);
            Image tpImg = tierPill.AddComponent<Image>();
            tpImg.sprite = ProceduralUISpriteFactory.GetGlossyButtonSprite();
            tpImg.type = Image.Type.Sliced;
            tpImg.color = spec.TierBadgeColor;
            CreateUIText(tierPill.transform, spec.LengthTierLabel, 13, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(90f, 26f), Color.white);

            if (!isUnlocked)
            {
                GameObject lockGo = new GameObject("Padlock");
                lockGo.transform.SetParent(card.transform, false);
                RectTransform lrt = lockGo.AddComponent<RectTransform>();
                lrt.anchoredPosition = new Vector2(0f, 15f);
                lrt.sizeDelta = new Vector2(54f, 54f);
                Image limg = lockGo.AddComponent<Image>();
                limg.sprite = ProceduralUISpriteFactory.GetPadlockClosedSprite();
                limg.color = HexColor("#94A3B8");

                CreateUIText(card.transform, "LOCKED", 18, TextAnchor.MiddleCenter, new Vector2(0f, -25f), new Vector2(200f, 30f), HexColor("#94A3B8"));
                CreateUIText(card.transform, "Beat Level " + (globalLvl - 1), 13, TextAnchor.MiddleCenter, new Vector2(0f, -50f), new Vector2(200f, 24f), HexColor("#64748B"));
            }
            else if (spec.IsBossLevel)
            {
                GameObject crownGo = new GameObject("Crown");
                crownGo.transform.SetParent(card.transform, false);
                RectTransform crt = crownGo.AddComponent<RectTransform>();
                crt.anchoredPosition = new Vector2(0f, 18f);
                crt.sizeDelta = new Vector2(52f, 52f);
                Image crImg = crownGo.AddComponent<Image>();
                crImg.sprite = ProceduralUISpriteFactory.GetBossCrownSprite();

                CreateUIText(card.transform, isCleared ? "DEFEATED ✔" : "FIGHT! ⚔", 18, TextAnchor.MiddleCenter, new Vector2(0f, -22f), new Vector2(200f, 30f), isCleared ? HexColor("#22C55E") : HexColor("#EF4444"));
                CreateUIText(card.transform, spec.BossMaxHP + " HP", 14, TextAnchor.MiddleCenter, new Vector2(0f, -48f), new Vector2(200f, 24f), HexColor("#FACC15"));
            }
            else if (isCleared)
            {
                GameObject chkGo = new GameObject("Checkmark");
                chkGo.transform.SetParent(card.transform, false);
                RectTransform chkRt = chkGo.AddComponent<RectTransform>();
                chkRt.anchoredPosition = new Vector2(0f, 22f);
                chkRt.sizeDelta = new Vector2(38f, 38f);
                Image chkImg = chkGo.AddComponent<Image>();
                chkImg.sprite = ProceduralUISpriteFactory.GetCheckmarkSprite();

                float bestTime = ProgressionManager.GetBestTime(globalLvl);
                string timeStr = (bestTime > 0f) ? string.Format("{0:0}:{1:00}", (int)bestTime / 60, (int)bestTime % 60) : "--:--";
                CreateUIText(card.transform, "⏱ " + timeStr, 15, TextAnchor.MiddleCenter, new Vector2(0f, -18f), new Vector2(200f, 26f), HexColor("#38BDF8"));
                CreateUIText(card.transform, "● " + BerryNames[globalLvl - 1], 15, TextAnchor.MiddleCenter, new Vector2(0f, -44f), new Vector2(200f, 26f), HexColor(BerryHexColors[globalLvl - 1]));
            }
            else
            {
                GameObject playBadge = new GameObject("PlayBadge");
                playBadge.transform.SetParent(card.transform, false);
                RectTransform pbRt = playBadge.AddComponent<RectTransform>();
                pbRt.anchoredPosition = new Vector2(0f, 10f);
                pbRt.sizeDelta = new Vector2(130f, 42f);
                Image pbImg = playBadge.AddComponent<Image>();
                pbImg.sprite = ProceduralUISpriteFactory.GetGlossyButtonSprite();
                pbImg.type = Image.Type.Sliced;
                pbImg.color = HexColor("#22C55E");
                CreateUIText(playBadge.transform, "PLAY ▶", 18, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(120f, 38f), Color.white);
                playBadge.AddComponent<UIPulseGlow>();

                CreateUIText(card.transform, "~" + ((int)spec.TargetCourseMeters) + "m", 14, TextAnchor.MiddleCenter, new Vector2(0f, -38f), new Vector2(200f, 24f), Color.white);
            }

            CreateUIText(card.transform, spec.IsBossLevel ? "40x40 Arena" : "~" + ((int)spec.TargetCourseMeters) + "m", 13, TextAnchor.LowerLeft, new Vector2(-60f, -84f), new Vector2(100f, 26f), HexColor("#94A3B8"));

            GameObject starGo = new GameObject("StarFooter");
            starGo.transform.SetParent(card.transform, false);
            RectTransform stRt = starGo.AddComponent<RectTransform>();
            stRt.anchoredPosition = new Vector2(84f, -84f);
            stRt.sizeDelta = new Vector2(24f, 24f);
            Image stImg = starGo.AddComponent<Image>();
            stImg.sprite = hasStar ? ProceduralUISpriteFactory.GetStarFilledSprite() : ProceduralUISpriteFactory.GetStarEmptySprite();

            Button btn = card.GetComponent<Button>();
            if (btn == null) btn = card.AddComponent<Button>();
            btn.onClick.RemoveAllListeners();
            int capturedLvl = globalLvl;
            btn.onClick.AddListener(() => {
                if (ProgressionManager.IsLevelUnlocked(capturedLvl)) LoadLevel(capturedLvl);
                else
                {
                    UIBouncyButton bb = card.GetComponent<UIBouncyButton>();
                    if (bb != null) bb.TriggerLockedShake();
                    UIToastBanner.Show("🔒 Complete Level " + (capturedLvl - 1) + " first to unlock Level " + capturedLvl + "!", HexColor("#EF4444"));
                }
            });

            UIBouncyButton cardBounce = card.GetComponent<UIBouncyButton>();
            if (cardBounce == null) card.AddComponent<UIBouncyButton>();
        }
    }

    public void PlayNextInCurrentWorld()
    {
        int startLvl = (CurrentWorld - 1) * 10 + 1;
        int endLvl = CurrentWorld * 10;
        int targetLvl = startLvl;
        for (int l = startLvl; l <= endLvl; l++)
        {
            if (ProgressionManager.IsLevelUnlocked(l) && !ProgressionManager.IsLevelCleared(l)) { targetLvl = l; break; }
        }
        LoadLevel(targetLvl);
    }

    public void SwitchToRelativeWorld(int delta)
    {
        int nextW = Mathf.Clamp(CurrentWorld + delta, 1, 5);
        if (ProgressionManager.IsWorldUnlocked(nextW)) { CurrentWorld = nextW; GoToLevelSelect(CurrentWorld); }
        else { UIToastBanner.Show("🔒 World " + nextW + " is locked!", HexColor("#EF4444")); }
    }
#endregion

// ===========================================================================
// SECTION 8: IN-GAME SAFE-AREA HUD, BOSS BAR, OFF-SCREEN INDICATOR & VICTORY
// ===========================================================================
#region 8. In-Game Distance Progress Bar, Touch HUD Safe-Area & Victory Fanfare UI
    void BuildHudUI()
    {
        PanelHUD = new GameObject("Panel_HUD");
        PanelHUD.transform.SetParent(MainCanvas.transform, false);
        RectTransform rt = PanelHUD.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;

        // Top-Left Safe-Area Info Card
        GameObject infoCard = CreateCardPanel(PanelHUD.transform, new Vector2(360f, 118f), new Vector2(36f, -32f), new Color(0.08f, 0.12f, 0.28f, 0.88f), HexColor("#38BDF8"), 3f);
        RectTransform icRt = infoCard.GetComponent<RectTransform>();
        icRt.anchorMin = new Vector2(0f, 1f); icRt.anchorMax = new Vector2(0f, 1f); icRt.pivot = new Vector2(0f, 1f);
        HudLevelText = CreateUIText(infoCard.transform, "World 1 - Level 1", 25, TextAnchor.MiddleLeft, new Vector2(15f, 34f), new Vector2(320f, 36f), Color.white);
        HudStarText = CreateUIText(infoCard.transform, "★ STAR: 0 / 1", 20, TextAnchor.MiddleLeft, new Vector2(15f, 2f), new Vector2(320f, 32f), HexColor("#FDE047"));
        HudHealthText = CreateUIText(infoCard.transform, "♥ ♥ ♥", 22, TextAnchor.MiddleLeft, new Vector2(15f, -32f), new Vector2(320f, 34f), HexColor("#FB7185"));

        // Top-Right Safe-Area Pause Button
        GameObject pauseBtn = CreateUIButton(PanelHUD.transform, "PAUSE ⏸", new Vector2(175f, 58f), new Vector2(-36f, -32f), HexColor("#EF4444"), () => PauseGame(), 22);
        RectTransform pRt = pauseBtn.GetComponent<RectTransform>();
        pRt.anchorMin = new Vector2(1f, 1f); pRt.anchorMax = new Vector2(1f, 1f); pRt.pivot = new Vector2(1f, 1f);

        // Top-Center Distance Progress Bar
        ProgressBarRoot = new GameObject("LevelDistanceProgressBar");
        ProgressBarRoot.transform.SetParent(PanelHUD.transform, false);
        RectTransform pbrt = ProgressBarRoot.AddComponent<RectTransform>();
        pbrt.anchorMin = new Vector2(0.5f, 1f); pbrt.anchorMax = new Vector2(0.5f, 1f); pbrt.pivot = new Vector2(0.5f, 1f);
        pbrt.anchoredPosition = new Vector2(0f, -34f);
        pbrt.sizeDelta = new Vector2(440f, 46f);

        Image trackImg = ProgressBarRoot.AddComponent<Image>();
        trackImg.sprite = ProceduralUISpriteFactory.GetProgressTrackSprite();
        trackImg.type = Image.Type.Sliced;
        trackImg.color = new Color(0.06f, 0.09f, 0.16f, 0.72f);

        GameObject fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(ProgressBarRoot.transform, false);
        RectTransform fillRt = fillGo.AddComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one; fillRt.pivot = new Vector2(0f, 0.5f);
        fillRt.sizeDelta = new Vector2(-8f, -8f);
        ProgressBarFillImage = fillGo.AddComponent<Image>();
        ProgressBarFillImage.sprite = ProceduralUISpriteFactory.GetProgressFillSprite();
        ProgressBarFillImage.type = Image.Type.Filled;
        ProgressBarFillImage.fillMethod = Image.FillMethod.Horizontal;
        ProgressBarFillImage.fillAmount = 0f;

        GameObject avatarGo = new GameObject("AvatarPin");
        avatarGo.transform.SetParent(ProgressBarRoot.transform, false);
        ProgressBarAvatarPin = avatarGo.AddComponent<RectTransform>();
        ProgressBarAvatarPin.sizeDelta = new Vector2(26f, 26f);
        ProgressBarAvatarPin.anchoredPosition = new Vector2(-210f, 0f);
        ProgressBarAvatarPinImg = avatarGo.AddComponent<Image>();
        ProgressBarAvatarPinImg.sprite = ProceduralUISpriteFactory.GetCircleBadgeSprite();
        ProgressBarAvatarPinImg.color = HexColor("#2563EB");

        GameObject starPinGo = new GameObject("StarPin");
        starPinGo.transform.SetParent(ProgressBarRoot.transform, false);
        ProgressBarStarPin = starPinGo.AddComponent<RectTransform>();
        ProgressBarStarPin.sizeDelta = new Vector2(18f, 18f);
        ProgressBarStarPin.anchoredPosition = new Vector2(60f, 0f);
        ProgressBarStarPinImg = starPinGo.AddComponent<Image>();
        ProgressBarStarPinImg.sprite = ProceduralUISpriteFactory.GetStarEmptySprite();

        GameObject mbPinGo = new GameObject("MiniBerryPin");
        mbPinGo.transform.SetParent(ProgressBarRoot.transform, false);
        ProgressBarMiniBerryPin = mbPinGo.AddComponent<RectTransform>();
        ProgressBarMiniBerryPin.sizeDelta = new Vector2(24f, 24f);
        ProgressBarMiniBerryPin.anchoredPosition = new Vector2(210f, 0f);
        ProgressBarMiniBerryPinImg = mbPinGo.AddComponent<Image>();
        ProgressBarMiniBerryPinImg.sprite = ProceduralUISpriteFactory.GetCircleBadgeSprite();
        ProgressBarMiniBerryPinImg.color = HexColor("#F472B6");

        ProgressBarReadoutText = CreateUIText(ProgressBarRoot.transform, "LEVEL 1 (SHORT) • 0m / 62m (0%)", 15, TextAnchor.MiddleCenter, new Vector2(0f, -32f), new Vector2(440f, 26f), Color.white);

        // Sleek Frosted-Glass Boss Health Bar (620px Top-Center with Chip-Damage Trailing Bar & Phase Badge)
        BossHealthContainer = CreateCardPanel(PanelHUD.transform, new Vector2(620f, 88f), new Vector2(0f, -32f), new Color(0.06f, 0.08f, 0.16f, 0.94f), HexColor("#EF4444"), 3.5f);
        RectTransform bhRt = BossHealthContainer.GetComponent<RectTransform>();
        bhRt.anchorMin = new Vector2(0.5f, 1f); bhRt.anchorMax = new Vector2(0.5f, 1f); bhRt.pivot = new Vector2(0.5f, 1f);

        GameObject bCrown = new GameObject("BossCrownIcon");
        bCrown.transform.SetParent(BossHealthContainer.transform, false);
        RectTransform bcRt = bCrown.AddComponent<RectTransform>();
        bcRt.anchoredPosition = new Vector2(-265f, 18f);
        bcRt.sizeDelta = new Vector2(36f, 36f);
        Image bcImg = bCrown.AddComponent<Image>();
        bcImg.sprite = ProceduralUISpriteFactory.GetBossCrownSprite();

        BossHealthBarLabel = CreateUIText(BossHealthContainer.transform, "KING CHEESEBURG • 10 / 10 HP", 22, TextAnchor.MiddleLeft, new Vector2(-20f, 18f), new Vector2(420f, 34f), Color.white);
        BossPhaseBadgeText = CreateUIText(BossHealthContainer.transform, "PHASE 1", 16, TextAnchor.MiddleRight, new Vector2(210f, 18f), new Vector2(150f, 30f), HexColor("#FACC15"));

        // Bar Track inside BossHealthContainer
        GameObject barTrack = new GameObject("BossTrack");
        barTrack.transform.SetParent(BossHealthContainer.transform, false);
        RectTransform btRt = barTrack.AddComponent<RectTransform>();
        btRt.anchoredPosition = new Vector2(0f, -18f);
        btRt.sizeDelta = new Vector2(580f, 28f);
        Image btImg = barTrack.AddComponent<Image>();
        btImg.sprite = ProceduralUISpriteFactory.GetProgressTrackSprite();
        btImg.type = Image.Type.Sliced;
        btImg.color = new Color(0.15f, 0.05f, 0.08f, 0.95f);

        // Delayed White Chip-Damage Trail
        GameObject bChipGo = new GameObject("ChipFill");
        bChipGo.transform.SetParent(barTrack.transform, false);
        BossChipDamageFill = bChipGo.AddComponent<RectTransform>();
        BossChipDamageFill.anchorMin = Vector2.zero; BossChipDamageFill.anchorMax = Vector2.one; BossChipDamageFill.pivot = new Vector2(0f, 0.5f);
        BossChipDamageFill.sizeDelta = new Vector2(-6f, -6f);
        Image chipImg = bChipGo.AddComponent<Image>();
        chipImg.sprite = ProceduralUISpriteFactory.GetGlossyButtonSprite();
        chipImg.type = Image.Type.Sliced;
        chipImg.color = new Color(1f, 0.95f, 0.85f, 0.85f);

        GameObject bFillGo = new GameObject("HealthFill");
        bFillGo.transform.SetParent(barTrack.transform, false);
        BossHealthBarFill = bFillGo.AddComponent<RectTransform>();
        BossHealthBarFill.anchorMin = Vector2.zero; BossHealthBarFill.anchorMax = Vector2.one; BossHealthBarFill.pivot = new Vector2(0f, 0.5f);
        BossHealthBarFill.sizeDelta = new Vector2(-6f, -6f);
        BossHealthFillImg = bFillGo.AddComponent<Image>();
        BossHealthFillImg.sprite = ProceduralUISpriteFactory.GetGlossyButtonSprite();
        BossHealthFillImg.type = Image.Type.Sliced;
        BossHealthFillImg.color = HexColor("#EF4444");

        BossHealthContainer.SetActive(false);

        // Off-screen Chasing Boss Direction Indicator
        GameObject offGo = new GameObject("OffscreenBossIndicator");
        offGo.transform.SetParent(PanelHUD.transform, false);
        OffscreenBossIndicator = offGo.AddComponent<RectTransform>();
        OffscreenBossIndicator.sizeDelta = new Vector2(54f, 54f);
        Image offImg = offGo.AddComponent<Image>();
        offImg.sprite = ProceduralUISpriteFactory.GetChevronArrowSprite();
        offImg.color = HexColor("#EF4444");

        GameObject offCrown = new GameObject("Crown");
        offCrown.transform.SetParent(offGo.transform, false);
        RectTransform ocRt = offCrown.AddComponent<RectTransform>();
        ocRt.sizeDelta = new Vector2(24f, 24f);
        ocRt.anchoredPosition = new Vector2(-6f, 0f);
        Image ocImg = offCrown.AddComponent<Image>();
        ocImg.sprite = ProceduralUISpriteFactory.GetBossCrownSprite();
        OffscreenBossIndicator.gameObject.SetActive(false);
    }

    void BuildVictoryUI()
    {
        PanelVictory = new GameObject("Panel_Victory");
        PanelVictory.transform.SetParent(MainCanvas.transform, false);
        RectTransform rt = PanelVictory.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;

        Image bg = PanelVictory.AddComponent<Image>();
        bg.color = new Color(0.03f, 0.06f, 0.16f, 0.88f);

        GameObject card = CreateCardPanel(PanelVictory.transform, new Vector2(760f, 520f), Vector2.zero, new Color(0.08f, 0.12f, 0.26f, 0.96f), HexColor("#22C55E"), 4f);

        VictoryTitleText = CreateUIText(card.transform, "LEVEL 1 COMPLETE!", 44, TextAnchor.MiddleCenter, new Vector2(0f, 210f), new Vector2(700f, 55f), HexColor("#4ADE80"));
        VictoryMiniBerryText = CreateUIText(card.transform, "Rescued Pip!", 24, TextAnchor.MiddleCenter, new Vector2(0f, 165f), new Vector2(700f, 35f), Color.white);

        GameObject statsRow = new GameObject("StatsRow");
        statsRow.transform.SetParent(card.transform, false);
        RectTransform srRt = statsRow.AddComponent<RectTransform>();
        srRt.anchoredPosition = new Vector2(0f, 110f);
        srRt.sizeDelta = new Vector2(680f, 50f);

        VictoryTimeText = CreateUIText(statsRow.transform, "⏱ TIME: 0:35", 24, TextAnchor.MiddleCenter, new Vector2(-120f, 0f), new Vector2(260f, 45f), HexColor("#38BDF8"));

        GameObject starBox = new GameObject("StarBox");
        starBox.transform.SetParent(statsRow.transform, false);
        RectTransform sbRt = starBox.AddComponent<RectTransform>();
        sbRt.anchoredPosition = new Vector2(160f, 0f);
        sbRt.sizeDelta = new Vector2(240f, 45f);

        GameObject starImgGo = new GameObject("StarImg");
        starImgGo.transform.SetParent(starBox.transform, false);
        RectTransform siRt = starImgGo.AddComponent<RectTransform>();
        siRt.anchoredPosition = new Vector2(-60f, 0f);
        siRt.sizeDelta = new Vector2(42f, 42f);
        VictoryStarImage = starImgGo.AddComponent<Image>();
        VictoryStarImage.sprite = ProceduralUISpriteFactory.GetStarFilledSprite();

        VictoryStarText = CreateUIText(starBox.transform, "★ GOT!", 22, TextAnchor.MiddleLeft, new Vector2(15f, 0f), new Vector2(120f, 40f), HexColor("#FDE047"));

        VictoryUnlockBanner = CreateCardPanel(card.transform, new Vector2(640f, 75f), new Vector2(0f, 25f), new Color(0.96f, 0.62f, 0.04f, 0.95f), HexColor("#FBBF24"), 2.5f);
        VictoryUnlockTitle = CreateUIText(VictoryUnlockBanner.transform, "LEVEL 2 UNLOCKED!", 26, TextAnchor.UpperCenter, new Vector2(0f, 12f), new Vector2(580f, 35f), Color.white);
        VictoryUnlockSubtext = CreateUIText(VictoryUnlockBanner.transform, "Next: SHORT • ~69m", 18, TextAnchor.LowerCenter, new Vector2(0f, -12f), new Vector2(580f, 28f), HexColor("#FEF08A"));

        VictoryWorldBanner = CreateCardPanel(card.transform, new Vector2(640f, 50f), new Vector2(0f, -45f), new Color(0.55f, 0.25f, 0.95f, 0.95f), HexColor("#C084FC"), 2.5f);
        VictoryWorldTitle = CreateUIText(VictoryWorldBanner.transform, "WORLD 2 UNLOCKED!", 22, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(600f, 42f), Color.white);
        VictoryWorldBanner.SetActive(false);

        VictorySkinBanner = CreateCardPanel(card.transform, new Vector2(640f, 50f), new Vector2(0f, -100f), new Color(0.06f, 0.72f, 0.65f, 0.95f), HexColor("#00FFFF"), 2.5f);
        VictorySkinTitle = CreateUIText(VictorySkinBanner.transform, "🏆 GOLDEN BERRY SKIN UNLOCKED!", 20, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(600f, 42f), Color.white);
        VictorySkinBanner.SetActive(false);

        CreateUIButton(card.transform, "NEXT LEVEL ▶", new Vector2(230f, 62f), new Vector2(-220f, -195f), HexColor("#22C55E"), () => PlayNextLevel(), 22);
        CreateUIButton(card.transform, "REPLAY ↻", new Vector2(210f, 62f), new Vector2(10f, -195f), HexColor("#F59E0B"), () => RestartCurrentLevel(), 22);
        CreateUIButton(card.transform, "LEVEL SELECT", new Vector2(220f, 62f), new Vector2(235f, -195f), HexColor("#38BDF8"), () => GoToLevelSelect(CurrentWorld), 22);

        PanelVictory.SetActive(false);
    }

    public void ShowVictoryOverlay(int completedLevel, float clearTime, bool starCollected)
    {
        HideAllPanels();
        PanelVictory.SetActive(true);
        CurrentState = GameState.Playing;
        PlayBGM(1.0f);
        PlaySFX(SFXType.VictoryFanfare);

        VictoryTitleText.text = "LEVEL " + completedLevel + " COMPLETE!";
        VictoryMiniBerryText.text = "Rescued " + BerryNames[completedLevel - 1] + "!";

        int mm = (int)clearTime / 60;
        int ss = (int)clearTime % 60;
        VictoryTimeText.text = string.Format("⏱ TIME: {0}:{1:00}", mm, ss);

        VictoryStarImage.sprite = starCollected ? ProceduralUISpriteFactory.GetStarFilledSprite() : ProceduralUISpriteFactory.GetStarEmptySprite();
        VictoryStarText.text = starCollected ? "★ GOT!" : "☆ MISSED";

        if (starCollected)
        {
            PlaySFX(SFXType.StarDing);
            if (ParticlePool != null) ParticlePool.SpawnConfettiBurst(MainCam.transform.position + MainCam.transform.forward * 4f, 30);
        }

        if (ProgressionManager.NewlyUnlockedLevel > 0)
        {
            VictoryUnlockBanner.SetActive(true);
            int nextLvl = ProgressionManager.NewlyUnlockedLevel;
            LevelLengthSpec nextSpec = LevelLengthCalculator.GetSpec(nextLvl);
            VictoryUnlockTitle.text = "LEVEL " + nextLvl + " UNLOCKED!";
            VictoryUnlockSubtext.text = "Next: " + nextSpec.LengthTierLabel + " • ~" + ((int)nextSpec.TargetCourseMeters) + "m";
            PlaySFX(SFXType.UnlockFanfare);
        }
        else VictoryUnlockBanner.SetActive(false);

        if (ProgressionManager.NewlyUnlockedWorld > 0)
        {
            VictoryWorldBanner.SetActive(true);
            int newW = ProgressionManager.NewlyUnlockedWorld;
            string[] wNames = new string[] { "Salad Suburbs", "Deep Fry Docks", "Sundae Summit", "Grill Factory", "Cosmic Drive-Thru" };
            VictoryWorldTitle.text = "WORLD " + newW + ": " + wNames[newW - 1].ToUpper() + " UNLOCKED!";
        }
        else VictoryWorldBanner.SetActive(false);

        bool diamondUnlockedNow = starCollected && ProgressionManager.GetTotalStars() >= 50;
        if (diamondUnlockedNow)
        {
            VictorySkinBanner.SetActive(true);
            VictorySkinTitle.text = "💎 FLYING DIAMOND BIRD SKIN UNLOCKED (50/50 STARS)!";
        }
        else if (completedLevel == 50)
        {
            VictorySkinBanner.SetActive(true);
            VictorySkinTitle.text = "🏆 GOLDEN BERRY SKIN UNLOCKED!";
        }
        else VictorySkinBanner.SetActive(false);
    }

    void BuildPauseUI()
    {
        PanelPause = new GameObject("Panel_Pause");
        PanelPause.transform.SetParent(MainCanvas.transform, false);
        RectTransform rt = PanelPause.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
        Image bg = PanelPause.AddComponent<Image>();
        bg.color = new Color(0.03f, 0.06f, 0.18f, 0.90f);

        GameObject card = CreateCardPanel(PanelPause.transform, new Vector2(500f, 540f), Vector2.zero, new Color(0.08f, 0.12f, 0.28f, 0.96f), HexColor("#38BDF8"), 4f);
        CreateUIText(card.transform, "GAME PAUSED", 56, TextAnchor.MiddleCenter, new Vector2(0f, 195f), new Vector2(460f, 80f), HexColor("#38BDF8"));

        CreateUIButton(card.transform, "RESUME", new Vector2(340f, 72f), new Vector2(0f, 90f), HexColor("#10B981"), () => ResumeGame(), 28);
        CreateUIButton(card.transform, "RESTART LEVEL", new Vector2(340f, 72f), new Vector2(0f, 0f), HexColor("#F59E0B"), () => RestartCurrentLevel(), 26);
        CreateUIButton(card.transform, "SETTINGS", new Vector2(340f, 72f), new Vector2(0f, -90f), HexColor("#8B5CF6"), () => OpenSettings(), 26);
        CreateUIButton(card.transform, "QUIT TO MENU", new Vector2(340f, 72f), new Vector2(0f, -180f), HexColor("#EF4444"), () => GoToMainMenu(), 26);
    }

    void BuildHowToPlayUI()
    {
        PanelHowToPlay = new GameObject("Panel_HowToPlay");
        PanelHowToPlay.transform.SetParent(MainCanvas.transform, false);
        RectTransform rt = PanelHowToPlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
        Image bg = PanelHowToPlay.AddComponent<Image>();
        bg.color = new Color(0.06f, 0.09f, 0.16f, 0.75f);

        GameObject card = CreateCardPanel(PanelHowToPlay.transform, new Vector2(1300f, 780f), new Vector2(0f, 20f), new Color(0.08f, 0.12f, 0.28f, 0.96f), HexColor("#38BDF8"), 4f);
        CreateUIText(card.transform, "HOW TO PLAY", 64, TextAnchor.MiddleCenter, new Vector2(0f, 320f), new Vector2(800f, 90f), HexColor("#38BDF8"));

        string helpText =
            "★ CONTROLS (MAC / PC):\n" +
            "  • MOVE: W, A, S, D or Arrow Keys\n" +
            "  • JUMP: Spacebar (Press again in mid-air to DOUBLE JUMP with a 360° flip!)\n" +
            "  • SHOOT: F, E, or Left Mouse Click (Fires glowing blue Juice Bullets!)\n\n" +
            "★ CONTROLS (iPAD / TOUCH):\n" +
            "  • MOVE: Smooth on-screen circular D-pad (Bottom-Left)\n" +
            "  • JUMP & SHOOT: Big Green 🦘 and Red 🎯 thumb buttons (Bottom-Right)\n\n" +
            "★ RELENTLESS BOSS CHASES & GTA-6 PBR TERRAIN:\n" +
            "  • Every World Boss actively tracks & hunts Berry Dad across the 40x40 Stadium!\n" +
            "  • Bait Boss charges into perimeter walls to dizzy-stagger them for bonus hits!\n" +
            "  • Collect all 50 Hidden Stars to unlock the FLYING DIAMOND BIRD SKIN!\n" +
            "  • Defeat all 5 World Bosses to rescue all 50 Mini-Berries and unlock the GOLDEN BERRY SKIN!";

        Text body = CreateUIText(card.transform, helpText, 24, TextAnchor.UpperLeft, new Vector2(0f, 30f), new Vector2(1180f, 480f), Color.white, false);
        body.lineSpacing = 1.25f;

        CreateUIButton(card.transform, "LET'S BOUNCE!", new Vector2(280f, 68f), new Vector2(0f, -310f), HexColor("#10B981"), () => GoToMainMenu(), 28);
    }

    void BuildSettingsUI()
    {
        PanelSettings = new GameObject("Panel_Settings");
        PanelSettings.transform.SetParent(MainCanvas.transform, false);
        RectTransform rt = PanelSettings.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
        Image bg = PanelSettings.AddComponent<Image>();
        bg.color = new Color(0.06f, 0.09f, 0.16f, 0.75f);

        CreateUIText(PanelSettings.transform, "SETTINGS, GRAPHICS & SKINS", 56, TextAnchor.MiddleCenter, new Vector2(0f, 415f), new Vector2(960f, 80f), HexColor("#38BDF8"));

        CreateUIButton(PanelSettings.transform, "SOUND: ON / OFF", new Vector2(320f, 62f), new Vector2(-380f, 305f), HexColor("#0284C7"), () => {
            SoundEnabled = !SoundEnabled;
            SaveProgression();
            if (MusicSource != null) MusicSource.mute = !SoundEnabled;
            if (SFXSource != null) SFXSource.mute = !SoundEnabled;
            UIToastBanner.Show(SoundEnabled ? "Sound Enabled" : "Sound Muted", HexColor("#0284C7"));
        }, 20);

        string gfxLabel = (CurrentQualityTier == GraphicsQualityTier.UltraGTA6) ? "GRAPHICS: ULTRA (GTA-6)" :
                          (CurrentQualityTier == GraphicsQualityTier.High) ? "GRAPHICS: HIGH" : "GRAPHICS: 60 FPS MOBILE";
        GameObject gfxBtnGo = CreateUIButton(PanelSettings.transform, gfxLabel, new Vector2(360f, 62f), new Vector2(0f, 305f), HexColor("#10B981"), () => CycleGraphicsQualityTier(), 19);
        m_GraphicsBtnText = gfxBtnGo.GetComponentInChildren<Text>();

        string touchLabel = (TouchModeSetting == 0) ? "TOUCH D-PAD: AUTO" : (TouchModeSetting == 1 ? "TOUCH D-PAD: ON" : "TOUCH D-PAD: OFF");
        GameObject touchBtnGo = CreateUIButton(PanelSettings.transform, touchLabel, new Vector2(320f, 62f), new Vector2(380f, 305f), HexColor("#6366F1"), () => CycleTouchControlsSetting(), 19);
        m_TouchBtnText = touchBtnGo.GetComponentInChildren<Text>();

        CreateUIText(PanelSettings.transform, "CHOOSE BERRY DAD'S SKIN:", 32, TextAnchor.MiddleCenter, new Vector2(0f, 215f), new Vector2(700f, 48f), HexColor("#FBBF24"));

        SkinButtons.Clear();
        SkinButtonTexts.Clear();
        string[] skinNames = new string[] {
            "Normal Berry Dad\n(Royal Blue & Black)",
            "Golden Berry Skin\n(Beat All 50 Levels)",
            "Flying Diamond Bird\n(Collect All 50 Stars)"
        };

        for (int i = 0; i < 3; i++)
        {
            int skinIdx = i;
            float xPos = (i - 1) * 440f;
            GameObject skinCard = CreateCardPanel(PanelSettings.transform, new Vector2(390f, 225f), new Vector2(xPos, 45f), new Color(0.10f, 0.15f, 0.32f, 0.95f), (skinIdx == 1) ? HexColor("#F59E0B") : ((skinIdx == 2) ? HexColor("#06B6D4") : HexColor("#3B82F6")), 3f);
            CreateUIText(skinCard.transform, skinNames[i], 22, TextAnchor.MiddleCenter, new Vector2(0f, 46f), new Vector2(360f, 88f), Color.white);

            GameObject btnGo = CreateUIButton(skinCard.transform, "EQUIP", new Vector2(230f, 56f), new Vector2(0f, -50f), HexColor("#10B981"), () => EquipSkin(skinIdx), 22);
            SkinButtons.Add(btnGo.GetComponent<Button>());
            SkinButtonTexts.Add(btnGo.GetComponentInChildren<Text>());
        }

        GameObject resetBtnGo = CreateUIButton(PanelSettings.transform, "RESET PROGRESS (LOCK LEVELS)", new Vector2(380f, 55f), new Vector2(-220f, -155f), HexColor("#EF4444"), () => {
            m_ResetTapCount++;
            if (m_ResetTapCount == 1)
            {
                if (m_ResetBtnText != null) m_ResetBtnText.text = "TAP AGAIN TO RESET?";
            }
            else if (m_ResetTapCount >= 2)
            {
                ProgressionManager.ResetAllProgress();
                LoadProgression();
                RefreshSettingsSkinUI();
                FocusWorld(1);
                UIToastBanner.Show("Progress reset! All levels 2-50 locked.", HexColor("#EF4444"));
                m_ResetTapCount = 0;
                if (m_ResetBtnText != null) m_ResetBtnText.text = "RESET PROGRESS (LOCK LEVELS)";
            }
        }, 20);
        m_ResetBtnText = resetBtnGo.GetComponentInChildren<Text>();

        CreateUIButton(PanelSettings.transform, "UNLOCK EVERYTHING (TEST MODE)", new Vector2(380f, 55f), new Vector2(220f, -155f), HexColor("#8B5CF6"), () => {
            ProgressionManager.UnlockEverythingTestMode();
            LoadProgression();
            RefreshSettingsSkinUI();
            FocusWorld(FocusedWorldIndex);
            UIToastBanner.Show("⚡ TEST MODE ACTIVE: All 50 Levels, Stars & Skins Unlocked!", HexColor("#A855F7"));
        }, 19);

        CreateUIButton(PanelSettings.transform, "BACK", new Vector2(250f, 65f), new Vector2(0f, -260f), HexColor("#64748B"), () => {
            if (CurrentState == GameState.Paused)
            {
                PanelSettings.SetActive(false);
                PanelPause.SetActive(true);
            }
            else
            {
                GoToMainMenu();
            }
        }, 26);

        RefreshSettingsSkinUI();
    }

    void RefreshSettingsSkinUI()
    {
        if (SkinButtons.Count < 3) return;
        bool goldenUnlocked = ProgressionManager.IsLevelCleared(50) || PlayerPrefs.GetInt("BerryBounce_AllLevelsBeaten", 0) == 1;
        bool birdUnlocked = ProgressionManager.GetTotalStars() >= 50;

        if (EquippedSkin == 0) { SkinButtonTexts[0].text = "EQUIPPED ✔"; SkinButtons[0].interactable = false; }
        else { SkinButtonTexts[0].text = "EQUIP"; SkinButtons[0].interactable = true; }

        if (EquippedSkin == 1) { SkinButtonTexts[1].text = "EQUIPPED ✔"; SkinButtons[1].interactable = false; }
        else if (goldenUnlocked) { SkinButtonTexts[1].text = "EQUIP"; SkinButtons[1].interactable = true; }
        else { SkinButtonTexts[1].text = "LOCKED 🔒"; SkinButtons[1].interactable = false; }

        if (EquippedSkin == 2) { SkinButtonTexts[2].text = "EQUIPPED ✔"; SkinButtons[2].interactable = false; }
        else if (birdUnlocked) { SkinButtonTexts[2].text = "EQUIP"; SkinButtons[2].interactable = true; }
        else { SkinButtonTexts[2].text = "LOCKED 🔒"; SkinButtons[2].interactable = false; }
    }

    public void EquipSkin(int skinIdx)
    {
        EquippedSkin = skinIdx;
        SaveProgression();
        RefreshSettingsSkinUI();
        RefreshDioramaPlayerSkin();
        if (Player != null) Player.RebuildCharacterSkin();
    }

    void BuildDialogueUI()
    {
        PanelDialogue = new GameObject("Panel_Dialogue");
        PanelDialogue.transform.SetParent(MainCanvas.transform, false);
        RectTransform rt = PanelDialogue.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;

        Image bg = PanelDialogue.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.45f);

        GameObject box = CreateCardPanel(PanelDialogue.transform, new Vector2(1040f, 220f), new Vector2(0f, -310f), new Color(0.06f, 0.09f, 0.18f, 0.95f), HexColor("#38BDF8"), 4f);
        DialogueSpeakerText = CreateUIText(box.transform, "Speaker", 32, TextAnchor.UpperLeft, new Vector2(40f, 65f), new Vector2(600f, 45f), HexColor("#FACC15"));
        DialogueBodyText = CreateUIText(box.transform, "Dialogue text here...", 24, TextAnchor.UpperLeft, new Vector2(40f, -10f), new Vector2(880f, 110f), Color.white, false);
        DialogueBodyText.lineSpacing = 1.25f;

        DialogueNextIndicator = CreateUIText(box.transform, "TAP / SPACE ▶", 18, TextAnchor.LowerRight, new Vector2(420f, -70f), new Vector2(180f, 30f), HexColor("#38BDF8")).gameObject;

        Button nextBtn = PanelDialogue.AddComponent<Button>();
        nextBtn.onClick.AddListener(() => AdvanceDialogue());
        PanelDialogue.SetActive(false);
    }

    void BuildEndingCelebrationUI()
    {
        PanelEndingCelebration = new GameObject("Panel_EndingCelebration");
        PanelEndingCelebration.transform.SetParent(MainCanvas.transform, false);
        RectTransform rt = PanelEndingCelebration.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;

        Image bg = PanelEndingCelebration.AddComponent<Image>();
        bg.color = new Color(0.03f, 0.05f, 0.12f, 0.95f);

        GameObject card = CreateCardPanel(PanelEndingCelebration.transform, new Vector2(1100f, 680f), Vector2.zero, new Color(0.06f, 0.10f, 0.22f, 0.98f), HexColor("#FFD700"), 4.5f);
        CreateUIText(card.transform, "BERRY BOUNCE 3D", 56, TextAnchor.MiddleCenter, new Vector2(0f, 260f), new Vector2(800f, 75f), HexColor("#38BDF8"));
        CreateUIText(card.transform, "Made by BerryBounce3D.xyz", 26, TextAnchor.MiddleCenter, new Vector2(0f, 205f), new Vector2(800f, 40f), HexColor("#FBBF24"));
        CreateUIText(card.transform, "ALL 50 MINI-BERRIES RESCUED!", 34, TextAnchor.MiddleCenter, new Vector2(0f, 155f), new Vector2(800f, 50f), HexColor("#FFD700"));

        string endMsg =
            "Congratulations, Berry Dad! You fought through all five worlds,\n" +
            "defeated King Cheeseburg, Captain Crispy Fry, Frosty Cone Monster,\n" +
            "Sir Sizzle Hotdog, and the Galactic Mega-Pizza UFO!\n\n" +
            "Every single one of your 50 little mini-berries is back home safe and sound!\n\n" +
            "★ GOLDEN BERRY SKIN UNLOCKED! ★\n" +
            "Thank you for playing Berry Bounce 3D!";

        Text body = CreateUIText(card.transform, endMsg, 24, TextAnchor.MiddleCenter, new Vector2(0f, -15f), new Vector2(1000f, 240f), Color.white, false);
        body.lineSpacing = 1.3f;

        CreateUIButton(card.transform, "RETURN TO MAIN MENU", new Vector2(360f, 72f), new Vector2(0f, -240f), HexColor("#10B981"), () => GoToMainMenu(), 24);
        PanelEndingCelebration.SetActive(false);
    }

    void BuildTouchControlsUI()
    {
        PanelTouchControls = new GameObject("Panel_TouchControls");
        PanelTouchControls.transform.SetParent(MainCanvas.transform, false);
        RectTransform rt = PanelTouchControls.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;

        GameObject joyGo = new GameObject("TouchJoystick");
        joyGo.transform.SetParent(PanelTouchControls.transform, false);
        RectTransform jrt = joyGo.AddComponent<RectTransform>();
        jrt.anchorMin = new Vector2(0f, 0f); jrt.anchorMax = new Vector2(0f, 0f); jrt.pivot = new Vector2(0f, 0f);
        jrt.anchoredPosition = new Vector2(44f, 44f);
        jrt.sizeDelta = new Vector2(180f, 180f);
        Image jImg = joyGo.AddComponent<Image>();
        jImg.sprite = ProceduralUISpriteFactory.GetCircleBadgeSprite();
        jImg.color = new Color(1f, 1f, 1f, 0.25f);

        GameObject knobGo = new GameObject("JoystickKnob");
        knobGo.transform.SetParent(joyGo.transform, false);
        RectTransform kRt = knobGo.AddComponent<RectTransform>();
        kRt.sizeDelta = new Vector2(76f, 76f);
        Image kImg = knobGo.AddComponent<Image>();
        kImg.sprite = ProceduralUISpriteFactory.GetCircleBadgeSprite();
        kImg.color = new Color(0.95f, 0.98f, 1f, 0.72f);

        TouchDPadJoystick dpad = joyGo.AddComponent<TouchDPadJoystick>();
        dpad.Knob = kRt;
        dpad.MaxRadius = 62f;

        GameObject jmpBtn = CreateUIButton(PanelTouchControls.transform, "JUMP 🦘", new Vector2(142f, 142f), new Vector2(-202f, 44f), HexColor("#10B981"), null, 24);
        UIInstantPressButton jmpPress = jmpBtn.AddComponent<UIInstantPressButton>();
        jmpPress.OnPressDown = () => { if (Player != null) Player.TriggerJump(); };
        RectTransform jmpRt = jmpBtn.GetComponent<RectTransform>();
        jmpRt.anchorMin = new Vector2(1f, 0f); jmpRt.anchorMax = new Vector2(1f, 0f); jmpRt.pivot = new Vector2(1f, 0f);

        GameObject shtBtn = CreateUIButton(PanelTouchControls.transform, "SHOOT 🎯", new Vector2(132f, 132f), new Vector2(-42f, 44f), HexColor("#EF4444"), null, 22);
        UIInstantPressButton shtPress = shtBtn.AddComponent<UIInstantPressButton>();
        shtPress.OnPressDown = () => { if (Player != null) Player.ShootJuiceBullet(); };
        RectTransform shtRt = shtBtn.GetComponent<RectTransform>();
        shtRt.anchorMin = new Vector2(1f, 0f); shtRt.anchorMax = new Vector2(1f, 0f); shtRt.pivot = new Vector2(1f, 0f);

        PanelTouchControls.SetActive(false);
    }

    public void GoToMainMenu()
    {
        Time.timeScale = 1f;
        InBossFight = false;
        BossAlive = false;
        if (StageRoot != null) { Destroy(StageRoot.gameObject); StageRoot = null; }
        if (Player != null) { Destroy(Player.gameObject); Player = null; }
        if (LightPool != null) { LightPool.SetBossAura(false, Vector3.zero, Color.white, 0f, 0f); LightPool.ClearEnvTargets(); }

        HideAllPanels();
        PanelMainMenu.SetActive(true);
        CurrentState = GameState.MainMenu;
        if (MainMenuStarCountText != null) MainMenuStarCountText.text = "Stars: " + ProgressionManager.GetTotalStars() + " / 50 ★";

        if (CamCtrl != null) CamCtrl.SetMenuCameraState(MenuCameraState.MainMenu);
        ShowMenuDioramaForWorld(1, false);
        PlayBGM(1.0f);
    }

    public void GoToWorldSelect()
    {
        Time.timeScale = 1f;
        InBossFight = false;
        BossAlive = false;
        if (StageRoot != null) { Destroy(StageRoot.gameObject); StageRoot = null; }
        if (Player != null) { Destroy(Player.gameObject); Player = null; }

        HideAllPanels();
        PanelWorldSelect.SetActive(true);
        CurrentState = GameState.WorldSelect;
        FocusWorld(CurrentWorld);
    }

    public void GoToLevelSelect(int worldIndex)
    {
        Time.timeScale = 1f;
        InBossFight = false;
        BossAlive = false;
        if (StageRoot != null) { Destroy(StageRoot.gameObject); StageRoot = null; }
        if (Player != null) { Destroy(Player.gameObject); Player = null; }

        CurrentWorld = Mathf.Clamp(worldIndex, 1, 5);
        HideAllPanels();
        PanelLevelSelect.SetActive(true);
        CurrentState = GameState.LevelSelect;

        if (CamCtrl != null) CamCtrl.SetMenuCameraState(MenuCameraState.LevelSelect);
        ShowMenuDioramaForWorld(CurrentWorld, false);
        RefreshLevelSelectGrid();
    }

    public void OpenSettings()
    {
        HideAllPanels();
        PanelSettings.SetActive(true);
        CurrentState = GameState.Settings;
        if (CamCtrl != null) CamCtrl.SetMenuCameraState(MenuCameraState.Settings);
        RefreshSettingsSkinUI();
    }

    public void OpenHowToPlay()
    {
        HideAllPanels();
        PanelHowToPlay.SetActive(true);
        CurrentState = GameState.HowToPlay;
    }

    public void PauseGame()
    {
        if (CurrentState != GameState.Playing) return;
        Time.timeScale = 0f;
        PanelHUD.SetActive(false);
        if (PanelTouchControls != null) PanelTouchControls.SetActive(false);
        PanelPause.SetActive(true);
        CurrentState = GameState.Paused;
    }

    public void ResumeGame()
    {
        Time.timeScale = 1f;
        PanelPause.SetActive(false);
        PanelHUD.SetActive(true);
        CurrentState = GameState.Playing;
        UpdateTouchControlsVisibility();
    }

    public void RestartCurrentLevel()
    {
        Time.timeScale = 1f;
        LoadLevel(CurrentLevel);
    }

    public void PlayNextLevel()
    {
        Time.timeScale = 1f;
        if (CurrentLevel < 50) LoadLevel(CurrentLevel + 1);
        else GoToWorldSelect();
    }

    void HideAllPanels()
    {
        if (PanelMainMenu != null) PanelMainMenu.SetActive(false);
        if (PanelWorldSelect != null) PanelWorldSelect.SetActive(false);
        if (PanelLevelSelect != null) PanelLevelSelect.SetActive(false);
        if (PanelHUD != null) PanelHUD.SetActive(false);
        if (PanelVictory != null) PanelVictory.SetActive(false);
        if (PanelPause != null) PanelPause.SetActive(false);
        if (PanelHowToPlay != null) PanelHowToPlay.SetActive(false);
        if (PanelSettings != null) PanelSettings.SetActive(false);
        if (PanelDialogue != null) PanelDialogue.SetActive(false);
        if (PanelEndingCelebration != null) PanelEndingCelebration.SetActive(false);
        if (PanelTouchControls != null) PanelTouchControls.SetActive(false);
        if (OffscreenBossIndicator != null) OffscreenBossIndicator.gameObject.SetActive(false);
    }
#endregion

// ===========================================================================
// SECTION 7: LEVEL LOADING, GTA-6 LAND SCULPTING, COMBAT & BOSS ARENAS
// ===========================================================================
#region 7. Berry Dad, 3 Skins, 50 Unique Mini-Berries, 50 Dialogues & 5 Moving Bosses
    public void LoadLevel(int globalLevel)
    {
        if (!ProgressionManager.IsLevelUnlocked(globalLevel))
        {
            PlaySFX(SFXType.UILocked);
            UIToastBanner.Show("Complete Level " + (globalLevel - 1) + " first to unlock Level " + globalLevel + "!", HexColor("#EF4444"));
            return;
        }

        CurrentLevel = Mathf.Clamp(globalLevel, 1, 50);
        CurrentWorld = ((CurrentLevel - 1) / 10) + 1;
        LevelStartTime = Time.time;

        Time.timeScale = 1f;
        HideAllPanels();
        PanelHUD.SetActive(true);
        CurrentState = GameState.Playing;
        UpdateTouchControlsVisibility();

        if (MainMenuDioramaRoot != null)
        {
            Destroy(MainMenuDioramaRoot);
            MainMenuDioramaRoot = null;
            CurrentDioramaWorldIndex = -1;
        }
        if (StageRoot != null) Destroy(StageRoot.gameObject);
        if (WindSystem != null) WindSystem.ClearItems();
        if (LightPool != null)
        {
            LightPool.ClearEnvTargets();
            LightPool.SetBossAura(false, Vector3.zero, Color.white, 0f, 0f);
        }

        StageRoot = new GameObject("StageRoot").transform;
        ActiveEnemies.Clear();
        ActiveBullets.Clear();
        TouchMoveInput = Vector2.zero;

        LevelLengthSpec spec = LevelLengthCalculator.GetSpec(CurrentLevel);
        LevelFinishZ = spec.TargetCourseMeters;

        PlayerMaxHP = spec.IsBossLevel ? 5 : 3;
        PlayerHP = PlayerMaxHP;
        UpdatePlayerHealthHUD();

        HudLevelText.text = "World " + CurrentWorld + " - Level " + CurrentLevel;
        HudStarText.text = ProgressionManager.IsStarCollected(CurrentLevel) ? "★ STAR: 1 / 1" : "★ STAR: 0 / 1";

        ProgressBarRoot.SetActive(!spec.IsBossLevel);
        if (ProgressBarFillImage != null)
        {
            Color[] wColors = new Color[] { HexColor("#4ADE80"), HexColor("#FB923C"), HexColor("#38BDF8"), HexColor("#F87171"), HexColor("#C084FC") };
            ProgressBarFillImage.color = wColors[CurrentWorld - 1];
            ProgressBarFillImage.fillAmount = 0f;
        }

        ApplyWorldAtmosphere(CurrentWorld, false);
        SpawnWorldEnvironmentVistas(CurrentWorld, spec.IsBossLevel ? 100f : spec.TargetCourseMeters);
        PlayBGM(1.0f);

        if (spec.IsBossLevel)
        {
            BuildBossArena(CurrentWorld);
            StartBossFightLevel(CurrentLevel);
        }
        else
        {
            InBossFight = false;
            BossAlive = false;
            BossDialogueDone = false;
            BossHealthContainer.SetActive(false);
            ModularCourseBuilder.BuildLevel(this, CurrentLevel, CurrentWorld);
        }

        SpawnBerryDadPlayer();
    }

    public void UpdatePlayerHealthHUD()
    {
        if (HudHealthText == null) return;
        string s = "";
        for (int i = 0; i < PlayerMaxHP; i++)
            s += (i < PlayerHP) ? "♥ " : "♡ ";
        HudHealthText.text = s.TrimEnd();
    }

    public void DamagePlayer(int amount, Vector3 sourcePos)
    {
        if (Player == null || CurrentState != GameState.Playing) return;
        if (Player.IsInvulnerable()) return;

        PlayerHP -= amount;
        UpdatePlayerHealthHUD();
        m_DamageFlashAlpha = 0.72f;
        PlaySFX(SFXType.HurtFall);
        TriggerScreenShake(0.26f, 0.28f);

        if (PlayerHP <= 0)
        {
            RespawnPlayerAtCheckpoint();
        }
        else
        {
            Vector3 away = Player.transform.position - sourcePos;
            away.y = 0f;
            if (away.sqrMagnitude < 0.05f) away = -Player.transform.forward;
            away.Normalize();
            Player.ApplyKnockbackAndInvuln(away * 11f + Vector3.up * 8.5f, 1.25f);
        }
    }

    void SpawnBerryDadPlayer()
    {
        if (Player != null) Destroy(Player.gameObject);

        GameObject pGo = new GameObject("BerryDad_Player");
        pGo.transform.position = PlayerSpawnPoint;
        pGo.tag = "Player";

        Player = pGo.AddComponent<BerryDadController>();
        Player.EquippedSkin = EquippedSkin;
        Player.IsWorld5LowGravity = (CurrentWorld == 5);

        if (CamCtrl != null)
        {
            CamCtrl.IsOrbitingMenu = false;
            CamCtrl.Target = pGo.transform;
            CamCtrl.Offset = new Vector3(0f, 6.8f, -9.5f);
        }
    }

    public void RespawnPlayerAtCheckpoint()
    {
        PlaySFX(SFXType.HurtFall);
        m_DamageFlashAlpha = 0.85f;
        PlayerHP = PlayerMaxHP;
        UpdatePlayerHealthHUD();
        if (Player != null)
        {
            Player.transform.position = CheckpointPoint + Vector3.up * 1.5f;
            Player.ResetVelocity();
            Player.ApplyKnockbackAndInvuln(Vector3.zero, 1.0f);
        }
    }

    public void OnLevelFinished(int completedLevel)
    {
        float clearTime = Time.time - LevelStartTime;
        bool starCollected = ProgressionManager.IsStarCollected(completedLevel);

        ProgressionManager.CompleteLevel(completedLevel, clearTime, starCollected);
        UnlockedLevel = ProgressionManager.GetHighestUnlockedLevel();

        if (completedLevel >= 50) LaunchGrandFinaleEnding();
        else ShowVictoryOverlay(completedLevel, clearTime, starCollected);
    }

    void LaunchGrandFinaleEnding()
    {
        HideAllPanels();
        PanelEndingCelebration.SetActive(true);
        CurrentState = GameState.EndingCelebration;
        PlayBGM(1.0f);
        PlaySFX(SFXType.VictoryFanfare);
    }

    public void StartMiniBerryRescue(MiniBerry berry)
    {
        if (CurrentState == GameState.Dialogue) return;
        List<DialogueLine> lines;
        if (LevelDialogues.TryGetValue(berry.LevelNumber, out lines))
        {
            StartDialogueSequence(lines, () => OnLevelFinished(berry.LevelNumber));
        }
        else OnLevelFinished(berry.LevelNumber);
    }

    public void StartBossPreFightDialogue(int level, Action onComplete)
    {
        List<DialogueLine> lines;
        if (BossPreFightDialogues.TryGetValue(level, out lines)) StartDialogueSequence(lines, onComplete);
        else if (onComplete != null) onComplete();
    }

    void StartDialogueSequence(List<DialogueLine> lines, Action onComplete)
    {
        CurrentState = GameState.Dialogue;
        CurrentConversation = lines;
        CurrentDialogueIndex = 0;
        OnDialogueComplete = onComplete;
        PanelDialogue.SetActive(true);
        if (PanelTouchControls != null) PanelTouchControls.SetActive(false);
        DisplayCurrentDialogueLine();
    }

    void DisplayCurrentDialogueLine()
    {
        if (CurrentConversation == null || CurrentDialogueIndex >= CurrentConversation.Count)
        {
            EndDialogue();
            return;
        }
        DialogueLine line = CurrentConversation[CurrentDialogueIndex];
        DialogueSpeakerText.text = line.Speaker;
        DialogueSpeakerText.color = line.SpeakerColor;
        DialogueBodyText.text = line.Text;
        PlaySFX(SFXType.VoiceBlip);
    }

    public void AdvanceDialogue()
    {
        CurrentDialogueIndex++;
        if (CurrentDialogueIndex < CurrentConversation.Count) DisplayCurrentDialogueLine();
        else EndDialogue();
    }

    void EndDialogue()
    {
        PanelDialogue.SetActive(false);
        CurrentState = GameState.Playing;
        UpdateTouchControlsVisibility();
        if (OnDialogueComplete != null)
        {
            Action cb = OnDialogueComplete;
            OnDialogueComplete = null;
            cb();
        }
    }

    public void OnEnemyHitByBullet(EnemyController enemy, JuiceBullet bullet)
    {
        if (enemy != null)
        {
            if (ParticlePool != null) ParticlePool.SpawnJuiceSplashParticles(enemy.transform.position, 10);
            if (LightPool != null) LightPool.TriggerFlash(enemy.transform.position + Vector3.up, HexColor("#38BDF8"), 4f, 0.2f);
            enemy.Defeat();
        }
        if (bullet != null)
        {
            ActiveBullets.Remove(bullet);
            Destroy(bullet.gameObject);
        }
    }

    public void DamageBoss(int dmg, Vector3 hitPos, Vector3 hitDir)
    {
        if (!BossAlive || ActiveBoss == null) return;
        BossCurrentHP = Mathf.Max(0, BossCurrentHP - dmg);
        UpdateBossHealthUI();

        PlaySFX(SFXType.Hit);
        if (ParticlePool != null) ParticlePool.SpawnJuiceSplashParticles(hitPos, 12);
        if (LightPool != null) LightPool.TriggerFlash(hitPos, HexColor("#38BDF8"), 5.5f, 0.22f, 16f);
        DamagePopupFloater.Spawn(hitPos + Vector3.up * 1.4f, "-" + dmg, HexColor("#FDE047"));

        ActiveBoss.OnTookBulletDamage(dmg, hitDir, BossCurrentHP, BossMaxHP);
    }

    public void BuildStandardLevel(int level, int world)
    {
        ModularCourseBuilder.BuildLevel(this, level, world);
    }

    public void SpawnWorldEnvironmentVistas(int world, float courseLength)
    {
        // Animated Reflective Ocean / Lava Sea at y = -18f (visual sea below platforms)
        float midZ = courseLength * 0.45f;
        GameObject sea = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sea.name = "AnimatedAbyssSea";
        sea.transform.SetParent(StageRoot, false);
        sea.transform.position = new Vector3(0f, -18f, midZ);
        sea.transform.localScale = new Vector3(520f, 0.5f, courseLength + 460f);
        DestroyImmediate(sea.GetComponent<Collider>());
        PBRMaterialSuite seaSuite = ProceduralTextureFactory.GetOceanOrLavaPBRSuite(world);
        sea.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedPBRMaterial("EnvSea_W" + world, seaSuite, 36f, (courseLength + 460f) / 14f);
        var sa = sea.AddComponent<AbyssSeaAnimator>();
        sa.SeaRenderer = sea.GetComponent<Renderer>();
        sa.ScrollSpeed = (world == 4) ? new Vector2(0.015f, -0.025f) : new Vector2(0.03f, -0.05f);

        // World 1: Blazing Golden Hour Sun Disc + Sun God Rays + Lush Forest Meadow Islands
        if (world == 1)
        {
            GameObject sun = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sun.name = "HorizonSunDisc";
            sun.transform.SetParent(StageRoot, false);
            sun.transform.position = new Vector3(-90f, 75f, courseLength + 240f);
            sun.transform.localScale = Vector3.one * 34f;
            DestroyImmediate(sun.GetComponent<Collider>());
            sun.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("SunDiscMat", HexColor("#FEF08A"), null, 0f, 0f, HexColor("#FDE047") * 2.5f);

            // Flanking Non-Colliding Sculpted Meadow Tree Islands along the entire course!
            System.Random rndW1 = new System.Random(101);
            int groveCount = Mathf.Clamp((int)(courseLength / 16f) + 8, 12, 26);
            PBRMaterialSuite w1Top = ProceduralTextureFactory.GetWorldTerrainPBRSuite(1, false);
            PBRMaterialSuite w1Cliff = ProceduralTextureFactory.GetWorldTerrainPBRSuite(1, true);
            Material islTopMat = SafeMaterialFactory.GetCachedPBRMaterial("W1_ScenicTop_" + w1Top.AlbedoTex.width, w1Top, 1.0f, 1.0f);
            Material islCliffMat = SafeMaterialFactory.GetCachedPBRMaterial("W1_ScenicCliff_" + w1Cliff.AlbedoTex.width, w1Cliff, 1.0f, 1.0f);

            for (int i = 0; i < groveCount; i++)
            {
                float side = (i % 2 == 0) ? -1f : 1f;
                float distX = 21f + (float)rndW1.NextDouble() * 20f;
                float iz = -12f + i * ((courseLength + 52f) / Mathf.Max(1, groveCount - 1));
                float iy = -6.5f + (float)rndW1.NextDouble() * 10.5f;
                float iw = 6.5f + (float)rndW1.NextDouble() * 4.5f;
                float id = 6.5f + (float)rndW1.NextDouble() * 4.5f;

                GameObject isl = new GameObject("MeadowTreeIsland_" + i);
                isl.transform.SetParent(StageRoot, false);
                isl.transform.position = new Vector3(side * distX, iy, iz);

                GameObject cap = new GameObject("Cap");
                cap.transform.SetParent(isl.transform, false);
                cap.transform.localPosition = new Vector3(0f, 0.8f, 0f);
                cap.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshHelper.GetBeveledSlabMesh(iw, 0.45f, id, 0.18f);
                cap.AddComponent<MeshRenderer>().sharedMaterial = islTopMat;

                GameObject baseCliff = new GameObject("Base");
                baseCliff.transform.SetParent(isl.transform, false);
                baseCliff.transform.localPosition = new Vector3(0f, -0.7f, 0f);
                baseCliff.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshHelper.GetBeveledSlabMesh(iw * 0.82f, 2.6f, id * 0.82f, 0.22f);
                baseCliff.AddComponent<MeshRenderer>().sharedMaterial = islCliffMat;

                float topSurfaceY = 1.02f;
                int treeKind = i % 3;
                float tScale = 1.15f + (float)rndW1.NextDouble() * 0.45f;
                if (treeKind == 0)
                    BuildLushOakTree(isl.transform, new Vector3(-0.7f * side, topSurfaceY, 0.4f), tScale);
                else if (treeKind == 1)
                    BuildStylizedPineTree(isl.transform, new Vector3(-0.6f * side, topSurfaceY, -0.3f), tScale);
                else
                    BuildTropicalPalmTree(isl.transform, new Vector3(-0.5f * side, topSurfaceY, 0.2f), tScale);

                // Companion smaller tree + flowering bush on each meadow hill!
                if (i % 2 == 0)
                    BuildStylizedPineTree(isl.transform, new Vector3(1.4f * side, topSurfaceY, -1.2f), tScale * 0.78f);
                else
                    BuildLushOakTree(isl.transform, new Vector3(1.3f * side, topSurfaceY, 1.3f), tScale * 0.72f);

                BuildFloweringBerryBush(isl.transform, new Vector3(-1.4f * side, topSurfaceY, -1.5f), 0.95f);
                SpawnGrassTuft(isl.transform, new Vector3(1.1f * side, topSurfaceY, 1.2f));
            }

            if (CurrentQualityTier == GraphicsQualityTier.UltraGTA6)
            {
                int rayCount = Mathf.Clamp((int)(courseLength / 45f) + 2, 2, 7);
                for (int r = 0; r < rayCount; r++)
                {
                    GameObject ray = new GameObject("SunGodRay_" + r);
                    ray.transform.SetParent(StageRoot, false);
                    ray.transform.position = new Vector3(-22f + (r % 2) * 32f, 26f, 20f + r * 48f);
                    ray.transform.rotation = Quaternion.Euler(14f, 0f, -26f);
                    MeshFilter mfR = ray.AddComponent<MeshFilter>();
                    mfR.sharedMesh = LowPolyMeshHelper.GetVolumetricConeMesh(16, 1.5f, 8.5f, 38f);
                    MeshRenderer mrR = ray.AddComponent<MeshRenderer>();
                    mrR.sharedMaterial = SafeMaterialFactory.GetCachedVertexAlphaMaterial("EnvSunRayW1", new Color(1f, 0.88f, 0.58f, 0.14f), null, 3050);
                }
            }
        }
        // World 2: Vice-City Neon Waterfront Skyscraper Skyline
        else if (world == 2)
        {
            System.Random rnd = new System.Random(202);
            for (int b = 0; b < 18; b++)
            {
                float side = (b % 2 == 0) ? -1f : 1f;
                float bx = side * (95f + (float)rnd.NextDouble() * 45f);
                float bz = -20f + b * ((courseLength + 120f) / 18f);
                float bh = 28f + (float)rnd.NextDouble() * 48f;
                float bw = 12f + (float)rnd.NextDouble() * 14f;

                GameObject bld = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bld.name = "ViceSkyscraper_" + b;
                bld.transform.SetParent(StageRoot, false);
                bld.transform.position = new Vector3(bx, bh * 0.5f - 18f, bz);
                bld.transform.localScale = new Vector3(bw, bh, bw);
                DestroyImmediate(bld.GetComponent<Collider>());
                bld.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("SkyscraperMat", HexColor("#1E1B4B"), null, 0.6f);

                // Neon crown trim on tower top
                GameObject trim = GameObject.CreatePrimitive(PrimitiveType.Cube);
                trim.transform.SetParent(bld.transform, false);
                trim.transform.localPosition = new Vector3(0f, 0.48f, 0f);
                trim.transform.localScale = new Vector3(1.03f, 0.04f, 1.03f);
                DestroyImmediate(trim.GetComponent<Collider>());
                Color neonCol = (b % 3 == 0) ? HexColor("#FF2E93") : ((b % 3 == 1) ? HexColor("#00F5D4") : HexColor("#FACC15"));
                trim.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("SkyTrim_" + (b % 3), neonCol, null, 0.9f, 0f, neonCol * 2.2f);
            }
        }
        // World 3: 3 Polar Aurora Borealis Ribbons + Alpine Silhouettes
        else if (world == 3)
        {
            for (int a = 0; a < 3; a++)
            {
                GameObject aur = GameObject.CreatePrimitive(PrimitiveType.Cube);
                aur.name = "AuroraRibbon_" + a;
                aur.transform.SetParent(StageRoot, false);
                aur.transform.position = new Vector3(-40f + a * 45f, 85f + a * 12f, courseLength + 140f);
                aur.transform.rotation = Quaternion.Euler(12f, -20f + a * 18f, 8f);
                aur.transform.localScale = new Vector3(140f, 12f, 1.2f);
                DestroyImmediate(aur.GetComponent<Collider>());
                Color ac = (a == 1) ? HexColor("#A855F7") : HexColor("#2DD4BF");
                aur.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedVertexAlphaMaterial("AuroraMat_" + a, new Color(ac.r, ac.g, ac.b, 0.36f), null, 3040);
            }
        }
        // World 4: Towering Volcanic Factory Smokestacks
        else if (world == 4)
        {
            for (int s = 0; s < 10; s++)
            {
                float side = (s % 2 == 0) ? -1f : 1f;
                GameObject stk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stk.name = "FoundryStack_" + s;
                stk.transform.SetParent(StageRoot, false);
                stk.transform.position = new Vector3(side * 90f, 22f, s * ((courseLength + 80f) / 10f));
                stk.transform.localScale = new Vector3(12f, 45f, 12f);
                DestroyImmediate(stk.GetComponent<Collider>());
                stk.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("StackMat", HexColor("#18181B"));

                GameObject rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                rim.transform.SetParent(stk.transform, false);
                rim.transform.localPosition = new Vector3(0f, 0.98f, 0f);
                rim.transform.localScale = new Vector3(1.05f, 0.04f, 1.05f);
                DestroyImmediate(rim.GetComponent<Collider>());
                rim.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("StackGlowMat", HexColor("#FF4500"), null, 0.9f, 0f, HexColor("#FF4500") * 2.2f);
            }
        }
        // World 5: Planets & Twinkling 3D Star Field
        else if (world == 5)
        {
            SpawnCosmicBackgroundPlanets();
        }
    }

    public void BuildBossArena(int world)
    {
        LevelLengthSpec spec = LevelLengthCalculator.GetSpec(world * 10);
        float approach = spec.BossApproachMeters;

        PlayerSpawnPoint = new Vector3(0f, 2f, -approach - 14f);
        CheckpointPoint = PlayerSpawnPoint;

        Color bumperColor = HexColor("#D6C29F");
        switch (world)
        {
            case 1: bumperColor = HexColor("#D6C29F"); break;
            case 2: bumperColor = HexColor("#FF2E93"); break;
            case 3: bumperColor = HexColor("#38BDF8"); break;
            case 4: bumperColor = HexColor("#EF4444"); break;
            case 5: bumperColor = HexColor("#00F5D4"); break;
        }

        // Approach Catwalk Bridge
        CreateDetailedPlatform(new Vector3(0f, 0f, -approach * 0.5f - 20f), new Vector3(10f, 2f, approach + 4f), world, PlatformKind.Static, "ApproachBridge");

        // 40x40 Sculpted Wet-Puddle PBR Boss Stadium
        GameObject arena = CreateDetailedPlatform(Vector3.zero, new Vector3(40f, 2f, 40f), world, PlatformKind.BossArena, "BossArena_Platform");

        SpawnBossCentralBattleRing(arena.transform, world);

        SpawnBossArenaTotem(arena.transform, new Vector3(18f, 1f, 18f), world, 0f);
        SpawnBossArenaTotem(arena.transform, new Vector3(-18f, 1f, 18f), world, 1.5f);
        SpawnBossArenaTotem(arena.transform, new Vector3(18f, 1f, -18f), world, 3.0f);
        SpawnBossArenaTotem(arena.transform, new Vector3(-18f, 1f, -18f), world, 4.5f);

        CreatePlatform(new Vector3(0f, 1.2f, 20f), new Vector3(40f, 1.15f, 1.2f), bumperColor, "Bumper_N");
        CreatePlatform(new Vector3(0f, 1.2f, -20f), new Vector3(40f, 1.15f, 1.2f), bumperColor, "Bumper_S");
        CreatePlatform(new Vector3(20f, 1.2f, 0f), new Vector3(1.2f, 1.15f, 40f), bumperColor, "Bumper_E");
        CreatePlatform(new Vector3(-20f, 1.2f, 0f), new Vector3(1.2f, 1.15f, 40f), bumperColor, "Bumper_W");

        SpawnPedestalDonutSpring(new Vector3(15.5f, 1f, 15.5f), 26f, world);
        SpawnPedestalDonutSpring(new Vector3(-15.5f, 1f, 15.5f), 26f, world);
        SpawnPedestalDonutSpring(new Vector3(15.5f, 1f, -15.5f), 26f, world);
        SpawnPedestalDonutSpring(new Vector3(-15.5f, 1f, -15.5f), 26f, world);

        if (world == 1)
        {
            // Framing woodland grove outside World 1 Boss Coliseum perimeter!
            BuildLushOakTree(arena.transform, new Vector3(18.6f, 1f, 12.0f), 1.15f);
            BuildStylizedPineTree(arena.transform, new Vector3(-18.6f, 1f, 12.0f), 1.15f);
            BuildTropicalPalmTree(arena.transform, new Vector3(18.6f, 1f, -12.0f), 1.10f);
            BuildLushOakTree(arena.transform, new Vector3(-18.6f, 1f, -12.0f), 1.10f);
            BuildFloweringBerryBush(arena.transform, new Vector3(12.0f, 1f, 18.6f), 1.0f);
            BuildFloweringBerryBush(arena.transform, new Vector3(-12.0f, 1f, 18.6f), 1.0f);
        }

        CreateHazardFloor(world);
        CreateGoldenStar(new Vector3(15.5f, 8f, 15.5f), CurrentLevel);
    }

    private void SpawnBossCentralBattleRing(Transform parent, int world)
    {
        GameObject outer = new GameObject("BattleRing_Outer");
        outer.transform.SetParent(parent, false);
        outer.transform.localPosition = new Vector3(0f, 1.04f, 0f);
        MeshFilter mfO = outer.AddComponent<MeshFilter>();
        mfO.sharedMesh = LowPolyMeshHelper.GetTorusDonut(24, 8, 7.5f, 0.25f);
        MeshRenderer mrO = outer.AddComponent<MeshRenderer>();
        Color ringCol = (world == 4) ? HexColor("#FF5500") : ((world == 5) ? HexColor("#00F5D4") : ((world == 2) ? HexColor("#FF2E93") : HexColor("#FACC15")));
        mrO.sharedMaterial = SafeMaterialFactory.GetCachedMaterial("BattleRingMat_" + world, ringCol, null, 0.85f, 0.4f, ringCol * 0.9f);

        GameObject star = new GameObject("BattleRing_Star");
        star.transform.SetParent(parent, false);
        star.transform.localPosition = new Vector3(0f, 1.06f, 0f);
        star.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        MeshFilter mfS = star.AddComponent<MeshFilter>();
        mfS.sharedMesh = LowPolyMeshHelper.GetFivePointStar(3.2f, 1.4f, 0.08f);
        MeshRenderer mrS = star.AddComponent<MeshRenderer>();
        mrS.sharedMaterial = mrO.sharedMaterial;
    }

    private void SpawnBossArenaTotem(Transform parent, Vector3 localPos, int world, float phase)
    {
        GameObject totem = new GameObject("BossTotem_" + world);
        totem.transform.SetParent(parent, false);
        totem.transform.localPosition = localPos;

        GameObject col = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        col.transform.SetParent(totem.transform, false);
        col.transform.localPosition = new Vector3(0f, 3.8f, 0f);
        col.transform.localScale = new Vector3(1.4f, 3.8f, 1.4f);
        DestroyImmediate(col.GetComponent<Collider>());
        Color colColor = (world == 4) ? HexColor("#450A0A") : ((world == 5) ? HexColor("#1E1B4B") : HexColor("#334155"));
        col.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("TotemColMat_" + world, colColor, null, 0.5f, 0.4f);

        GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orb.transform.SetParent(totem.transform, false);
        orb.transform.localPosition = new Vector3(0f, 8.0f, 0f);
        orb.transform.localScale = Vector3.one * 1.8f;
        DestroyImmediate(orb.GetComponent<Collider>());
        Color orbColor = (world == 4) ? HexColor("#FF4500") : ((world == 5) ? HexColor("#00F5D4") : ((world == 2) ? HexColor("#FF2E93") : HexColor("#FACC15")));
        orb.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("TotemOrbMat_" + world, orbColor, null, 0.9f, 0.2f, orbColor * 2.0f);

        // Sweeping Volumetric Stadium Spotlight aimed inward over arena floor
        GameObject spot = new GameObject("StadiumSpotlight_" + phase);
        spot.transform.SetParent(totem.transform, false);
        spot.transform.localPosition = new Vector3(0f, 8.0f, 0f);
        Vector3 toCenter = new Vector3(-localPos.x, -7.0f, -localPos.z);
        Quaternion baseLook = Quaternion.FromToRotation(Vector3.down, toCenter.normalized);
        spot.transform.localRotation = baseLook;

        MeshFilter mfB = spot.AddComponent<MeshFilter>();
        mfB.sharedMesh = LowPolyMeshHelper.GetVolumetricConeMesh(16, 0.45f, 5.2f, 26f);
        MeshRenderer mrB = spot.AddComponent<MeshRenderer>();
        Color spotCol = new Color(orbColor.r, orbColor.g, orbColor.b, 0.20f);
        mrB.sharedMaterial = SafeMaterialFactory.GetCachedVertexAlphaMaterial("VolSpot_" + world + "_P1", spotCol, null, 3050);

        VolumetricSweepController sweep = spot.AddComponent<VolumetricSweepController>();
        sweep.BaseEuler = baseLook.eulerAngles;
        sweep.Phase = phase;
        sweep.BeamRenderer = mrB;
        sweep.SetWorld(world);
    }

    private void SpawnPedestalDonutSpring(Vector3 pos, float force, int world)
    {
        GameObject ped = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ped.transform.SetParent(StageRoot);
        ped.transform.position = pos + Vector3.up * 0.75f;
        ped.transform.localScale = new Vector3(2.6f, 0.75f, 2.6f);
        DestroyImmediate(ped.GetComponent<Collider>());
        Color pedColor = (world == 4) ? HexColor("#7F1D1D") : HexColor("#475569");
        ped.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("DonutPedMat_" + world, pedColor);

        CreateDonutSpring(pos + Vector3.up * 1.55f, force);
    }

    void StartBossFightLevel(int level)
    {
        InBossFight = true;
        BossAlive = false;
        BossDialogueDone = false;
        BossHealthContainer.SetActive(false);
        m_BossChipRatio = 1f;

        int world = level / 10;
        Vector3 bossSpawnPos = new Vector3(0f, 2.5f, 12f);

        GameObject bGo = new GameObject("Boss_World_" + world);
        bGo.transform.SetParent(StageRoot);
        bGo.transform.position = bossSpawnPos;

        BossController bCtrl = bGo.AddComponent<BossController>();
        ActiveBoss = bCtrl;
        bCtrl.BossType = world;

        LevelLengthSpec spec = LevelLengthCalculator.GetSpec(level);
        BossMaxHP = spec.BossMaxHP;

        switch (world)
        {
            case 1: BossName = "KING CHEESEBURG"; bCtrl.BuildKingCheeseburg(); break;
            case 2: BossName = "CAPTAIN CRISPY FRY"; bCtrl.BuildCaptainCrispyFry(); break;
            case 3: BossName = "FROSTY CONE MONSTER"; bCtrl.BuildFrostyConeMonster(); break;
            case 4: BossName = "SIR SIZZLE HOTDOG"; bCtrl.BuildSirSizzleHotdog(); break;
            case 5: BossName = "GALACTIC MEGA-PIZZA UFO"; bCtrl.BuildGalacticMegaPizza(); break;
        }

        BossCurrentHP = BossMaxHP;
        UpdateBossHealthUI();
        bCtrl.IsActiveAI = false;
    }

    public void TriggerBossGateEncounter()
    {
        if (!InBossFight || BossDialogueDone || ActiveBoss == null) return;
        BossDialogueDone = true;
        CheckpointPoint = new Vector3(0f, 2f, -15f);
        CheckpointActivated = true;
        ActiveBoss.EnterPreFightDialogueState();

        StartBossPreFightDialogue(CurrentLevel, () => {
            BossAlive = true;
            if (ActiveBoss != null) ActiveBoss.BeginRelentlessChase();
            BossHealthContainer.SetActive(true);
            if (ProgressBarRoot != null) ProgressBarRoot.SetActive(false);
            PlayBGM(1.25f);
            PlaySFX(SFXType.UnlockFanfare);
        });
    }

    public void UpdateBossHealthUI()
    {
        float pct = Mathf.Clamp01((float)BossCurrentHP / Mathf.Max(1, BossMaxHP));
        if (BossHealthBarFill != null) BossHealthBarFill.localScale = new Vector3(pct, 1f, 1f);
        if (BossHealthBarLabel != null) BossHealthBarLabel.text = BossName + " • " + BossCurrentHP + " / " + BossMaxHP + " HP";
        if (BossPhaseBadgeText != null)
        {
            bool enraged = (BossCurrentHP <= BossMaxHP * 0.5f && BossCurrentHP > 0);
            BossPhaseBadgeText.text = enraged ? "⚡ ENRAGED!" : "PHASE 1";
            BossPhaseBadgeText.color = enraged ? HexColor("#FF2E93") : HexColor("#FACC15");
            if (BossHealthFillImg != null) BossHealthFillImg.color = enraged ? HexColor("#FF2E93") : HexColor("#EF4444");
        }
    }

    public void OnBossDefeated(BossController boss)
    {
        BossAlive = false;
        InBossFight = false;
        BossHealthContainer.SetActive(false);
        if (OffscreenBossIndicator != null) OffscreenBossIndicator.gameObject.SetActive(false);
        if (LightPool != null) LightPool.SetBossAura(false, Vector3.zero, Color.white, 0f, 0f);
        PlayBGM(1.0f);

        Vector3 spawnPos = new Vector3(0f, 1.6f, 0f);
        GameObject mb = CreateMiniBerryObject(spawnPos, CurrentLevel);
        MiniBerry comp = mb.GetComponent<MiniBerry>();
        if (comp != null) comp.PlayCelebrationBeacon();

        // Celebratory Golden Sunbeam Beacon over the Rescued Mini-Berry
        GameObject beam = new GameObject("MiniBerry_VictorySunbeam");
        beam.transform.SetParent(mb.transform, false);
        beam.transform.localPosition = new Vector3(0f, 14f, 0f);
        MeshFilter mfB = beam.AddComponent<MeshFilter>();
        mfB.sharedMesh = LowPolyMeshHelper.GetVolumetricConeMesh(16, 1.8f, 3.6f, 16f);
        MeshRenderer mrB = beam.AddComponent<MeshRenderer>();
        mrB.sharedMaterial = SafeMaterialFactory.GetCachedVertexAlphaMaterial("VictoryBeamMat", new Color(1f, 0.90f, 0.35f, 0.28f), null, 3050);
    }
#endregion

// ===========================================================================
// SECTION 7B: CHARACTER & ENEMY 3D MODELS + GTA-6 LAND SCULPTOR & PROPS
// ===========================================================================
public static void BuildBerryCharacterModel(GameObject parent, int skin)
    {
        Color mainBumpCol = HexColor("#1E5BFF");
        Color darkBumpCol = HexColor("#121B3A");
        Color leafCol = HexColor("#4CD936");
        Color facePatchCol = HexColor("#38BDF8");

        if (skin == 1) // Golden Berry
        {
            mainBumpCol = HexColor("#FFD700");
            darkBumpCol = HexColor("#B45309");
            leafCol = HexColor("#10B981");
            facePatchCol = HexColor("#FDE047");
        }
        else if (skin == 2) // Flying Diamond Bird
        {
            mainBumpCol = HexColor("#00FFFF");
            darkBumpCol = HexColor("#0284C7");
            leafCol = HexColor("#38BDF8");
            facePatchCol = HexColor("#E0F2FE");
        }

        GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.transform.SetParent(parent.transform, false);
        core.transform.localPosition = new Vector3(0f, 0.95f, 0f);
        core.transform.localScale = Vector3.one * 1.35f;
        DestroyImmediate(core.GetComponent<Collider>());
        core.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("BerryCore_" + skin, mainBumpCol, null, 0.88f);

        for (int i = 0; i < 16; i++)
        {
            float phi = Mathf.Acos(-1f + (2f * i) / 16f);
            float theta = Mathf.Sqrt(16f * Mathf.PI) * phi;
            Vector3 pos = new Vector3(Mathf.Cos(theta) * Mathf.Sin(phi), Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(phi)) * 0.75f;
            pos.y += 0.95f;

            GameObject bump = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bump.transform.SetParent(parent.transform, false);
            bump.transform.localPosition = pos;
            bump.transform.localScale = Vector3.one * 0.58f;
            DestroyImmediate(bump.GetComponent<Collider>());
            bump.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial((i % 2 == 0) ? "BumpM_" + skin : "BumpD_" + skin, (i % 2 == 0) ? mainBumpCol : darkBumpCol, null, 0.92f);
        }

        for (int l = 0; l < 5; l++)
        {
            float la = l * 72f * Mathf.Deg2Rad;
            GameObject leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leaf.transform.SetParent(parent.transform, false);
            leaf.transform.localPosition = new Vector3(Mathf.Cos(la) * 0.38f, 1.82f, Mathf.Sin(la) * 0.38f);
            leaf.transform.localScale = new Vector3(0.28f, 0.08f, 0.42f);
            leaf.transform.localRotation = Quaternion.Euler(30f, l * 72f, 0f);
            DestroyImmediate(leaf.GetComponent<Collider>());
            leaf.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("LeafCrownMat", leafCol);
        }

        GameObject eyeL = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eyeL.transform.SetParent(parent.transform, false);
        eyeL.transform.localPosition = new Vector3(-0.25f, 1.15f, 0.68f);
        eyeL.transform.localScale = new Vector3(0.24f, 0.32f, 0.16f);
        DestroyImmediate(eyeL.GetComponent<Collider>());
        eyeL.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("EyeBlack", Color.black, null, 0.9f);

        GameObject eyeR = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eyeR.transform.SetParent(parent.transform, false);
        eyeR.transform.localPosition = new Vector3(0.25f, 1.15f, 0.68f);
        eyeR.transform.localScale = new Vector3(0.24f, 0.32f, 0.16f);
        DestroyImmediate(eyeR.GetComponent<Collider>());
        eyeR.GetComponent<Renderer>().sharedMaterial = eyeL.GetComponent<Renderer>().sharedMaterial;

        if (skin == 2)
        {
            GameObject wL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wL.name = "Wing_Left";
            wL.transform.SetParent(parent.transform, false);
            wL.transform.localPosition = new Vector3(-0.95f, 1.15f, -0.2f);
            wL.transform.localScale = new Vector3(1.1f, 0.08f, 0.45f);
            wL.transform.localRotation = Quaternion.Euler(15f, 25f, 20f);
            DestroyImmediate(wL.GetComponent<Collider>());
            wL.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("BirdWingMat", HexColor("#00FFFF"), null, 0.95f);

            GameObject wR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wR.name = "Wing_Right";
            wR.transform.SetParent(parent.transform, false);
            wR.transform.localPosition = new Vector3(0.95f, 1.15f, -0.2f);
            wR.transform.localScale = new Vector3(1.1f, 0.08f, 0.45f);
            wR.transform.localRotation = Quaternion.Euler(15f, -25f, -20f);
            DestroyImmediate(wR.GetComponent<Collider>());
            wR.GetComponent<Renderer>().sharedMaterial = wL.GetComponent<Renderer>().sharedMaterial;
        }
    }

public void BuildBurgerModel(GameObject root, EnemyVisualAnimator anim = null)
    {
        GameObject rig = new GameObject("VisualRig");
        rig.transform.SetParent(root.transform, false);
        if (anim != null) anim.VisualRig = rig.transform;

        // Bottom Bun
        GameObject bBun = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bBun.transform.SetParent(rig.transform, false);
        bBun.transform.localPosition = new Vector3(0f, 0.22f, 0f);
        bBun.transform.localScale = new Vector3(1.05f, 0.22f, 1.05f);
        DestroyImmediate(bBun.GetComponent<Collider>());
        bBun.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("BurgerBunMat", HexColor("#E69F45"), ProceduralTextureFactory.GetToastedBunTexture());

        // Patty 1
        GameObject patty1 = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        patty1.transform.SetParent(rig.transform, false);
        patty1.transform.localPosition = new Vector3(0f, 0.38f, 0f);
        patty1.transform.localScale = new Vector3(1.02f, 0.16f, 1.02f);
        DestroyImmediate(patty1.GetComponent<Collider>());
        patty1.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("BurgerPattyMat", HexColor("#5C2C16"), ProceduralTextureFactory.GetGrillMarksTexture());

        // Cheese 1 (45 deg)
        GameObject cheese1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cheese1.transform.SetParent(rig.transform, false);
        cheese1.transform.localPosition = new Vector3(0f, 0.48f, 0f);
        cheese1.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        cheese1.transform.localScale = new Vector3(0.98f, 0.06f, 0.98f);
        DestroyImmediate(cheese1.GetComponent<Collider>());
        cheese1.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("BurgerCheeseMat", HexColor("#FACC15"));

        // Lettuce Ring
        GameObject lettuce = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        lettuce.transform.SetParent(rig.transform, false);
        lettuce.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        lettuce.transform.localScale = new Vector3(1.08f, 0.08f, 1.08f);
        DestroyImmediate(lettuce.GetComponent<Collider>());
        lettuce.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("BurgerLettuceMat", HexColor("#22C55E"));

        // Tomato Slice
        GameObject tomato = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tomato.transform.SetParent(rig.transform, false);
        tomato.transform.localPosition = new Vector3(0f, 0.62f, 0f);
        tomato.transform.localScale = new Vector3(0.94f, 0.07f, 0.94f);
        DestroyImmediate(tomato.GetComponent<Collider>());
        tomato.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("BurgerTomatoMat", HexColor("#EF4444"));

        // Patty 2
        GameObject patty2 = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        patty2.transform.SetParent(rig.transform, false);
        patty2.transform.localPosition = new Vector3(0f, 0.72f, 0f);
        patty2.transform.localScale = new Vector3(1.0f, 0.16f, 1.0f);
        DestroyImmediate(patty2.GetComponent<Collider>());
        patty2.GetComponent<Renderer>().sharedMaterial = patty1.GetComponent<Renderer>().sharedMaterial;

        // Top Bun Dome
        GameObject tBun = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        tBun.transform.SetParent(rig.transform, false);
        tBun.transform.localPosition = new Vector3(0f, 0.96f, 0f);
        tBun.transform.localScale = new Vector3(1.08f, 0.52f, 1.08f);
        DestroyImmediate(tBun.GetComponent<Collider>());
        tBun.GetComponent<Renderer>().sharedMaterial = bBun.GetComponent<Renderer>().sharedMaterial;

        // 10 Cream Sesame Seeds on Top Bun
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            GameObject ses = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ses.transform.SetParent(tBun.transform, false);
            ses.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.35f, 0.38f, Mathf.Sin(a) * 0.35f);
            ses.transform.localScale = Vector3.one * 0.09f;
            DestroyImmediate(ses.GetComponent<Collider>());
            ses.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("SesameMat", HexColor("#FEF3C7"));
        }

        // Cartoon Angry Face on front
        AttachCartoonFace(rig.transform, new Vector3(0f, 0.98f, 0.48f), 0.22f);

        // Stubby White Cartoon Shoes
        GameObject fL = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        fL.transform.SetParent(root.transform, false);
        fL.transform.localPosition = new Vector3(-0.28f, 0.08f, 0.04f);
        fL.transform.localScale = new Vector3(0.24f, 0.16f, 0.34f);
        DestroyImmediate(fL.GetComponent<Collider>());
        fL.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("ShoeWhite", Color.white);

        GameObject fR = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        fR.transform.SetParent(root.transform, false);
        fR.transform.localPosition = new Vector3(0.28f, 0.08f, 0.04f);
        fR.transform.localScale = new Vector3(0.24f, 0.16f, 0.34f);
        DestroyImmediate(fR.GetComponent<Collider>());
        fR.GetComponent<Renderer>().sharedMaterial = fL.GetComponent<Renderer>().sharedMaterial;

        if (anim != null)
        {
            anim.LeftFoot = fL.transform;
            anim.RightFoot = fR.transform;
        }

        // Contact Ground Shadow Quad
        AttachGroundShadow(root.transform, 1.35f, anim);
    }

    private void AttachCartoonFace(Transform parent, Vector3 localPos, float eyeScale)
    {
        // Left Eye (White sclera)
        GameObject eL = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eL.transform.SetParent(parent, false);
        eL.transform.localPosition = localPos + new Vector3(-eyeScale * 0.95f, 0f, 0f);
        eL.transform.localScale = Vector3.one * eyeScale;
        DestroyImmediate(eL.GetComponent<Collider>());
        eL.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("EyeWhite", Color.white, null, 0.88f);

        // Pupil + Specular catchlight
        GameObject pL = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        pL.transform.SetParent(eL.transform, false);
        pL.transform.localPosition = new Vector3(0f, 0f, 0.35f);
        pL.transform.localScale = Vector3.one * 0.55f;
        DestroyImmediate(pL.GetComponent<Collider>());
        pL.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("EyeBlack", Color.black, null, 0.92f);

        // Right Eye
        GameObject eR = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eR.transform.SetParent(parent, false);
        eR.transform.localPosition = localPos + new Vector3(eyeScale * 0.95f, 0f, 0f);
        eR.transform.localScale = Vector3.one * eyeScale;
        DestroyImmediate(eR.GetComponent<Collider>());
        eR.GetComponent<Renderer>().sharedMaterial = eL.GetComponent<Renderer>().sharedMaterial;

        GameObject pR = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        pR.transform.SetParent(eR.transform, false);
        pR.transform.localPosition = new Vector3(0f, 0f, 0.35f);
        pR.transform.localScale = Vector3.one * 0.55f;
        DestroyImmediate(pR.GetComponent<Collider>());
        pR.GetComponent<Renderer>().sharedMaterial = pL.GetComponent<Renderer>().sharedMaterial;

        // V-shaped Angry Eyebrows
        GameObject bL = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bL.transform.SetParent(parent, false);
        bL.transform.localPosition = localPos + new Vector3(-eyeScale * 0.95f, eyeScale * 0.65f, 0.05f);
        bL.transform.localRotation = Quaternion.Euler(0f, 0f, -22f);
        bL.transform.localScale = new Vector3(eyeScale * 1.1f, eyeScale * 0.28f, eyeScale * 0.35f);
        DestroyImmediate(bL.GetComponent<Collider>());
        bL.GetComponent<Renderer>().sharedMaterial = pL.GetComponent<Renderer>().sharedMaterial;

        GameObject bR = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bR.transform.SetParent(parent, false);
        bR.transform.localPosition = localPos + new Vector3(eyeScale * 0.95f, eyeScale * 0.65f, 0.05f);
        bR.transform.localRotation = Quaternion.Euler(0f, 0f, 22f);
        bR.transform.localScale = new Vector3(eyeScale * 1.1f, eyeScale * 0.28f, eyeScale * 0.35f);
        DestroyImmediate(bR.GetComponent<Collider>());
        bR.GetComponent<Renderer>().sharedMaterial = pL.GetComponent<Renderer>().sharedMaterial;
    }

    private void AttachGroundShadow(Transform parent, float scale, EnemyVisualAnimator anim = null)
    {
        GameObject shadow = GameObject.CreatePrimitive(PrimitiveType.Quad);
        shadow.name = "GroundShadow";
        shadow.transform.SetParent(parent, false);
        shadow.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        shadow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        shadow.transform.localScale = Vector3.one * scale;
        DestroyImmediate(shadow.GetComponent<Collider>());
        shadow.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("BlobShadowMat", Color.white, ProceduralTextureFactory.GetBlobShadowTexture(), 0f, 0f, null, true);
        if (anim != null) anim.GroundShadow = shadow.transform;
    }

    // World 2: Hopping Crazy Soda Cup
    public void BuildSodaCupEnemy(GameObject root, EnemyVisualAnimator anim)
    {
        GameObject rig = new GameObject("VisualRig");
        rig.transform.SetParent(root.transform, false);
        anim.VisualRig = rig.transform;

        // Tapered Cup Body
        GameObject cup = new GameObject("CupBody");
        cup.transform.SetParent(rig.transform, false);
        cup.transform.localPosition = new Vector3(0f, 0.65f, 0f);
        MeshFilter mf = cup.AddComponent<MeshFilter>();
        mf.sharedMesh = LowPolyMeshHelper.GetTaperedFrustum(0.48f, 0.38f, 1.05f, 12);
        MeshRenderer mr = cup.AddComponent<MeshRenderer>();
        mr.sharedMaterial = SafeMaterialFactory.GetCachedMaterial("SodaCupMat", Color.white, ProceduralTextureFactory.GetSodaStripesTexture());

        // Snap-on White Plastic Lid Rim
        GameObject lid = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        lid.transform.SetParent(rig.transform, false);
        lid.transform.localPosition = new Vector3(0f, 1.18f, 0f);
        lid.transform.localScale = new Vector3(1.02f, 0.12f, 1.02f);
        DestroyImmediate(lid.GetComponent<Collider>());
        lid.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("LidWhite", HexColor("#F8FAFC"));

        // Straw Pivot & Bent Straw
        GameObject strawPivot = new GameObject("StrawPivot");
        strawPivot.transform.SetParent(rig.transform, false);
        strawPivot.transform.localPosition = new Vector3(0.12f, 1.24f, 0f);
        anim.StrawPivot = strawPivot.transform;

        GameObject strawL = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        strawL.transform.SetParent(strawPivot.transform, false);
        strawL.transform.localPosition = new Vector3(0f, 0.20f, 0f);
        strawL.transform.localScale = new Vector3(0.10f, 0.25f, 0.10f);
        DestroyImmediate(strawL.GetComponent<Collider>());
        strawL.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("StrawRed", HexColor("#EF4444"));

        GameObject strawTip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        strawTip.transform.SetParent(strawPivot.transform, false);
        strawTip.transform.localPosition = new Vector3(0.12f, 0.42f, 0f);
        strawTip.transform.localRotation = Quaternion.Euler(0f, 0f, -48f);
        strawTip.transform.localScale = new Vector3(0.10f, 0.20f, 0.10f);
        DestroyImmediate(strawTip.GetComponent<Collider>());
        strawTip.GetComponent<Renderer>().sharedMaterial = strawL.GetComponent<Renderer>().sharedMaterial;

        // Grumpy Face on Cup Front
        AttachCartoonFace(rig.transform, new Vector3(0f, 0.72f, 0.45f), 0.20f);
        AttachGroundShadow(root.transform, 1.25f, anim);
    }

    // World 3: Sliding Frosty Sundae Monster
    public void BuildSundaeEnemy(GameObject root, EnemyVisualAnimator anim)
    {
        GameObject rig = new GameObject("VisualRig");
        rig.transform.SetParent(root.transform, false);
        anim.VisualRig = rig.transform;

        // Waffle Cone Base
        GameObject cone = new GameObject("WaffleBase");
        cone.transform.SetParent(rig.transform, false);
        cone.transform.localPosition = new Vector3(0f, 0.25f, 0f);
        cone.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
        MeshFilter mf = cone.AddComponent<MeshFilter>();
        mf.sharedMesh = LowPolyMeshHelper.GetSharpCone(10, 0.50f, 0.55f);
        MeshRenderer mr = cone.AddComponent<MeshRenderer>();
        mr.sharedMaterial = SafeMaterialFactory.GetCachedMaterial("WaferMat", HexColor("#D97706"), ProceduralTextureFactory.GetWaferCrissCrossTexture());

        // Mint Green Scoop
        GameObject s1 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s1.transform.SetParent(rig.transform, false);
        s1.transform.localPosition = new Vector3(0f, 0.62f, 0f);
        s1.transform.localScale = new Vector3(0.96f, 0.72f, 0.96f);
        DestroyImmediate(s1.GetComponent<Collider>());
        s1.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("MintScoopMat", HexColor("#6EE7B7"), null, 0.82f);

        // Strawberry Pink Scoop
        GameObject s2 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s2.transform.SetParent(rig.transform, false);
        s2.transform.localPosition = new Vector3(0f, 1.08f, 0f);
        s2.transform.localScale = new Vector3(0.78f, 0.68f, 0.78f);
        DestroyImmediate(s2.GetComponent<Collider>());
        s2.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("BerryScoopMat", HexColor("#F472B6"), null, 0.82f);

        // Chocolate Fudge Cap
        GameObject fudge = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        fudge.transform.SetParent(rig.transform, false);
        fudge.transform.localPosition = new Vector3(0f, 1.34f, 0f);
        fudge.transform.localScale = new Vector3(0.72f, 0.32f, 0.72f);
        DestroyImmediate(fudge.GetComponent<Collider>());
        fudge.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("FudgeMat", HexColor("#3E1F14"), null, 0.90f);

        // Cherry Crown
        GameObject cherry = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        cherry.transform.SetParent(rig.transform, false);
        cherry.transform.localPosition = new Vector3(0f, 1.54f, 0f);
        cherry.transform.localScale = Vector3.one * 0.24f;
        DestroyImmediate(cherry.GetComponent<Collider>());
        cherry.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("CherryMat", HexColor("#DC2626"), null, 0.92f);

        // Shivering Grumpy Face
        AttachCartoonFace(rig.transform, new Vector3(0f, 0.68f, 0.44f), 0.20f);
        AttachGroundShadow(root.transform, 1.30f, anim);
    }

    // World 4: Sizzling Mini-Hotdog Guard
    public void BuildHotdogEnemy(GameObject root, EnemyVisualAnimator anim)
    {
        GameObject rig = new GameObject("VisualRig");
        rig.transform.SetParent(root.transform, false);
        anim.VisualRig = rig.transform;

        // Bun Left & Right
        GameObject bunL = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        bunL.transform.SetParent(rig.transform, false);
        bunL.transform.localPosition = new Vector3(-0.24f, 0.42f, 0f);
        bunL.transform.localScale = new Vector3(0.36f, 0.42f, 1.18f);
        DestroyImmediate(bunL.GetComponent<Collider>());
        bunL.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("HotdogBunMat", HexColor("#E69F45"), ProceduralTextureFactory.GetToastedBunTexture());

        GameObject bunR = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        bunR.transform.SetParent(rig.transform, false);
        bunR.transform.localPosition = new Vector3(0.24f, 0.42f, 0f);
        bunR.transform.localScale = new Vector3(0.36f, 0.42f, 1.18f);
        DestroyImmediate(bunR.GetComponent<Collider>());
        bunR.GetComponent<Renderer>().sharedMaterial = bunL.GetComponent<Renderer>().sharedMaterial;

        // Grilled Sausage
        GameObject sausage = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        sausage.transform.SetParent(rig.transform, false);
        sausage.transform.localPosition = new Vector3(0f, 0.54f, 0f);
        sausage.transform.localScale = new Vector3(0.44f, 0.44f, 1.38f);
        DestroyImmediate(sausage.GetComponent<Collider>());
        sausage.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("SausageMat", HexColor("#9A3412"), ProceduralTextureFactory.GetGrillMarksTexture());

        // Yellow Mustard Zig-Zag
        for (int m = 0; m < 5; m++)
        {
            GameObject mst = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mst.transform.SetParent(rig.transform, false);
            mst.transform.localPosition = new Vector3(((m % 2 == 0) ? -0.06f : 0.06f), 0.78f, (m - 2) * 0.25f);
            mst.transform.localRotation = Quaternion.Euler(0f, (m % 2 == 0) ? -35f : 35f, 0f);
            mst.transform.localScale = new Vector3(0.08f, 0.08f, 0.26f);
            DestroyImmediate(mst.GetComponent<Collider>());
            mst.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("MustardMat", HexColor("#FACC15"));
        }

        // Fiery Glowing Eyes on Front
        AttachCartoonFace(rig.transform, new Vector3(0f, 0.58f, 0.65f), 0.18f);
        AttachGroundShadow(root.transform, 1.45f, anim);
    }

    // World 5: Hovering Alien Pizza Drone
    public void BuildPizzaDroneEnemy(GameObject root, EnemyVisualAnimator anim)
    {
        GameObject rig = new GameObject("VisualRig");
        rig.transform.SetParent(root.transform, false);
        anim.VisualRig = rig.transform;

        // Pizza Wedge Prism
        GameObject pizza = new GameObject("PizzaWedge");
        pizza.transform.SetParent(rig.transform, false);
        pizza.transform.localPosition = new Vector3(0f, 0.65f, 0f);
        MeshFilter mf = pizza.AddComponent<MeshFilter>();
        mf.sharedMesh = LowPolyMeshHelper.GetPizzaWedge(1.35f, 1.25f, 0.24f, 8);
        MeshRenderer mr = pizza.AddComponent<MeshRenderer>();
        mr.sharedMaterial = SafeMaterialFactory.GetCachedMaterial("PizzaCheeseMat", HexColor("#FACC15"), null, 0.65f);

        // Pepperoni Discs
        for (int p = 0; p < 4; p++)
        {
            GameObject pep = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pep.transform.SetParent(pizza.transform, false);
            float pz = 0.4f + (p * 0.25f);
            float px = Mathf.Sin(p * 1.7f) * 0.25f;
            pep.transform.localPosition = new Vector3(px, 0.14f, pz);
            pep.transform.localScale = new Vector3(0.24f, 0.04f, 0.24f);
            DestroyImmediate(pep.GetComponent<Collider>());
            pep.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("PepMat", HexColor("#DC2626"), null, 0.85f);
        }

        // Anti-Grav Thruster Ring Underneath
        GameObject thruster = new GameObject("ThrusterRing");
        thruster.transform.SetParent(rig.transform, false);
        thruster.transform.localPosition = new Vector3(0f, 0.50f, 0.70f);
        MeshFilter mfT = thruster.AddComponent<MeshFilter>();
        mfT.sharedMesh = LowPolyMeshHelper.GetTorusDonut(16, 8, 0.45f, 0.08f);
        MeshRenderer mrT = thruster.AddComponent<MeshRenderer>();
        mrT.sharedMaterial = SafeMaterialFactory.GetCachedMaterial("ThrusterGlowMat", HexColor("#00F5D4"), null, 0.9f, 0f, HexColor("#00F5D4"));
        anim.ThrusterRing = thruster.transform;

        // Alien Eye Stalks
        for (int s = 0; s < 2; s++)
        {
            float sx = (s == 0) ? -0.26f : 0.26f;
            GameObject stalk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stalk.transform.SetParent(rig.transform, false);
            stalk.transform.localPosition = new Vector3(sx, 0.95f, 0.65f);
            stalk.transform.localScale = new Vector3(0.08f, 0.25f, 0.08f);
            DestroyImmediate(stalk.GetComponent<Collider>());
            stalk.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("AlienGreenMat", HexColor("#4ADE80"));
        }
        AttachCartoonFace(rig.transform, new Vector3(0f, 1.15f, 0.65f), 0.18f);
        AttachGroundShadow(root.transform, 1.40f, anim);
    }

    // Regular Enemy Spawner
    public void SpawnPatrollingEnemy(Vector3 pos, int world)
    {
        GameObject enGo = new GameObject("Enemy_World_" + world);
        enGo.transform.SetParent(StageRoot);
        enGo.transform.position = pos;

        EnemyController ctrl = enGo.AddComponent<EnemyController>();
        EnemyVisualAnimator anim = enGo.AddComponent<EnemyVisualAnimator>();
        anim.KindIndex = Mathf.Clamp(world, 1, 5);

        switch (anim.KindIndex)
        {
            case 1: BuildBurgerModel(enGo, anim); break;
            case 2: BuildSodaCupEnemy(enGo, anim); break;
            case 3: BuildSundaeEnemy(enGo, anim); break;
            case 4: BuildHotdogEnemy(enGo, anim); break;
            case 5: BuildPizzaDroneEnemy(enGo, anim); break;
        }

        ActiveEnemies.Add(ctrl);
    }

    // -----------------------------------------------------------------------
    // GTA-6 SCULPTED 6-LAYER LAND PLATFORMS & LIVING FOLIAGE PROPS
    // -----------------------------------------------------------------------
    public GameObject CreateDetailedPlatform(Vector3 center, Vector3 size, int worldIndex, PlatformKind kind = PlatformKind.Static, string name = "Platform")
    {
        GameObject platRoot = new GameObject(name);
        platRoot.transform.SetParent(StageRoot);
        platRoot.transform.position = center;

        // Layer 0: Exact Physics BoxCollider (Zero decorative colliders!)
        BoxCollider col = platRoot.AddComponent<BoxCollider>();
        col.size = size;
        col.center = Vector3.zero;

        float W = size.x;
        float H = size.y;
        float D = size.z;

        // Layer 1: Sculpted Beveled Surface Cap (4-Channel 256x256 PBR + Wet Puddles!)
        float capH = Mathf.Min(0.36f, H * 0.40f);
        Vector3 capPos = new Vector3(0f, (H * 0.5f) - (capH * 0.5f), 0f);

        GameObject topCap = new GameObject("Tier1_BeveledLandCap");
        topCap.transform.SetParent(platRoot.transform, false);
        topCap.transform.localPosition = capPos;
        MeshFilter mfTop = topCap.AddComponent<MeshFilter>();
        mfTop.sharedMesh = LowPolyMeshHelper.GetBeveledSlabMesh(W + 0.38f, capH, D + 0.38f, 0.16f);
        MeshRenderer mrTop = topCap.AddComponent<MeshRenderer>();

        PBRMaterialSuite topSuite = (kind == PlatformKind.Wafer)
            ? ProceduralTextureFactory.GetWaferPBRSuite()
            : ProceduralTextureFactory.GetWorldTerrainPBRSuite(worldIndex, false);
        float tileX = (kind == PlatformKind.BossArena) ? 1.8f : 1.0f;
        float tileY = (kind == PlatformKind.BossArena) ? 1.8f : 1.0f;
        string matKey = (kind == PlatformKind.Wafer ? "Wafer" : "W" + worldIndex + "_Top") + "_" + ProceduralTextureFactory.GetWorldGroundTexture(worldIndex).width;
        mrTop.sharedMaterial = SafeMaterialFactory.GetCachedPBRMaterial(matKey, topSuite, tileX, tileY);

        // Layer 2: Architectural Perimeter Curb / Shoreline Trim Lip
        float lipH = 0.14f;
        Vector3 lipSize = new Vector3(W + 0.18f, lipH, D + 0.18f);
        Vector3 lipPos = new Vector3(0f, capPos.y - (capH * 0.5f) - (lipH * 0.5f), 0f);

        GameObject lip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lip.name = "Tier2_PerimeterCurb";
        lip.transform.SetParent(platRoot.transform, false);
        lip.transform.localPosition = lipPos;
        lip.transform.localScale = lipSize;
        DestroyImmediate(lip.GetComponent<Collider>());

        Color lipCol = ProceduralTextureFactory.GetWorldLipColor(worldIndex);
        Texture2D lipTex = (worldIndex == 4) ? ProceduralTextureFactory.GetHazardStripeTexture() : null;
        Color? lipEm = (worldIndex == 2 || worldIndex == 4 || worldIndex == 5) ? (Color?)(lipCol * 1.4f) : null;
        lip.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedTiledMaterial("W" + worldIndex + "_Lip", lipTex, lipCol, W / 2f, 1f, 0.72f, (worldIndex == 4 ? 0.5f : 0f), lipEm);

        // Side Accent Neon Highway Rails on World 5 Wide Platforms
        if (worldIndex == 5 && W >= 5.5f && D >= 6.0f && kind != PlatformKind.BossArena)
        {
            SpawnSideNeonRail(platRoot.transform, new Vector3(-W * 0.5f - 0.1f, H * 0.5f + 0.05f, 0f), D, HexColor("#00F5D4"));
            SpawnSideNeonRail(platRoot.transform, new Vector3( W * 0.5f + 0.1f, H * 0.5f + 0.05f, 0f), D, HexColor("#FF2E93"));
        }

        // Layer 3: Embedded Land Surface Storytelling Decals (Zero Collider!)
        if (W >= 6.0f && D >= 8.0f && kind != PlatformKind.BossArena)
        {
            SpawnEmbeddedSurfaceDecals(platRoot.transform, W, H, D, worldIndex);
        }

        // Layer 4: Stratified Normal-Mapped Cliff Foundation
        float cliffH = Mathf.Max(0.4f, H - capH - lipH);
        Vector3 cliffSize = new Vector3(W * 0.92f, cliffH, D * 0.92f);
        Vector3 cliffPos = new Vector3(0f, lipPos.y - (lipH * 0.5f) - (cliffH * 0.5f), 0f);

        GameObject cliff = new GameObject("Tier4_StratifiedCliff");
        cliff.transform.SetParent(platRoot.transform, false);
        cliff.transform.localPosition = cliffPos;
        MeshFilter mfCliff = cliff.AddComponent<MeshFilter>();
        mfCliff.sharedMesh = LowPolyMeshHelper.GetBeveledSlabMesh(cliffSize.x, cliffSize.y, cliffSize.z, 0.08f);
        MeshRenderer mrCliff = cliff.AddComponent<MeshRenderer>();
        PBRMaterialSuite cliffSuite = ProceduralTextureFactory.GetWorldTerrainPBRSuite(worldIndex, true);
        Material cliffMat = SafeMaterialFactory.GetCachedPBRMaterial("W" + worldIndex + "_Cliff_" + cliffSuite.AlbedoTex.width, cliffSuite, 1.0f, 1.0f);
        mrCliff.sharedMaterial = cliffMat;

        // Layer 5: Tapered Sculpted Rocky Underside & World Hanging Details
        float anchorH = Mathf.Min(0.90f, H * 0.65f);
        Vector3 anchorSize = new Vector3(W * 0.70f, anchorH, D * 0.70f);
        Vector3 anchorPos = new Vector3(0f, cliffPos.y - (cliffH * 0.5f) - (anchorH * 0.5f), 0f);

        GameObject anchor = new GameObject("Tier5_RockyAnchor");
        anchor.transform.SetParent(platRoot.transform, false);
        anchor.transform.localPosition = anchorPos;
        MeshFilter mfAnc = anchor.AddComponent<MeshFilter>();
        mfAnc.sharedMesh = LowPolyMeshHelper.GetBeveledSlabMesh(anchorSize.x, anchorSize.y, anchorSize.z, 0.14f);
        anchor.AddComponent<MeshRenderer>().sharedMaterial = cliffMat;

        SpawnUndersideWorldDetails(platRoot.transform, anchorPos, anchorSize, worldIndex);

        bool allowDecor = (kind == PlatformKind.Static) && ((worldIndex == 1 && W >= 5.0f && D >= 4.5f) || (W >= 6f && D >= 6f));
        if (allowDecor)
        {
            DecoratePlatformSafely(platRoot.transform, size, worldIndex);
        }

        return platRoot;
    }

    private void SpawnSideNeonRail(Transform parent, Vector3 localPos, float length, Color col)
    {
        GameObject rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rail.name = "NeonEdgeRail";
        rail.transform.SetParent(parent, false);
        rail.transform.localPosition = localPos;
        rail.transform.localScale = new Vector3(0.14f, 0.12f, length);
        DestroyImmediate(rail.GetComponent<Collider>());
        rail.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("RailNeon_" + col.GetHashCode(), col, null, 0.9f, 0f, col * 2.2f);
    }

    private void SpawnEmbeddedSurfaceDecals(Transform parent, float W, float H, float D, int world)
    {
        float topY = H * 0.5f + 0.015f;
        int count = Mathf.Clamp((int)(D / 4.5f), 1, 5);
        for (int i = 0; i < count; i++)
        {
            float zPos = Mathf.Lerp(-D * 0.34f, D * 0.34f, (count == 1) ? 0.5f : (float)i / (count - 1));
            GameObject decal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            decal.name = "LandDecal_" + world + "_" + i;
            decal.transform.SetParent(parent, false);
            decal.transform.localPosition = new Vector3(((i % 2 == 0) ? -0.35f : 0.35f), topY, zPos);
            DestroyImmediate(decal.GetComponent<Collider>());

            if (world == 1)
            {
                decal.transform.localScale = new Vector3(1.35f, 0.03f, 1.05f);
                decal.transform.localRotation = Quaternion.Euler(0f, i * 18f, 0f);
                decal.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("FlagstoneMat", HexColor("#D6C29F"), null, 0.35f);
            }
            else if (world == 2)
            {
                decal.transform.localPosition = new Vector3(0f, topY, zPos);
                decal.transform.localScale = new Vector3(0.35f, 0.02f, 1.4f);
                decal.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("CrosswalkMat", Color.white, null, 0.7f);
            }
            else if (world == 3)
            {
                decal.transform.localScale = new Vector3(2.2f, 0.02f, 1.8f);
                decal.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("MirrorIcePatch", HexColor("#E0F7FA"), null, 0.96f);
            }
            else if (world == 4)
            {
                decal.transform.localPosition = new Vector3(0f, topY, zPos);
                decal.transform.localScale = new Vector3(W * 0.38f, 0.03f, 0.55f);
                decal.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("ForgeGrateGlow", HexColor("#FF5500"), null, 0.6f, 0f, HexColor("#FF5500") * 2f);
            }
            else
            {
                decal.transform.localPosition = new Vector3(0f, topY, zPos);
                decal.transform.localScale = new Vector3(1.6f, 0.02f, 0.32f);
                decal.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("HoloPadMat", HexColor("#00F5D4"), null, 0.9f, 0f, HexColor("#00F5D4") * 2f);
            }
        }
    }

    private void SpawnUndersideWorldDetails(Transform parent, Vector3 anchorPos, Vector3 anchorSize, int worldIndex)
    {
        if (anchorSize.x < 3f || anchorSize.z < 3f) return;

        if (worldIndex == 1) // Hanging emerald ivy tendrils
        {
            for (int i = 0; i < 4; i++)
            {
                float ox = ((i % 2 == 0) ? -1f : 1f) * (anchorSize.x * 0.44f);
                float oz = (i < 2 ? -1f : 1f) * (anchorSize.z * 0.44f);
                GameObject vine = GameObject.CreatePrimitive(PrimitiveType.Cube);
                vine.name = "HangingVine_" + i;
                vine.transform.SetParent(parent, false);
                vine.transform.localPosition = anchorPos + new Vector3(ox, -anchorSize.y * 0.3f, oz);
                vine.transform.localScale = new Vector3(0.14f, 0.95f, 0.14f);
                DestroyImmediate(vine.GetComponent<Collider>());
                vine.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("VineMat", HexColor("#16A34A"));
                if (WindSystem != null) WindSystem.RegisterItem(vine.transform, 8f, 1.8f);
            }
        }
        else if (worldIndex == 2) // Harbor pier support pilings
        {
            for (int i = 0; i < 4; i++)
            {
                float ox = ((i % 2 == 0) ? -1f : 1f) * (anchorSize.x * 0.42f);
                float oz = (i < 2 ? -1f : 1f) * (anchorSize.z * 0.42f);
                GameObject piling = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                piling.transform.SetParent(parent, false);
                piling.transform.localPosition = anchorPos + new Vector3(ox, -2.5f, oz);
                piling.transform.localScale = new Vector3(0.45f, 3.0f, 0.45f);
                DestroyImmediate(piling.GetComponent<Collider>());
                piling.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("PilingWoodMat", HexColor("#5C3316"));
            }
        }
        else if (worldIndex == 3) // Sharp translucent glacier icicles
        {
            for (int i = 0; i < 4; i++)
            {
                float ox = ((i % 2 == 0) ? -1f : 1f) * (anchorSize.x * 0.4f);
                float oz = (i < 2 ? -1f : 1f) * (anchorSize.z * 0.4f);
                GameObject icicle = new GameObject("Icicle_" + i);
                icicle.transform.SetParent(parent, false);
                icicle.transform.localPosition = anchorPos + new Vector3(ox, -anchorSize.y * 0.5f, oz);
                icicle.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
                MeshFilter mf = icicle.AddComponent<MeshFilter>();
                mf.sharedMesh = LowPolyMeshHelper.GetSharpCone(8, 0.22f, 1.15f);
                icicle.AddComponent<MeshRenderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("IcicleMat", HexColor("#BAE6FD"), null, 0.90f);
            }
        }
        else if (worldIndex == 4) // Dripping molten magma stalactites
        {
            for (int i = 0; i < 4; i++)
            {
                float ox = ((i % 2 == 0) ? -1f : 1f) * (anchorSize.x * 0.4f);
                float oz = (i < 2 ? -1f : 1f) * (anchorSize.z * 0.4f);
                GameObject stal = new GameObject("LavaStalactite_" + i);
                stal.transform.SetParent(parent, false);
                stal.transform.localPosition = anchorPos + new Vector3(ox, -anchorSize.y * 0.5f, oz);
                stal.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
                MeshFilter mf = stal.AddComponent<MeshFilter>();
                mf.sharedMesh = LowPolyMeshHelper.GetSharpCone(6, 0.18f, 0.95f);
                stal.AddComponent<MeshRenderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("LavaStalMat", HexColor("#FF5500"), null, 0.8f, 0f, HexColor("#FF4500") * 1.6f);
            }
        }
        else // World 5 Anti-gravity plasma hover ring
        {
            GameObject ring = new GameObject("HoverStabilizerRing");
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = anchorPos + Vector3.down * 0.7f;
            MeshFilter mf = ring.AddComponent<MeshFilter>();
            mf.sharedMesh = LowPolyMeshHelper.GetTorusDonut(16, 6, Mathf.Min(anchorSize.x, anchorSize.z) * 0.35f, 0.14f);
            ring.AddComponent<MeshRenderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("HoverRingMat", HexColor("#00F5D4"), null, 0.9f, 0f, HexColor("#00F5D4") * 2f);
        }
    }

    public void BuildTropicalPalmTree(Transform parent, Vector3 localPos, float scale = 1.0f)
    {
        GameObject palm = new GameObject("TropicalPalmTree");
        palm.transform.SetParent(parent, false);
        palm.transform.localPosition = localPos;
        palm.transform.localScale = Vector3.one * scale;

        // Root collar anchored solidly into turf
        GameObject rootCollar = new GameObject("PalmRootBase");
        rootCollar.transform.SetParent(palm.transform, false);
        rootCollar.transform.localPosition = new Vector3(0f, 0.14f, 0f);
        rootCollar.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshHelper.GetTaperedFrustum(0.24f, 0.38f, 0.28f, 8);
        rootCollar.AddComponent<MeshRenderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("PalmBarkDark", HexColor("#78350F"));

        // 4 tapered bark rings built directly under palm.transform (no cumulative scale squash!)
        Material barkLight = SafeMaterialFactory.GetCachedMaterial("PalmTrunkMat", HexColor("#92400E"), null, 0.25f);
        Material barkDark = SafeMaterialFactory.GetCachedMaterial("PalmBarkRing", HexColor("#78350F"), null, 0.22f);
        Vector3 prevTop = Vector3.zero;
        int segs = 4;
        float segLen = 0.85f;
        for (int s = 0; s < segs; s++)
        {
            float tiltDeg = -5f - s * 3.5f;
            Vector3 segDir = Quaternion.Euler(0f, 0f, tiltDeg) * Vector3.up;
            Vector3 nextTop = prevTop + segDir * segLen;
            Vector3 mid = (prevTop + nextTop) * 0.5f;

            float botR = Mathf.Lerp(0.26f, 0.15f, (float)s / segs);
            float topR = Mathf.Lerp(0.22f, 0.12f, (float)(s + 1) / segs);

            GameObject seg = new GameObject("PalmTrunkSeg_" + s);
            seg.transform.SetParent(palm.transform, false);
            seg.transform.localPosition = mid;
            seg.transform.localRotation = Quaternion.Euler(0f, 0f, tiltDeg);
            seg.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshHelper.GetTaperedFrustum(topR, botR, segLen + 0.06f, 8);
            seg.AddComponent<MeshRenderer>().sharedMaterial = (s % 2 == 0) ? barkLight : barkDark;

            prevTop = nextTop;
        }

        // Canopy placed at EXACT final trunk tip so it NEVER floats disconnected!
        GameObject crown = new GameObject("PalmCanopy");
        crown.transform.SetParent(palm.transform, false);
        crown.transform.localPosition = prevTop;
        if (WindSystem != null) WindSystem.RegisterItem(crown.transform, 5.5f, 1.9f);

        GameObject crownHub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        crownHub.transform.SetParent(crown.transform, false);
        crownHub.transform.localScale = new Vector3(0.42f, 0.34f, 0.42f);
        DestroyImmediate(crownHub.GetComponent<Collider>());
        crownHub.GetComponent<Renderer>().sharedMaterial = barkDark;

        Material frondMatA = SafeMaterialFactory.GetCachedMaterial("PalmFrondMat", HexColor("#22C55E"), null, 0.38f);
        Material frondMatB = SafeMaterialFactory.GetCachedMaterial("PalmFrondMatLight", HexColor("#4ADE80"), null, 0.42f);
        for (int f = 0; f < 7; f++)
        {
            GameObject frond = new GameObject("Frond_" + f);
            frond.transform.SetParent(crown.transform, false);
            frond.transform.localRotation = Quaternion.Euler(-10f, f * (360f / 7f), 0f);
            frond.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshHelper.GetArchFrondMesh();
            frond.AddComponent<MeshRenderer>().sharedMaterial = (f % 2 == 0) ? frondMatA : frondMatB;
        }
        for (int tf = 0; tf < 4; tf++)
        {
            GameObject topFrond = new GameObject("TopFrond_" + tf);
            topFrond.transform.SetParent(crown.transform, false);
            topFrond.transform.localRotation = Quaternion.Euler(-28f, tf * 90f + 22f, 0f);
            topFrond.transform.localScale = Vector3.one * 0.72f;
            topFrond.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshHelper.GetArchFrondMesh();
            topFrond.AddComponent<MeshRenderer>().sharedMaterial = frondMatB;
        }

        for (int c = 0; c < 3; c++)
        {
            float ca = c * 120f * Mathf.Deg2Rad;
            GameObject nut = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            nut.transform.SetParent(crown.transform, false);
            nut.transform.localPosition = new Vector3(Mathf.Cos(ca) * 0.22f, -0.18f, Mathf.Sin(ca) * 0.22f);
            nut.transform.localScale = Vector3.one * 0.28f;
            DestroyImmediate(nut.GetComponent<Collider>());
            nut.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("CoconutMat", HexColor("#451A03"));
        }
    }

    public void BuildLushOakTree(Transform parent, Vector3 localPos, float scale = 1.0f)
    {
        GameObject oak = new GameObject("LushMeadowOakTree");
        oak.transform.SetParent(parent, false);
        oak.transform.localPosition = localPos;
        oak.transform.localScale = Vector3.one * scale;

        Material barkMat = SafeMaterialFactory.GetCachedMaterial("OakBarkMat", HexColor("#6D3B1A"), null, 0.22f);

        GameObject trunk = new GameObject("OakTrunk");
        trunk.transform.SetParent(oak.transform, false);
        trunk.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        trunk.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshHelper.GetTaperedFrustum(0.28f, 0.46f, 2.1f, 8);
        trunk.AddComponent<MeshRenderer>().sharedMaterial = barkMat;

        for (int b = 0; b < 2; b++)
        {
            float sign = (b == 0) ? -1f : 1f;
            GameObject branch = new GameObject("OakBranch_" + b);
            branch.transform.SetParent(oak.transform, false);
            branch.transform.localPosition = new Vector3(sign * 0.38f, 1.85f, (b == 0 ? 0.14f : -0.14f));
            branch.transform.localRotation = Quaternion.Euler(0f, 0f, -sign * 34f);
            branch.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshHelper.GetTaperedFrustum(0.11f, 0.18f, 0.95f, 6);
            branch.AddComponent<MeshRenderer>().sharedMaterial = barkMat;
        }

        GameObject canopy = new GameObject("OakCanopy");
        canopy.transform.SetParent(oak.transform, false);
        canopy.transform.localPosition = new Vector3(0f, 2.55f, 0f);
        if (WindSystem != null) WindSystem.RegisterItem(canopy.transform, 4.0f, 1.5f);

        Material leafMain = SafeMaterialFactory.GetCachedMaterial("OakLeafMain", HexColor("#16A34A"), null, 0.35f);
        Material leafBright = SafeMaterialFactory.GetCachedMaterial("OakLeafBright", HexColor("#22C55E"), null, 0.42f);
        Material leafHighlight = SafeMaterialFactory.GetCachedMaterial("OakLeafHi", HexColor("#4ADE80"), null, 0.45f);

        Vector3[] lobeOffsets = new Vector3[] {
            new Vector3(0f, 0.25f, 0f),
            new Vector3(-0.78f, -0.08f, 0.25f),
            new Vector3(0.78f, -0.05f, -0.20f),
            new Vector3(0.18f, 0.05f, 0.78f),
            new Vector3(-0.15f, 0.10f, -0.78f),
            new Vector3(0f, 0.85f, 0f)
        };
        Vector3[] lobeScales = new Vector3[] {
            new Vector3(2.05f, 1.65f, 2.05f),
            new Vector3(1.38f, 1.18f, 1.38f),
            new Vector3(1.34f, 1.14f, 1.34f),
            new Vector3(1.28f, 1.10f, 1.28f),
            new Vector3(1.24f, 1.08f, 1.24f),
            new Vector3(1.32f, 1.05f, 1.32f)
        };
        for (int i = 0; i < lobeOffsets.Length; i++)
        {
            GameObject lobe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lobe.name = "OakLobe_" + i;
            lobe.transform.SetParent(canopy.transform, false);
            lobe.transform.localPosition = lobeOffsets[i];
            lobe.transform.localScale = lobeScales[i];
            DestroyImmediate(lobe.GetComponent<Collider>());
            lobe.GetComponent<Renderer>().sharedMaterial = (i == 5) ? leafHighlight : ((i % 2 == 0) ? leafMain : leafBright);
        }

        // Cute meadow berries/blossoms in the foliage!
        for (int f = 0; f < 5; f++)
        {
            float fa = f * 72f * Mathf.Deg2Rad;
            GameObject berry = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            berry.transform.SetParent(canopy.transform, false);
            berry.transform.localPosition = new Vector3(Mathf.Cos(fa) * 1.12f, 0.08f + (f % 2) * 0.35f, Mathf.Sin(fa) * 1.12f);
            berry.transform.localScale = Vector3.one * 0.22f;
            DestroyImmediate(berry.GetComponent<Collider>());
            berry.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial(
                "OakFruit_" + (f % 2), (f % 2 == 0) ? HexColor("#F472B6") : HexColor("#FACC15"), null, 0.85f);
        }
    }

    public void BuildStylizedPineTree(Transform parent, Vector3 localPos, float scale = 1.0f)
    {
        GameObject pine = new GameObject("StylizedMeadowPineTree");
        pine.transform.SetParent(parent, false);
        pine.transform.localPosition = localPos;
        pine.transform.localScale = Vector3.one * scale;

        GameObject trunk = new GameObject("PineTrunk");
        trunk.transform.SetParent(pine.transform, false);
        trunk.transform.localPosition = new Vector3(0f, 0.72f, 0f);
        trunk.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshHelper.GetTaperedFrustum(0.18f, 0.34f, 1.44f, 8);
        trunk.AddComponent<MeshRenderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("PineTrunkMat", HexColor("#78350F"), null, 0.22f);

        GameObject canopy = new GameObject("PineCanopy");
        canopy.transform.SetParent(pine.transform, false);
        canopy.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        if (WindSystem != null) WindSystem.RegisterItem(canopy.transform, 3.6f, 1.7f);

        string[] tierKeys = { "PineTier0", "PineTier1", "PineTier2" };
        Color[] tierCols = { HexColor("#15803D"), HexColor("#16A34A"), HexColor("#22C55E") };
        float[] radii = { 1.40f, 1.12f, 0.82f };
        float[] heights = { 1.55f, 1.38f, 1.25f };
        float[] tierY = { 0.75f, 1.58f, 2.38f };

        for (int t = 0; t < 3; t++)
        {
            GameObject tier = new GameObject(tierKeys[t]);
            tier.transform.SetParent(canopy.transform, false);
            tier.transform.localPosition = new Vector3(0f, tierY[t], 0f);
            tier.transform.localRotation = Quaternion.Euler(0f, t * 20f, 0f);
            tier.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshHelper.GetSharpCone(9, radii[t], heights[t]);
            tier.AddComponent<MeshRenderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial(tierKeys[t], tierCols[t], null, 0.35f);
        }
    }

    public void BuildFloweringBerryBush(Transform parent, Vector3 localPos, float scale = 1.0f)
    {
        GameObject bush = new GameObject("FloweringBerryBush");
        bush.transform.SetParent(parent, false);
        bush.transform.localPosition = localPos;
        bush.transform.localScale = Vector3.one * scale;
        if (WindSystem != null) WindSystem.RegisterItem(bush.transform, 5.0f, 2.1f);

        Material leafA = SafeMaterialFactory.GetCachedMaterial("BushLeafA", HexColor("#16A34A"), null, 0.38f);
        Material leafB = SafeMaterialFactory.GetCachedMaterial("BushLeafB", HexColor("#22C55E"), null, 0.42f);
        Vector3[] offs = { Vector3.zero + Vector3.up * 0.32f, new Vector3(-0.32f, 0.24f, 0.14f), new Vector3(0.32f, 0.24f, -0.14f) };
        float[] sz = { 0.78f, 0.56f, 0.56f };
        for (int i = 0; i < 3; i++)
        {
            GameObject puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            puff.transform.SetParent(bush.transform, false);
            puff.transform.localPosition = offs[i];
            puff.transform.localScale = Vector3.one * sz[i];
            DestroyImmediate(puff.GetComponent<Collider>());
            puff.GetComponent<Renderer>().sharedMaterial = (i == 0) ? leafA : leafB;
        }
        for (int b = 0; b < 4; b++)
        {
            float ba = b * 90f * Mathf.Deg2Rad;
            GameObject berry = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            berry.transform.SetParent(bush.transform, false);
            berry.transform.localPosition = new Vector3(Mathf.Cos(ba) * 0.36f, 0.38f, Mathf.Sin(ba) * 0.36f);
            berry.transform.localScale = Vector3.one * 0.15f;
            DestroyImmediate(berry.GetComponent<Collider>());
            berry.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial(
                "BushFruit_" + (b % 2), (b % 2 == 0) ? HexColor("#F43F5E") : HexColor("#38BDF8"), null, 0.85f);
        }
    }

    public void SpawnArtDecoNeonStreetlamp(Transform parent, Vector3 localPos, Color neonCol)
    {
        GameObject lamp = new GameObject("ViceNeonLamp");
        lamp.transform.SetParent(parent, false);
        lamp.transform.localPosition = localPos;

        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.transform.SetParent(lamp.transform, false);
        pole.transform.localPosition = new Vector3(0f, 1.55f, 0f);
        pole.transform.localScale = new Vector3(0.16f, 1.55f, 0.16f);
        DestroyImmediate(pole.GetComponent<Collider>());
        pole.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("LampPoleMat", HexColor("#334155"), null, 0.8f, 0.7f);

        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.transform.SetParent(lamp.transform, false);
        head.transform.localPosition = new Vector3(0f, 3.25f, 0f);
        head.transform.localScale = Vector3.one * 0.55f;
        DestroyImmediate(head.GetComponent<Collider>());
        head.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("NeonBulbMat_" + neonCol.GetHashCode(), neonCol, null, 0.95f, 0f, neonCol * 2.5f);

        // Additive glow sprite halo
        GameObject halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        halo.transform.SetParent(head.transform, false);
        halo.transform.localScale = Vector3.one * 4.5f;
        DestroyImmediate(halo.GetComponent<Collider>());
        halo.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedAdditiveGlowMaterial("NeonHaloMat_" + neonCol.GetHashCode(), new Color(neonCol.r, neonCol.g, neonCol.b, 0.55f), ProceduralTextureFactory.GetSoftRadialGlowTexture());
        halo.AddComponent<BillboardToCamera>();

        if (LightPool != null) LightPool.RegisterEnvLightTarget(head.transform, neonCol);
    }

    public void DecoratePlatformSafely(Transform platformParent, Vector3 size, int worldIndex)
    {
        float hw = size.x * 0.5f;
        float hd = size.z * 0.5f;
        float topY = size.y * 0.5f;

        switch (worldIndex)
        {
            case 1:
                {
                    int pat = Mathf.Abs(Mathf.RoundToInt(platformParent.position.z * 0.7f + platformParent.position.x)) % 3;
                    if (size.x >= 5.0f && size.z >= 4.5f)
                    {
                        if (pat == 0) BuildLushOakTree(platformParent, new Vector3(-hw + 0.85f, topY, hd - 0.95f), 0.78f);
                        else if (pat == 1) BuildTropicalPalmTree(platformParent, new Vector3(-hw + 0.85f, topY, hd - 0.95f), 0.85f);
                        else BuildStylizedPineTree(platformParent, new Vector3(-hw + 0.85f, topY, hd - 0.95f), 0.80f);
                    }
                    if (size.x >= 7.8f && size.z >= 7.0f)
                    {
                        // Start Sanctuary / Finish Sanctuary / Checkpoint Oasis get a matching second tree on opposite corner!
                        if (pat == 0) BuildStylizedPineTree(platformParent, new Vector3(hw - 0.9f, topY, -hd + 1.0f), 0.74f);
                        else BuildLushOakTree(platformParent, new Vector3(hw - 0.9f, topY, -hd + 1.0f), 0.72f);
                    }
                    BuildFloweringBerryBush(platformParent, new Vector3(hw - 0.8f, topY, hd - 0.95f), 0.82f);
                    SpawnGrassTuft(platformParent, new Vector3(-hw + 0.8f, topY, -hd + 1.0f));
                    SpawnGrassTuft(platformParent, new Vector3(hw - 0.8f, topY, -hd + 1.1f));
                    SpawnFlowerProp(platformParent, new Vector3(-hw + 1.0f, topY, 0f), HexColor("#EC4899"));
                    SpawnFlowerProp(platformParent, new Vector3(hw - 1.0f, topY, -1.6f), HexColor("#FDE047"));
                    SpawnPebbleProp(platformParent, new Vector3(-hw + 1.1f, topY, hd - 1.4f));
                    if (size.z >= 6.0f) SpawnGardenFence(platformParent, new Vector3(-hw + 0.35f, topY, 0f), size.z * 0.52f);
                    break;
                }

            case 2:
                SpawnArtDecoNeonStreetlamp(platformParent, new Vector3(-hw + 0.65f, topY, hd - 1.0f), HexColor("#FF2E93"));
                SpawnCondimentBottle(platformParent, new Vector3(-hw + 0.9f, topY, -hd + 1.2f), HexColor("#EF4444"));
                SpawnCondimentBottle(platformParent, new Vector3(-hw + 1.4f, topY, -hd + 1.2f), HexColor("#FACC15"));
                SpawnPierBollard(platformParent, new Vector3(hw - 0.7f, topY, -hd + 0.8f));
                SpawnPierBollard(platformParent, new Vector3(hw - 0.7f, topY, hd - 0.8f));
                SpawnCrateProp(platformParent, new Vector3(-hw + 1.0f, topY, hd - 1.5f));
                break;

            case 3:
                SpawnIceCrystalCluster(platformParent, new Vector3(-hw + 1.0f, topY, -hd + 1.2f));
                SpawnSnowdriftProp(platformParent, new Vector3(hw - 1.2f, topY, hd - 1.2f));
                SpawnCandyCanePost(platformParent, new Vector3(-hw + 0.8f, topY, hd - 1.4f));
                break;

            case 4:
                SpawnLavaVentProp(platformParent, new Vector3(0f, topY + 0.02f, -hd + 0.7f), size.x * 0.5f);
                SpawnSteelRivetProp(platformParent, new Vector3(-hw + 0.6f, topY, -hd + 0.6f));
                SpawnSteelRivetProp(platformParent, new Vector3(hw - 0.6f, topY, -hd + 0.6f));
                SpawnSteelRivetProp(platformParent, new Vector3(-hw + 0.6f, topY, hd - 0.6f));
                SpawnSteelRivetProp(platformParent, new Vector3(hw - 0.6f, topY, hd - 0.6f));
                SpawnFactoryPipe(platformParent, new Vector3(hw - 0.8f, topY, 0f));
                break;

            case 5:
                SpawnNeonPylon(platformParent, new Vector3(-hw + 0.8f, topY, -hd + 0.8f), HexColor("#00F5D4"));
                SpawnNeonPylon(platformParent, new Vector3(hw - 0.8f, topY, -hd + 0.8f), HexColor("#FF2E93"));
                SpawnNeonPylon(platformParent, new Vector3(-hw + 0.8f, topY, hd - 0.8f), HexColor("#FF2E93"));
                SpawnNeonPylon(platformParent, new Vector3(hw - 0.8f, topY, hd - 0.8f), HexColor("#00F5D4"));
                SpawnMoonCrater(platformParent, new Vector3(0f, topY + 0.02f, hd - 1.6f));
                break;
        }

        foreach (Collider c in platformParent.GetComponentsInChildren<Collider>())
        {
            if (c.gameObject != platformParent.gameObject) DestroyImmediate(c);
        }
    }

    public void SpawnGrassTuft(Transform parent, Vector3 localPos)
    {
        GameObject tuft = new GameObject("GrassTuft");
        tuft.transform.SetParent(parent, false);
        tuft.transform.localPosition = localPos;
        Color gCol1 = HexColor("#22C55E");
        Color gCol2 = HexColor("#4ADE80");

        for (int i = 0; i < 4; i++)
        {
            GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.transform.SetParent(tuft.transform, false);
            blade.transform.localPosition = new Vector3((i - 1.5f) * 0.12f, 0.22f, 0f);
            blade.transform.localRotation = Quaternion.Euler(0f, i * 45f, (i % 2 == 0 ? -1 : 1) * (15f + i * 5f));
            blade.transform.localScale = new Vector3(0.06f, 0.44f, 0.06f);
            DestroyImmediate(blade.GetComponent<Collider>());
            blade.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("BladeMat_" + (i % 2), i % 2 == 0 ? gCol1 : gCol2);
        }
        if (WindSystem != null) WindSystem.RegisterItem(tuft.transform, 10f, 2.8f);
    }

    public void SpawnFlowerProp(Transform parent, Vector3 localPos, Color petalColor)
    {
        GameObject flower = new GameObject("FlowerProp");
        flower.transform.SetParent(parent, false);
        flower.transform.localPosition = localPos;

        GameObject stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stem.transform.SetParent(flower.transform, false);
        stem.transform.localPosition = new Vector3(0f, 0.25f, 0f);
        stem.transform.localScale = new Vector3(0.06f, 0.25f, 0.06f);
        DestroyImmediate(stem.GetComponent<Collider>());
        stem.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("StemMat", HexColor("#16A34A"));

        GameObject center = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        center.transform.SetParent(flower.transform, false);
        center.transform.localPosition = new Vector3(0f, 0.52f, 0f);
        center.transform.localScale = Vector3.one * 0.18f;
        DestroyImmediate(center.GetComponent<Collider>());
        center.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("FlowerCenter", HexColor("#FACC15"));

        for (int p = 0; p < 5; p++)
        {
            float ang = p * 72f * Mathf.Deg2Rad;
            GameObject petal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            petal.transform.SetParent(flower.transform, false);
            petal.transform.localPosition = new Vector3(Mathf.Cos(ang) * 0.16f, 0.52f, Mathf.Sin(ang) * 0.16f);
            petal.transform.localScale = new Vector3(0.14f, 0.06f, 0.14f);
            DestroyImmediate(petal.GetComponent<Collider>());
            petal.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("PetalMat_" + petalColor.GetHashCode(), petalColor);
        }
        if (WindSystem != null) WindSystem.RegisterItem(flower.transform, 7f, 2.4f);
    }

    public void SpawnPebbleProp(Transform parent, Vector3 localPos)
    {
        GameObject pebble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        pebble.name = "Pebble";
        pebble.transform.SetParent(parent, false);
        pebble.transform.localPosition = localPos + Vector3.up * 0.08f;
        pebble.transform.localScale = new Vector3(0.35f, 0.16f, 0.28f);
        pebble.transform.localRotation = Quaternion.Euler(15f, 35f, 0f);
        DestroyImmediate(pebble.GetComponent<Collider>());
        pebble.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("PebbleMat", HexColor("#94A3B8"));
    }

    public void SpawnGardenFence(Transform parent, Vector3 localPos, float length)
    {
        GameObject fence = new GameObject("GardenFence");
        fence.transform.SetParent(parent, false);
        fence.transform.localPosition = localPos;
        int posts = Mathf.Max(2, Mathf.RoundToInt(length / 1.2f));

        for (int i = 0; i < posts; i++)
        {
            float z = (i - (posts - 1) * 0.5f) * 1.2f;
            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.transform.SetParent(fence.transform, false);
            post.transform.localPosition = new Vector3(0f, 0.45f, z);
            post.transform.localScale = new Vector3(0.12f, 0.90f, 0.12f);
            DestroyImmediate(post.GetComponent<Collider>());
            post.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("FenceWhite", Color.white);
        }

        for (int r = 0; r < 2; r++)
        {
            GameObject rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rail.transform.SetParent(fence.transform, false);
            rail.transform.localPosition = new Vector3(0f, 0.35f + r * 0.35f, 0f);
            rail.transform.localScale = new Vector3(0.08f, 0.08f, length);
            DestroyImmediate(rail.GetComponent<Collider>());
            rail.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("FenceWhite", Color.white);
        }
    }

    public void SpawnCondimentBottle(Transform parent, Vector3 localPos, Color bottleCol)
    {
        GameObject bottle = new GameObject("CondimentBottle");
        bottle.transform.SetParent(parent, false);
        bottle.transform.localPosition = localPos;

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        body.transform.SetParent(bottle.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.35f, 0f);
        body.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
        DestroyImmediate(body.GetComponent<Collider>());
        body.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("BottleMat_" + bottleCol.GetHashCode(), bottleCol, null, 0.85f);

        GameObject nozzle = new GameObject("Nozzle");
        nozzle.transform.SetParent(bottle.transform, false);
        nozzle.transform.localPosition = new Vector3(0f, 0.70f, 0f);
        MeshFilter mf = nozzle.AddComponent<MeshFilter>();
        mf.sharedMesh = LowPolyMeshHelper.GetSharpCone(8, 0.12f, 0.30f);
        nozzle.AddComponent<MeshRenderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("NozzleWhite", Color.white);
    }

    public void SpawnPierBollard(Transform parent, Vector3 localPos)
    {
        GameObject bollard = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bollard.name = "PierBollard";
        bollard.transform.SetParent(parent, false);
        bollard.transform.localPosition = localPos + Vector3.up * 0.40f;
        bollard.transform.localScale = new Vector3(0.40f, 0.40f, 0.40f);
        DestroyImmediate(bollard.GetComponent<Collider>());
        bollard.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("BollardMat", HexColor("#451A03"), ProceduralTextureFactory.GetDinerWoodDockTexture());
    }

    public void SpawnCrateProp(Transform parent, Vector3 localPos)
    {
        GameObject crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        crate.name = "CrateProp";
        crate.transform.SetParent(parent, false);
        crate.transform.localPosition = localPos + Vector3.up * 0.45f;
        crate.transform.localScale = Vector3.one * 0.90f;
        crate.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);
        DestroyImmediate(crate.GetComponent<Collider>());
        crate.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("CrateMat", HexColor("#9A5B2C"), ProceduralTextureFactory.GetDinerWoodDockTexture());
    }

    public void SpawnIceCrystalCluster(Transform parent, Vector3 localPos)
    {
        GameObject cluster = new GameObject("IceCrystalCluster");
        cluster.transform.SetParent(parent, false);
        cluster.transform.localPosition = localPos;

        for (int i = 0; i < 3; i++)
        {
            GameObject shard = new GameObject("Shard_" + i);
            shard.transform.SetParent(cluster.transform, false);
            shard.transform.localPosition = new Vector3((i - 1) * 0.22f, 0f, 0f);
            shard.transform.localRotation = Quaternion.Euler((i % 2 == 0 ? -1 : 1) * 12f, i * 40f, (i - 1) * 14f);
            MeshFilter mf = shard.AddComponent<MeshFilter>();
            mf.sharedMesh = LowPolyMeshHelper.GetSharpCone(6, 0.18f + i * 0.05f, 0.85f + i * 0.25f);
            shard.AddComponent<MeshRenderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("CrystalMat", HexColor("#BAE6FD"), null, 0.92f, 0f, HexColor("#38BDF8"));
        }
    }

    public void SpawnSnowdriftProp(Transform parent, Vector3 localPos)
    {
        GameObject drift = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        drift.name = "Snowdrift";
        drift.transform.SetParent(parent, false);
        drift.transform.localPosition = localPos + Vector3.up * 0.12f;
        drift.transform.localScale = new Vector3(1.4f, 0.35f, 0.9f);
        DestroyImmediate(drift.GetComponent<Collider>());
        drift.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("SnowMat", HexColor("#F8FAFC"), null, 0.20f);
    }

    public void SpawnCandyCanePost(Transform parent, Vector3 localPos)
    {
        GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = "CandyCane";
        post.transform.SetParent(parent, false);
        post.transform.localPosition = localPos + Vector3.up * 0.60f;
        post.transform.localScale = new Vector3(0.18f, 0.60f, 0.18f);
        DestroyImmediate(post.GetComponent<Collider>());
        post.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("CandyCaneMat", HexColor("#EF4444"), ProceduralTextureFactory.GetCandyCaneTexture());
    }

    public void SpawnLavaVentProp(Transform parent, Vector3 localPos, float length)
    {
        GameObject vent = GameObject.CreatePrimitive(PrimitiveType.Cube);
        vent.name = "LavaVent";
        vent.transform.SetParent(parent, false);
        vent.transform.localPosition = localPos;
        vent.transform.localScale = new Vector3(0.35f, 0.05f, length);
        DestroyImmediate(vent.GetComponent<Collider>());
        vent.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("LavaVentMat", HexColor("#FF4500"), null, 0.8f, 0f, HexColor("#FF4500") * 2f);
        if (LightPool != null) LightPool.RegisterEnvLightTarget(vent.transform, HexColor("#FF5500"));
    }

    public void SpawnSteelRivetProp(Transform parent, Vector3 localPos)
    {
        GameObject rivet = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rivet.name = "SteelRivet";
        rivet.transform.SetParent(parent, false);
        rivet.transform.localPosition = localPos + Vector3.up * 0.05f;
        rivet.transform.localScale = new Vector3(0.22f, 0.06f, 0.22f);
        DestroyImmediate(rivet.GetComponent<Collider>());
        rivet.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("RivetMat", HexColor("#94A3B8"), null, 0.85f, 0.8f);
    }

    public void SpawnFactoryPipe(Transform parent, Vector3 localPos)
    {
        GameObject pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pipe.name = "FactoryPipe";
        pipe.transform.SetParent(parent, false);
        pipe.transform.localPosition = localPos + Vector3.up * 0.50f;
        pipe.transform.localScale = new Vector3(0.25f, 0.50f, 0.25f);
        DestroyImmediate(pipe.GetComponent<Collider>());
        pipe.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("PipeMat", HexColor("#475569"), null, 0.65f, 0.5f);
    }

    public void SpawnNeonPylon(Transform parent, Vector3 localPos, Color pylonCol)
    {
        GameObject pylon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pylon.name = "NeonPylon";
        pylon.transform.SetParent(parent, false);
        pylon.transform.localPosition = localPos + Vector3.up * 0.65f;
        pylon.transform.localScale = new Vector3(0.22f, 0.65f, 0.22f);
        DestroyImmediate(pylon.GetComponent<Collider>());
        pylon.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("PylonMat_" + pylonCol.GetHashCode(), pylonCol, null, 0.9f, 0f, pylonCol * 1.8f);

        GameObject halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        halo.transform.SetParent(pylon.transform, false);
        halo.transform.localPosition = new Vector3(0f, 1.0f, 0f);
        halo.transform.localScale = Vector3.one * 4.5f;
        DestroyImmediate(halo.GetComponent<Collider>());
        halo.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedAdditiveGlowMaterial("PylonHalo_" + pylonCol.GetHashCode(), new Color(pylonCol.r, pylonCol.g, pylonCol.b, 0.5f), ProceduralTextureFactory.GetSoftRadialGlowTexture());
        halo.AddComponent<BillboardToCamera>();

        if (LightPool != null) LightPool.RegisterEnvLightTarget(pylon.transform, pylonCol);
    }

    public void SpawnMoonCrater(Transform parent, Vector3 localPos)
    {
        GameObject crater = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        crater.name = "MoonCrater";
        crater.transform.SetParent(parent, false);
        crater.transform.localPosition = localPos;
        crater.transform.localScale = new Vector3(0.85f, 0.04f, 0.85f);
        DestroyImmediate(crater.GetComponent<Collider>());
        crater.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("CraterMat", HexColor("#334155"));
    }

    public void SpawnCosmicBackgroundPlanets()
    {
        GameObject p1 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        p1.name = "CosmicPlanet_1";
        p1.transform.SetParent(StageRoot, false);
        p1.transform.position = new Vector3(65f, 32f, 130f);
        p1.transform.localScale = Vector3.one * 18f;
        DestroyImmediate(p1.GetComponent<Collider>());
        p1.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("Planet1Mat", HexColor("#A855F7"), null, 0.6f);

        GameObject ring1 = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring1.transform.SetParent(p1.transform, false);
        ring1.transform.localRotation = Quaternion.Euler(25f, 0f, 35f);
        ring1.transform.localScale = new Vector3(1.8f, 0.02f, 1.8f);
        DestroyImmediate(ring1.GetComponent<Collider>());
        ring1.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("Ring1Mat", HexColor("#00F5D4"), null, 0.7f, 0f, HexColor("#00F5D4"));

        GameObject p2 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        p2.name = "CosmicPlanet_2";
        p2.transform.SetParent(StageRoot, false);
        p2.transform.position = new Vector3(-70f, 28f, 110f);
        p2.transform.localScale = Vector3.one * 12f;
        DestroyImmediate(p2.GetComponent<Collider>());
        p2.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("Planet2Mat", HexColor("#FBBF24"), null, 0.6f);

        for (int i = 0; i < 180; i++)
        {
            Vector3 sp = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(130f, 260f);
            if (sp.y < -15f) sp.y = -sp.y;
            GameObject star = GameObject.CreatePrimitive(PrimitiveType.Cube);
            star.name = "StarField_" + i;
            star.transform.SetParent(StageRoot, false);
            star.transform.position = sp;
            star.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.7f, 1.9f);
            DestroyImmediate(star.GetComponent<Collider>());
            star.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("StarFieldMat", Color.white, null, 1f, 0f, Color.white);
        }
    }

    public GameObject CreatePlatform(Vector3 pos, Vector3 size, Color col, string name = "Platform")
    {
        return CreateDetailedPlatform(pos, size, CurrentWorld, PlatformKind.Static, name);
    }

    public GameObject CreateGoldenStar(Vector3 pos, int level)
    {
        GameObject star = new GameObject("GoldenStar_" + level);
        star.transform.SetParent(StageRoot);
        star.transform.position = pos;

        MeshFilter mf = star.AddComponent<MeshFilter>();
        mf.sharedMesh = LowPolyMeshHelper.GetFivePointStar(0.65f, 0.28f, 0.20f);
        MeshRenderer mr = star.AddComponent<MeshRenderer>();
        mr.sharedMaterial = SafeMaterialFactory.GetCachedMaterial("GoldenStarMat", HexColor("#FFD700"), null, 0.90f, 0.78f, HexColor("#B45309"));

        GameObject halo = new GameObject("StarHalo");
        halo.transform.SetParent(star.transform, false);
        halo.transform.localRotation = Quaternion.Euler(65f, 0f, 25f);
        MeshFilter mfH = halo.AddComponent<MeshFilter>();
        mfH.sharedMesh = LowPolyMeshHelper.GetTorusDonut(18, 8, 1.15f, 0.08f);
        halo.AddComponent<MeshRenderer>().sharedMaterial = mr.sharedMaterial;

        GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
        glow.transform.SetParent(star.transform, false);
        glow.transform.localScale = Vector3.one * 3.2f;
        DestroyImmediate(glow.GetComponent<Collider>());
        glow.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedAdditiveGlowMaterial("StarGlowMat", new Color(1f, 0.85f, 0.2f, 0.55f), ProceduralTextureFactory.GetSoftRadialGlowTexture());
        glow.AddComponent<BillboardToCamera>();

        SphereCollider sc = star.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = 1.2f;

        GoldenStar comp = star.AddComponent<GoldenStar>();
        comp.LevelNumber = level;
        if (LightPool != null) LightPool.RegisterEnvLightTarget(star.transform, HexColor("#FDE047"));

        return star;
    }

    public GameObject CreateCheckpoint(Vector3 pos)
    {
        GameObject flag = new GameObject("CheckpointFlag");
        flag.transform.SetParent(StageRoot);
        flag.transform.position = pos;

        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.transform.SetParent(flag.transform, false);
        pole.transform.localPosition = new Vector3(0f, 2.5f, 0f);
        pole.transform.localScale = new Vector3(0.18f, 2.5f, 0.18f);
        DestroyImmediate(pole.GetComponent<Collider>());
        pole.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("PoleMat", Color.white, null, 0.85f, 0.8f);

        GameObject finial = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        finial.transform.SetParent(flag.transform, false);
        finial.transform.localPosition = new Vector3(0f, 5.1f, 0f);
        finial.transform.localScale = Vector3.one * 0.45f;
        DestroyImmediate(finial.GetComponent<Collider>());
        finial.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("FinialMat", HexColor("#FFD700"), null, 0.9f, 0.8f, HexColor("#B45309"));

        GameObject banner = GameObject.CreatePrimitive(PrimitiveType.Cube);
        banner.transform.SetParent(flag.transform, false);
        banner.transform.localPosition = new Vector3(0.85f, 4.2f, 0f);
        banner.transform.localScale = new Vector3(1.5f, 1.0f, 0.08f);
        DestroyImmediate(banner.GetComponent<Collider>());
        banner.GetComponent<Renderer>().material = Aadish3DGame.GetSafeMaterial(HexColor("#EF4444"), "BannerRed");
        if (WindSystem != null) WindSystem.RegisterItem(banner.transform, 10f, 3.2f);

        BoxCollider bc = flag.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.size = new Vector3(2.5f, 5f, 2.5f);
        bc.center = new Vector3(0f, 2.5f, 0f);

        CheckpointComp comp = flag.AddComponent<CheckpointComp>();
        comp.BannerRenderer = banner.GetComponent<Renderer>();
        return flag;
    }

    public GameObject CreateMiniBerryObject(Vector3 pos, int level)
    {
        GameObject mb = new GameObject("MiniBerry_L" + level);
        mb.transform.SetParent(StageRoot);
        mb.transform.position = pos;

        int idx = Mathf.Clamp(level - 1, 0, 49);
        Color fruitCol = HexColor(BerryHexColors[idx]);
        float sz = 0.82f + ((level % 5) * 0.07f);
        mb.transform.localScale = Vector3.one * sz;

        for (int i = 0; i < 7; i++)
        {
            float a = i * (Mathf.PI * 2f / 6f);
            Vector3 offset = (i == 6) ? new Vector3(0f, 0.6f, 0f) : new Vector3(Mathf.Cos(a) * 0.35f, 0.35f, Mathf.Sin(a) * 0.35f);
            GameObject bump = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bump.transform.SetParent(mb.transform, false);
            bump.transform.localPosition = offset;
            bump.transform.localScale = Vector3.one * 0.55f;
            DestroyImmediate(bump.GetComponent<Collider>());
            bump.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("MiniBerryMat_" + fruitCol.GetHashCode(), fruitCol, null, 0.85f);
        }

        for (int l = 0; l < 4; l++)
        {
            float la = l * 90f * Mathf.Deg2Rad;
            GameObject leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leaf.transform.SetParent(mb.transform, false);
            leaf.transform.localPosition = new Vector3(Mathf.Cos(la) * 0.22f, 0.95f, Mathf.Sin(la) * 0.22f);
            leaf.transform.localScale = new Vector3(0.20f, 0.08f, 0.28f);
            leaf.transform.localRotation = Quaternion.Euler(25f, l * 90f, 0f);
            DestroyImmediate(leaf.GetComponent<Collider>());
            leaf.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("LeafMat", HexColor("#4CD936"));
        }

        AttachCartoonFace(mb.transform, new Vector3(0f, 0.52f, 0.38f), 0.16f);

        BoxCollider bc = mb.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.size = new Vector3(2.2f, 2.2f, 2.2f);
        bc.center = new Vector3(0f, 0.8f, 0f);

        MiniBerry comp = mb.AddComponent<MiniBerry>();
        comp.LevelNumber = level;
        comp.KidName = BerryNames[idx];
        comp.KidColor = fruitCol;
        return mb;
    }

    public GameObject CreateHazardFloor(int world)
    {
        GameObject haz = new GameObject("HazardFloorTrigger");
        haz.transform.SetParent(StageRoot);
        haz.transform.position = new Vector3(0f, -15.5f, 160f);

        BoxCollider bc = haz.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.size = new Vector3(520f, 4f, 650f);

        haz.AddComponent<HazardFloorComp>();
        return haz;
    }

    public GameObject CreateDonutSpring(Vector3 pos, float bounceForce = 24f)
    {
        GameObject spring = new GameObject("DonutSpring");
        spring.transform.SetParent(StageRoot);
        spring.transform.position = pos;

        GameObject visual = new GameObject("DonutVisual");
        visual.transform.SetParent(spring.transform, false);

        GameObject dough = new GameObject("DonutDough");
        dough.transform.SetParent(visual.transform, false);
        dough.transform.localPosition = new Vector3(0f, 0.35f, 0f);
        MeshFilter mfD = dough.AddComponent<MeshFilter>();
        mfD.sharedMesh = LowPolyMeshHelper.GetTorusDonut(18, 10, 1.15f, 0.38f);
        dough.AddComponent<MeshRenderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("DonutDoughMat", HexColor("#F59E0B"), ProceduralTextureFactory.GetToastedBunTexture(), 0.32f);

        GameObject frost = new GameObject("DonutFrosting");
        frost.transform.SetParent(visual.transform, false);
        frost.transform.localPosition = new Vector3(0f, 0.44f, 0f);
        MeshFilter mfF = frost.AddComponent<MeshFilter>();
        mfF.sharedMesh = LowPolyMeshHelper.GetTorusDonut(18, 10, 1.12f, 0.28f);
        frost.AddComponent<MeshRenderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("DonutFrostMat", HexColor("#F472B6"), null, 0.88f);

        Color[] sprinkleCols = new Color[] {
            HexColor("#EF4444"), HexColor("#38BDF8"), HexColor("#10B981"), HexColor("#FACC15"), HexColor("#A855F7")
        };
        for (int i = 0; i < 14; i++)
        {
            float ang = i * (Mathf.PI * 2f / 14f);
            GameObject sp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            sp.transform.SetParent(visual.transform, false);
            sp.transform.localPosition = new Vector3(Mathf.Cos(ang) * 1.12f, 0.72f, Mathf.Sin(ang) * 1.12f);
            sp.transform.localRotation = Quaternion.Euler(0f, (i * 47f) % 360f, 0f);
            sp.transform.localScale = new Vector3(0.08f, 0.04f, 0.20f);
            DestroyImmediate(sp.GetComponent<Collider>());
            sp.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("SprinkleMat_" + (i % 5), sprinkleCols[i % sprinkleCols.Length], null, 0.75f);
        }

        BoxCollider trig = spring.AddComponent<BoxCollider>();
        trig.isTrigger = true;
        trig.size = new Vector3(2.8f, 1.5f, 2.8f);
        trig.center = new Vector3(0f, 0.7f, 0f);

        DonutSpringComp comp = spring.AddComponent<DonutSpringComp>();
        comp.BounceForce = bounceForce;
        comp.VisualRoot = visual.transform;

        return spring;
    }

    void Update()
    {
        if (SkyDomeTransform != null && MainCam != null)
        {
            SkyDomeTransform.position = MainCam.transform.position;
        }

        if (m_TitleAnimRt != null && PanelMainMenu != null && PanelMainMenu.activeSelf)
        {
            float s = 1.0f + Mathf.Sin(Time.unscaledTime * 2.0f) * 0.03f;
            m_TitleAnimRt.localScale = new Vector3(s, s, 1.0f);
        }

        if (ScreenFXVignetteImg != null)
        {
            if (m_DamageFlashAlpha > 0f) m_DamageFlashAlpha = Mathf.MoveTowards(m_DamageFlashAlpha, 0f, Time.unscaledDeltaTime * 2.2f);
            float enragePulse = (BossAlive && ActiveBoss != null && ActiveBoss.IsPhase2Enraged)
                ? (Mathf.Sin(Time.time * 6f) * 0.12f + 0.18f) : 0f;
            ScreenFXVignetteImg.color = new Color(0.95f, 0.1f, 0.2f, Mathf.Clamp01(m_DamageFlashAlpha + enragePulse));
        }

        // Boss Arena Gate Encounter Check
        if (CurrentState == GameState.Playing && InBossFight && !BossDialogueDone && Player != null && ActiveBoss != null)
        {
            if (Player.transform.position.z >= -19.5f)
            {
                TriggerBossGateEncounter();
            }
        }

        // Boss Chip-Damage Trailing Bar + Offscreen Boss Warning Indicator
        if (BossHealthContainer != null && BossHealthContainer.activeSelf)
        {
            float targetRatio = Mathf.Clamp01((float)BossCurrentHP / Mathf.Max(1, BossMaxHP));
            m_BossChipRatio = Mathf.MoveTowards(m_BossChipRatio, targetRatio, Time.deltaTime * 0.45f);
            if (BossChipDamageFill != null) BossChipDamageFill.localScale = new Vector3(m_BossChipRatio, 1f, 1f);
        }

        if (OffscreenBossIndicator != null)
        {
            if (CurrentState == GameState.Playing && BossAlive && ActiveBoss != null && MainCam != null)
            {
                Vector3 vp = MainCam.WorldToViewportPoint(ActiveBoss.transform.position + Vector3.up * 2f);
                bool onScreen = (vp.z > 0f && vp.x >= 0.05f && vp.x <= 0.95f && vp.y >= 0.05f && vp.y <= 0.95f);
                if (!onScreen)
                {
                    OffscreenBossIndicator.gameObject.SetActive(true);
                    if (vp.z < 0f) { vp.x = 1f - vp.x; vp.y = 0.08f; }
                    Vector2 dir = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
                    if (dir.sqrMagnitude < 0.001f) dir = Vector2.down;
                    dir.Normalize();
                    OffscreenBossIndicator.anchoredPosition = dir * 420f;
                    float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                    OffscreenBossIndicator.localRotation = Quaternion.Euler(0f, 0f, ang);
                }
                else OffscreenBossIndicator.gameObject.SetActive(false);
            }
            else OffscreenBossIndicator.gameObject.SetActive(false);
        }

        // Progress Bar Update
        if (CurrentState == GameState.Playing && Player != null && !InBossFight && ProgressBarRoot != null && ProgressBarRoot.activeSelf)
        {
            float totalDist = Mathf.Max(10f, LevelFinishZ);
            float currentDist = Mathf.Clamp(Player.transform.position.z, 0f, totalDist);
            float pct = Mathf.Clamp01(currentDist / totalDist);

            if (ProgressBarFillImage != null) ProgressBarFillImage.fillAmount = pct;
            if (ProgressBarAvatarPin != null) ProgressBarAvatarPin.anchoredPosition = new Vector2(Mathf.Lerp(-210f, 210f, pct), 0f);
            if (ProgressBarStarPinImg != null)
            {
                ProgressBarStarPinImg.sprite = ProgressionManager.IsStarCollected(CurrentLevel) ? ProceduralUISpriteFactory.GetStarFilledSprite() : ProceduralUISpriteFactory.GetStarEmptySprite();
            }

            LevelLengthSpec spec = LevelLengthCalculator.GetSpec(CurrentLevel);
            if (ProgressBarReadoutText != null)
            {
                ProgressBarReadoutText.text = "LEVEL " + CurrentLevel + " (" + spec.LengthTierLabel + ") • " + ((int)currentDist) + "m / " + ((int)totalDist) + "m (" + ((int)(pct * 100f)) + "%)";
            }
        }

        HandleMenuKeyboardNavigation();
    }

    void HandleMenuKeyboardNavigation()
    {
        try
        {
            if (CurrentState == GameState.Dialogue)
            {
                if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
                {
                    AdvanceDialogue();
                }
                return;
            }
            if (CurrentState == GameState.WorldSelect)
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) SelectPreviousWorld();
                else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) SelectNextWorld();
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) EnterFocusedWorld();
                else if (Input.GetKeyDown(KeyCode.Escape)) GoToMainMenu();
            }
            else if (CurrentState == GameState.LevelSelect)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) GoToWorldSelect();
            }
            else if (CurrentState == GameState.Playing)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) PauseGame();
            }
            else if (CurrentState == GameState.Paused)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) ResumeGame();
            }
        }
        catch { }
    }

    public static Color HexColor(string hex)
    {
        Color c;
        if (ColorUtility.TryParseHtmlString(hex, out c)) return c;
        return Color.white;
    }

    public static Font GetSafeFont()
    {
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
        if (f == null) { try { f = Font.CreateDynamicFontFromOSFont("Arial", 24); } catch { } }
        return f;
    }

    public static Text CreateUIText(Transform parent, string text, int fontSize, TextAnchor alignment, Vector2 anchoredPos, Vector2 size, Color col, bool addEffects = true)
    {
        GameObject go = new GameObject("Text_" + (text.Length > 8 ? text.Substring(0, 8) : text));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        Text t = go.AddComponent<Text>();
        t.font = GetSafeFont();
        t.fontSize = fontSize;
        t.text = text;
        t.alignment = alignment;
        t.color = col;
        t.fontStyle = FontStyle.Bold;

        if (addEffects)
        {
            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.03f, 0.06f, 0.16f, 0.95f);
            outline.effectDistance = new Vector2(2f, -2f);
        }
        return t;
    }

    public static GameObject CreateUIButton(Transform parent, string label, Vector2 size, Vector2 anchoredPos, Color bgColor, Action onClick, int fontSize = 24)
    {
        GameObject root = new GameObject("Btn_" + label);
        root.transform.SetParent(parent, false);
        RectTransform rootRt = root.AddComponent<RectTransform>();
        rootRt.sizeDelta = size;
        rootRt.anchoredPosition = anchoredPos;

        Image img = root.AddComponent<Image>();
        img.sprite = ProceduralUISpriteFactory.GetGlossyButtonSprite();
        img.type = Image.Type.Sliced;
        img.color = bgColor;

        Button btn = root.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        root.AddComponent<UIBouncyButton>();

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(root.transform, false);
        RectTransform trt = textGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.sizeDelta = Vector2.zero;
        Text txt = textGo.AddComponent<Text>();
        txt.font = GetSafeFont();
        txt.fontSize = fontSize;
        txt.text = label;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        txt.fontStyle = FontStyle.Bold;

        Outline outline = textGo.AddComponent<Outline>();
        outline.effectColor = new Color(0.04f, 0.08f, 0.16f, 0.95f);
        outline.effectDistance = new Vector2(2f, -2f);
        return root;
    }

    public static GameObject CreateCardPanel(Transform parent, Vector2 size, Vector2 anchoredPos, Color bgColor, Color borderColor, float borderWidth = 3f)
    {
        GameObject cardGo = new GameObject("CardPanel");
        cardGo.transform.SetParent(parent, false);
        RectTransform rt = cardGo.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        Image borderImg = cardGo.AddComponent<Image>();
        borderImg.sprite = ProceduralUISpriteFactory.GetRoundedCardSprite();
        borderImg.type = Image.Type.Sliced;
        borderImg.color = borderColor;

        GameObject innerGo = new GameObject("InnerFill");
        innerGo.transform.SetParent(cardGo.transform, false);
        RectTransform irt = innerGo.AddComponent<RectTransform>();
        irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
        irt.sizeDelta = new Vector2(-borderWidth * 2f, -borderWidth * 2f);
        irt.anchoredPosition = Vector2.zero;

        Image innerImg = innerGo.AddComponent<Image>();
        innerImg.sprite = ProceduralUISpriteFactory.GetRoundedCardSprite();
        innerImg.type = Image.Type.Sliced;
        innerImg.color = bgColor;

        return cardGo;
    }
}

// ===========================================================================
// PART C: CAMERA, BERRY DAD CONTROLLER, SWEEPING BULLETS, 8-STATE BOSS PURSUIT BRAIN & COURSE ENTITIES
// ===========================================================================

public enum MenuCameraState
{
    MainMenu,
    WorldSelect,
    LevelSelect,
    Settings,
    HowToPlay
}

public class CameraController : MonoBehaviour
{
    public Transform Target;
    public Vector3 Offset = new Vector3(0f, 6.8f, -9.5f);
    public float SmoothSpeed = 8.5f;
    public bool IsOrbitingMenu = true;
    public MenuCameraState MenuState = MenuCameraState.MainMenu;

    private Camera m_Cam;
    private float m_MenuAngle = 0f;
    private float m_ShakeTimer = 0f;
    private float m_ShakeIntensity = 0f;
    private Vector3 m_SmoothLookTarget = Vector3.zero;
    private bool m_HasLookInit = false;

    void Awake()
    {
        m_Cam = GetComponent<Camera>();
        if (m_Cam == null) m_Cam = gameObject.AddComponent<Camera>();
        m_Cam.fieldOfView = 60f;
        m_Cam.nearClipPlane = 0.3f;
        m_Cam.farClipPlane = 450f;
    }

    public void SetMenuCameraState(MenuCameraState state)
    {
        MenuState = state;
        IsOrbitingMenu = true;
        m_HasLookInit = false;
    }

    public void Shake(float duration, float intensity)
    {
        m_ShakeTimer = Mathf.Max(m_ShakeTimer, duration);
        m_ShakeIntensity = Mathf.Max(m_ShakeIntensity, intensity);
    }

    public void TriggerScreenShake(float intensity, float duration)
    {
        Shake(duration, intensity);
    }

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        if (m_Cam == null) m_Cam = GetComponent<Camera>();

        if (IsOrbitingMenu)
        {
            m_MenuAngle += dt * 11.5f;
            float rad = m_MenuAngle * Mathf.Deg2Rad;
            Vector3 desiredPos;
            Vector3 lookAt;
            float targetFov = 58f;

            switch (MenuState)
            {
                case MenuCameraState.WorldSelect:
                    desiredPos = new Vector3(Mathf.Sin(rad) * 10.2f, 5.2f, -Mathf.Cos(rad) * 10.2f);
                    lookAt = new Vector3(0f, 2.1f, 0f);
                    targetFov = 56f;
                    break;
                case MenuCameraState.LevelSelect:
                    desiredPos = new Vector3(Mathf.Sin(rad * 0.7f) * 11.8f, 6.8f, -Mathf.Cos(rad * 0.7f) * 11.8f);
                    lookAt = new Vector3(0f, 1.8f, 0f);
                    targetFov = 58f;
                    break;
                case MenuCameraState.Settings:
                case MenuCameraState.HowToPlay:
                    desiredPos = new Vector3(Mathf.Sin(rad * 0.6f) * 13.5f, 8.2f, -Mathf.Cos(rad * 0.6f) * 13.5f);
                    lookAt = new Vector3(0f, 1.5f, 0f);
                    targetFov = 60f;
                    break;
                default:
                    desiredPos = new Vector3(Mathf.Sin(rad * 0.85f) * 10.8f, 5.8f, -Mathf.Cos(rad * 0.85f) * 10.8f);
                    lookAt = new Vector3(0f, 1.9f, 0f);
                    targetFov = 58f;
                    break;
            }

            transform.position = Vector3.Lerp(transform.position, desiredPos, Mathf.Clamp01(dt * 4.5f));
            transform.LookAt(lookAt);
            if (m_Cam != null) m_Cam.fieldOfView = Mathf.Lerp(m_Cam.fieldOfView, targetFov, Mathf.Clamp01(dt * 5f));
            return;
        }

        if (Target == null) return;

        Vector3 playerPos = Target.position;
        Vector3 desiredCamOffset = Offset;
        Vector3 lookTarget = playerPos + new Vector3(0f, 1.45f, 1.2f);
        float desiredFov = 60f;

        Aadish3DGame gm = Aadish3DGame.Instance;
        if (gm != null && gm.InBossFight && gm.BossAlive && gm.ActiveBoss != null)
        {
            Vector3 bossPos = gm.ActiveBoss.transform.position;
            float distBetween = Vector3.Distance(playerPos, bossPos);
            float pullBack = Mathf.Clamp((distBetween - 9f) * 0.28f, 0f, 5.2f);

            // Dual-subject Boss Chase Framing: keeps both Berry Dad & towering pursuing Boss visible
            lookTarget = Vector3.Lerp(playerPos, bossPos, 0.34f) + new Vector3(0f, 1.85f, 0f);
            desiredCamOffset = new Vector3(0f, 9.4f + pullBack * 0.55f, -12.6f - pullBack);

            if (gm.ActiveBoss.IsCharging || gm.ActiveBoss.IsPhase2Enraged)
                desiredFov = 66f;
            else
                desiredFov = 62f;
        }

        Vector3 targetCamPos = playerPos + desiredCamOffset;
        if (gm != null && gm.InBossFight && gm.BossAlive && gm.ActiveBoss != null)
        {
            // Anchor Boss camera subtly toward arena mid while preserving player responsiveness
            Vector3 dualAnchor = Vector3.Lerp(playerPos, gm.ActiveBoss.transform.position, 0.18f);
            targetCamPos = dualAnchor + desiredCamOffset;
        }

        if (!m_HasLookInit)
        {
            m_SmoothLookTarget = lookTarget;
            m_HasLookInit = true;
        }
        else
        {
            m_SmoothLookTarget = Vector3.Lerp(m_SmoothLookTarget, lookTarget, Mathf.Clamp01(Time.deltaTime * 11f));
        }

        Vector3 smoothedPos = Vector3.Lerp(transform.position, targetCamPos, Mathf.Clamp01(Time.deltaTime * SmoothSpeed));

        if (m_ShakeTimer > 0f)
        {
            m_ShakeTimer -= dt;
            float fade = Mathf.Clamp01(m_ShakeTimer * 4f + 0.25f);
            smoothedPos += UnityEngine.Random.insideUnitSphere * (m_ShakeIntensity * fade);
            if (m_ShakeTimer <= 0f) m_ShakeIntensity = 0f;
        }

        transform.position = smoothedPos;
        Vector3 lookDir = m_SmoothLookTarget - transform.position;
        if (lookDir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);

        if (m_Cam != null)
            m_Cam.fieldOfView = Mathf.Lerp(m_Cam.fieldOfView, desiredFov, Mathf.Clamp01(Time.deltaTime * 6f));
    }
}

public class BerryDadController : MonoBehaviour
{
    public int EquippedSkin = 0;
    public bool IsWorld5LowGravity = false;

    public float MoveSpeed = 10.5f;
    public float JumpForce = 16.0f;
    public float DoubleJumpForce = 15.2f; // 0.95f * JumpForce
    public float StandardGravity = -32.0f;
    public float LowGravity = -14.4f; // 0.45x gravity in World 5 Space!

    private const int MAX_JUMPS = 2;
    private const float COYOTE_TIME = 0.12f;
    private const float JUMP_BUFFER_TIME = 0.12f;
    private const float FLIP_DURATION = 0.35f;
    private const float STRETCH_DURATION = 0.14f;

    private Rigidbody m_Rb;
    private GameObject m_ModelHolder;
    private Renderer[] m_CachedRenderers = new Renderer[0];
    private Transform m_ContactShadow;
    private float m_InvulnTimer = 0f;
    private bool m_BlinkVisible = true;

    private bool m_IsGrounded = false;
    private float m_CoyoteTimer = 0f;
    private float m_JumpBufferTimer = 0f;
    private float m_GroundSuppressTimer = 0f;
    private bool m_FromDonutSpring = false;
    private int m_JumpsUsed = 0;
    private float m_FlipTimer = 0f;
    private float m_StretchTimer = 0f;
    private float m_ShootCooldown = 0f;
    private float m_DonutLockTimer = 0f;
    private float m_SparkleTimer = 0f;
    private float m_WingFlapTimer = 0f;

    private Vector3 m_FacingDir = Vector3.forward;
    private Vector2 m_MoveInput = Vector2.zero;

    void Awake()
    {
        m_Rb = GetComponent<Rigidbody>();
        if (m_Rb == null) m_Rb = gameObject.AddComponent<Rigidbody>();
        m_Rb.useGravity = false; // Custom tuned platformer gravity in FixedUpdate
        m_Rb.mass = 1f;
        m_Rb.constraints = RigidbodyConstraints.FreezeRotation;
        m_Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        m_Rb.interpolation = RigidbodyInterpolation.Interpolate;

        CapsuleCollider col = GetComponent<CapsuleCollider>();
        if (col == null) col = gameObject.AddComponent<CapsuleCollider>();
        col.radius = 0.45f;
        col.height = 1.6f;
        col.center = new Vector3(0f, 0.8f, 0f);

        RebuildCharacterSkin();
    }

    public void RebuildCharacterSkin()
    {
        if (m_ModelHolder != null) Destroy(m_ModelHolder);
        m_ModelHolder = new GameObject("BerryDadModelRoot");
        m_ModelHolder.transform.SetParent(transform, false);
        Aadish3DGame.BuildBerryCharacterModel(m_ModelHolder, EquippedSkin);
        m_CachedRenderers = m_ModelHolder.GetComponentsInChildren<Renderer>(true);

        if (m_ContactShadow == null)
        {
            GameObject shad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shad.name = "PlayerContactShadow";
            shad.transform.SetParent(transform, false);
            shad.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            shad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shad.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
            DestroyImmediate(shad.GetComponent<Collider>());
            shad.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedVertexAlphaMaterial(
                "PlayerShadowMat", new Color(0.02f, 0.03f, 0.08f, 0.50f), ProceduralTextureFactory.GetSoftRadialGlowTexture());
            m_ContactShadow = shad.transform;
        }
    }

    public bool IsInvulnerable()
    {
        return m_InvulnTimer > 0f;
    }

    public void ApplyKnockbackAndInvuln(Vector3 impulse, float duration)
    {
        m_InvulnTimer = Mathf.Max(m_InvulnTimer, duration);
        if (m_Rb != null && impulse.sqrMagnitude > 0.01f)
        {
            Vector3 v = new Vector3(impulse.x, Mathf.Max(impulse.y, 6.5f), impulse.z);
            SetVelocity(v);
            m_IsGrounded = false;
        }
    }

    public void ApplyDonutBounce(float force)
    {
        if (m_Rb == null) return;
        Vector3 v = GetVelocity();
        v.y = 0f;
        v.y = force;
        SetVelocity(v);
        m_IsGrounded = false;
        m_JumpsUsed = 0; // Reset jumpsUsed immediately on Bouncy Donut Spring bounce
        m_FromDonutSpring = true;
        m_CoyoteTimer = 0f;
        m_FlipTimer = 0f;
        m_StretchTimer = STRETCH_DURATION;
        m_GroundSuppressTimer = 0.14f;
        m_DonutLockTimer = 0.14f;
    }

    public void ResetVelocity()
    {
        SetVelocity(Vector3.zero);
        m_JumpsUsed = 0;
        m_FromDonutSpring = false;
        m_CoyoteTimer = 0f;
        m_JumpBufferTimer = 0f;
        m_GroundSuppressTimer = 0f;
        m_FlipTimer = 0f;
        m_StretchTimer = 0f;
    }

    Vector3 GetVelocity()
    {
        if (m_Rb == null) return Vector3.zero;
#if UNITY_6000_0_OR_NEWER
        return m_Rb.linearVelocity;
#else
        return m_Rb.velocity;
#endif
    }

    void SetVelocity(Vector3 v)
    {
        if (m_Rb == null) return;
#if UNITY_6000_0_OR_NEWER
        m_Rb.linearVelocity = v;
#else
        m_Rb.velocity = v;
#endif
    }

    public void TriggerJump()
    {
        m_JumpBufferTimer = JUMP_BUFFER_TIME;
        ExecuteJump();
    }

    public void ShootJuiceBullet()
    {
        ExecuteShoot();
    }

    void Update()
    {
        Aadish3DGame gm = Aadish3DGame.Instance;
        if (gm == null || gm.CurrentState != Aadish3DGame.GameState.Playing)
        {
            m_MoveInput = Vector2.zero;
            return;
        }

        if (m_ShootCooldown > 0f) m_ShootCooldown -= Time.deltaTime;
        if (m_DonutLockTimer > 0f) m_DonutLockTimer -= Time.deltaTime;
        if (m_GroundSuppressTimer > 0f) m_GroundSuppressTimer -= Time.deltaTime;
        if (m_WingFlapTimer > 0f) m_WingFlapTimer -= Time.deltaTime;

        // Invulnerability blink visual feedback
        if (m_InvulnTimer > 0f)
        {
            m_InvulnTimer -= Time.deltaTime;
            bool vis = (Mathf.FloorToInt(m_InvulnTimer * 16f) % 2 == 0);
            if (vis != m_BlinkVisible)
            {
                m_BlinkVisible = vis;
                SetRenderersVisible(m_BlinkVisible);
            }
            if (m_InvulnTimer <= 0f && !m_BlinkVisible)
            {
                m_BlinkVisible = true;
                SetRenderersVisible(true);
            }
        }

        // Gather Keyboard + Virtual Touch Joystick input safely
        float h = 0f;
        float v = 0f;
        try
        {
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) h -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h += 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v -= 1f;

            Vector2 touchJoy = gm.TouchMoveInput;
            if (touchJoy.sqrMagnitude > 0.01f)
            {
                h = Mathf.Clamp(h + touchJoy.x, -1f, 1f);
                v = Mathf.Clamp(v + touchJoy.y, -1f, 1f);
            }

            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.K))
                m_JumpBufferTimer = JUMP_BUFFER_TIME;

            bool shootKey = Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return);
            if (shootKey) ExecuteShoot();
        }
        catch { }

        Vector2 raw = new Vector2(h, v);
        if (raw.sqrMagnitude > 1f) raw.Normalize();
        m_MoveInput = raw;

        if (m_MoveInput.sqrMagnitude > 0.04f)
        {
            Vector3 desiredFace = new Vector3(m_MoveInput.x, 0f, m_MoveInput.y).normalized;
            m_FacingDir = Vector3.Slerp(m_FacingDir, desiredFace, Time.deltaTime * 14f);
        }

        // Ground check using reliable downward ray/sphere probe
        CheckGrounded();
        if (m_IsGrounded)
        {
            m_CoyoteTimer = COYOTE_TIME;
            m_JumpsUsed = 0;
            m_FromDonutSpring = false;
        }
        else
        {
            m_CoyoteTimer = Mathf.Max(0f, m_CoyoteTimer - Time.deltaTime);
        }

        if (m_JumpBufferTimer > 0f)
        {
            ExecuteJump();
            m_JumpBufferTimer = Mathf.Max(0f, m_JumpBufferTimer - Time.deltaTime);
        }

        // Animate Berry Dad 360-degree forward flip, vertical stretch, squash/stretch & facing rotation
        if (m_ModelHolder != null)
        {
            Vector3 vel = GetVelocity();
            float speedRatio = Mathf.Clamp01(new Vector2(vel.x, vel.z).magnitude / MoveSpeed);

            float pitchX;
            if (m_FlipTimer > 0f)
            {
                m_FlipTimer = Mathf.Max(0f, m_FlipTimer - Time.deltaTime);
                float progress = 1f - (m_FlipTimer / FLIP_DURATION);
                pitchX = Mathf.SmoothStep(0f, 1f, progress) * 360f;
            }
            else if (m_IsGrounded)
            {
                pitchX = Mathf.Sin(Time.time * 13f) * 4f * speedRatio;
            }
            else
            {
                pitchX = Mathf.Clamp(-vel.y * 1.4f, -14f, 14f);
            }

            if (m_FacingDir.sqrMagnitude > 0.01f)
            {
                Quaternion baseFaceRot = Quaternion.LookRotation(m_FacingDir, Vector3.up);
                m_ModelHolder.transform.localRotation = baseFaceRot * Quaternion.Euler(pitchX, 0f, 0f);
            }

            if (m_StretchTimer > 0f)
            {
                m_StretchTimer = Mathf.Max(0f, m_StretchTimer - Time.deltaTime);
                float st = m_StretchTimer / STRETCH_DURATION;
                Vector3 targetBurstScale = Vector3.Lerp(Vector3.one, new Vector3(0.85f, 1.22f, 0.85f), st);
                m_ModelHolder.transform.localScale = Vector3.Lerp(m_ModelHolder.transform.localScale, targetBurstScale, Time.deltaTime * 28f);
            }
            else
            {
                float squashY = 1f;
                if (!m_IsGrounded) squashY = Mathf.Clamp(1f + vel.y * 0.012f, 0.85f, 1.22f);
                else squashY = 1f + Mathf.Abs(Mathf.Sin(Time.time * 12f)) * 0.06f * speedRatio;
                float invSq = 1f / Mathf.Sqrt(Mathf.Max(0.5f, squashY));
                m_ModelHolder.transform.localScale = Vector3.Lerp(m_ModelHolder.transform.localScale, new Vector3(invSq, squashY, invSq), Time.deltaTime * 14f);
            }

            // Diamond Bird Wing Flap animation when EquippedSkin == 2
            if (EquippedSkin == 2)
            {
                Transform lWing = m_ModelHolder.transform.Find("Wing_L");
                Transform rWing = m_ModelHolder.transform.Find("Wing_R");
                if (lWing != null && rWing != null)
                {
                    float flapFreq = (m_WingFlapTimer > 0f) ? 24f : (!m_IsGrounded ? 15f : 5f);
                    float flapAmp = (!m_IsGrounded || m_WingFlapTimer > 0f) ? 38f : 12f;
                    float wave = Mathf.Sin(Time.time * flapFreq) * flapAmp;
                    lWing.localRotation = Quaternion.Euler(0f, 0f, wave);
                    rWing.localRotation = Quaternion.Euler(0f, 0f, -wave);
                }
            }
        }

        // Skin Sparkle Trail for unlockable Gold / Diamond skins
        if (EquippedSkin > 0)
        {
            m_SparkleTimer -= Time.deltaTime;
            if (m_SparkleTimer <= 0f)
            {
                m_SparkleTimer = 0.14f;
                if (gm.ParticlePool != null)
                {
                    gm.ParticlePool.SpawnSkinSparkle(transform.position + Vector3.up * 0.8f, EquippedSkin);
                }
            }
        }

        // Dynamic contact shadow scaling by height above nearest ground
        UpdateContactShadow();

        // Out-of-bounds respawn guard
        if (transform.position.y < -15f)
            gm.RespawnPlayerAtCheckpoint();
    }

    void FixedUpdate()
    {
        if (m_Rb == null) return;
        Aadish3DGame gm = Aadish3DGame.Instance;
        if (gm == null || gm.CurrentState != Aadish3DGame.GameState.Playing)
        {
            SetVelocity(Vector3.zero);
            return;
        }

        Vector3 vel = GetVelocity();
        float curGravity = IsWorld5LowGravity ? LowGravity : StandardGravity;

        float targetVx = m_MoveInput.x * MoveSpeed;
        float targetVz = m_MoveInput.y * MoveSpeed;

        // Smooth acceleration without stripping knockback immediately if air-borne
        float accel = m_IsGrounded ? 18f : 11f;
        vel.x = Mathf.Lerp(vel.x, targetVx, Mathf.Clamp01(Time.fixedDeltaTime * accel));
        vel.z = Mathf.Lerp(vel.z, targetVz, Mathf.Clamp01(Time.fixedDeltaTime * accel));

        vel.y += curGravity * Time.fixedDeltaTime;
        if (vel.y < -28f) vel.y = -28f;

        SetVelocity(vel);
    }

    void CheckGrounded()
    {
        if (m_DonutLockTimer > 0f || m_GroundSuppressTimer > 0f)
        {
            m_IsGrounded = false;
            return;
        }
        if (GetVelocity().y > 1.25f)
        {
            m_IsGrounded = false;
            return;
        }
        Vector3 origin = transform.position + Vector3.up * 0.45f;
        RaycastHit hit;
        if (Physics.SphereCast(origin, 0.34f, Vector3.down, out hit, 0.38f))
        {
            if (hit.collider != null && !hit.collider.isTrigger && hit.collider.gameObject != gameObject)
            {
                m_IsGrounded = true;
                return;
            }
        }
        m_IsGrounded = false;
    }

    void UpdateContactShadow()
    {
        if (m_ContactShadow == null) return;
        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.up * 0.4f, Vector3.down, out hit, 18f))
        {
            if (hit.collider != null && !hit.collider.isTrigger)
            {
                m_ContactShadow.gameObject.SetActive(true);
                m_ContactShadow.position = new Vector3(transform.position.x, hit.point.y + 0.03f, transform.position.z);
                float h = Mathf.Clamp01(1f - (transform.position.y - hit.point.y) / 14f);
                float s = Mathf.Lerp(0.45f, 1.55f, h);
                m_ContactShadow.localScale = new Vector3(s, s, 1f);
                return;
            }
        }
        m_ContactShadow.gameObject.SetActive(false);
    }

    void ExecuteJump()
    {
        if (m_Rb == null) return;
        Aadish3DGame gm = Aadish3DGame.Instance;
        if (gm == null || gm.CurrentState != Aadish3DGame.GameState.Playing) return;

        bool isDiamondBird = (EquippedSkin == 2);
        bool canFirstJump = (m_IsGrounded || m_CoyoteTimer > 0f || m_FromDonutSpring) && (m_JumpsUsed == 0);
        bool canDoubleJump = (!canFirstJump) && (m_JumpsUsed < MAX_JUMPS || isDiamondBird);

        if (!canFirstJump && !canDoubleJump) return;

        m_JumpBufferTimer = 0f;
        m_CoyoteTimer = 0f;
        m_FromDonutSpring = false;
        m_IsGrounded = false;
        m_GroundSuppressTimer = 0.09f;

        Vector3 vel = GetVelocity();
        if (canFirstJump)
        {
            vel.y = JumpForce;
            SetVelocity(vel);
            m_JumpsUsed = 1;
            m_StretchTimer = 0.10f;
            if (isDiamondBird) m_WingFlapTimer = 0.35f;
            if (gm.ParticlePool != null) gm.ParticlePool.SpawnPuffCloud(transform.position, 6);
            gm.PlaySFX(Aadish3DGame.SFXType.Jump);
        }
        else
        {
            // Zero out downward fall speed first so Double Jump always launches cleanly upward!
            vel.y = 0f;
            vel.y = isDiamondBird ? (JumpForce * 0.92f) : DoubleJumpForce;
            SetVelocity(vel);

            if (isDiamondBird)
            {
                // Flying Diamond Bird skin bypasses the 2-jump limit for infinite mid-air flapping flight
                m_JumpsUsed++;
                m_WingFlapTimer = 0.45f;
                m_StretchTimer = 0.12f;
                if (gm.ParticlePool != null) gm.ParticlePool.SpawnDoubleJumpRing(transform.position, EquippedSkin);
                gm.PlaySFX(Aadish3DGame.SFXType.FlapWhoosh);
            }
            else
            {
                // Crisp 2nd jump: 360-degree forward flip + vertical stretch + cloud/juice ring + Boing-Whoosh SFX
                m_JumpsUsed = MAX_JUMPS;
                m_FlipTimer = FLIP_DURATION;
                m_StretchTimer = STRETCH_DURATION;
                if (gm.ParticlePool != null) gm.ParticlePool.SpawnDoubleJumpRing(transform.position, EquippedSkin);
                gm.PlaySFX(Aadish3DGame.SFXType.DoubleJump);
            }
        }
    }

    void ExecuteShoot()
    {
        Aadish3DGame gm = Aadish3DGame.Instance;
        if (gm == null || gm.CurrentState != Aadish3DGame.GameState.Playing) return;
        if (m_ShootCooldown > 0f) return;

        m_ShootCooldown = 0.22f;
        Vector3 muzzlePos = transform.position + Vector3.up * 0.95f + m_FacingDir.normalized * 0.85f;
        Vector3 shootDir = m_FacingDir.normalized;

        // 3D XYZ Aim-Assist: tracks hovering World 5 Galactic Mega-Pizza UFO and jumping bosses seamlessly!
        if (gm.BossAlive && gm.ActiveBoss != null)
        {
            Vector3 bossAimPt = gm.ActiveBoss.transform.position + Vector3.up * 1.45f;
            Vector3 toBoss = bossAimPt - muzzlePos;
            if (toBoss.magnitude < 38f)
            {
                Vector3 toBossFlat = new Vector3(toBoss.x, 0f, toBoss.z).normalized;
                if (Vector3.Dot(shootDir, toBossFlat) > -0.25f || toBoss.magnitude < 14f)
                    shootDir = toBoss.normalized;
            }
        }
        else
        {
            float bestDist = 20f;
            EnemyController bestEn = null;
            for (int i = 0; i < gm.ActiveEnemies.Count; i++)
            {
                EnemyController en = gm.ActiveEnemies[i];
                if (en == null) continue;
                Vector3 toEn = (en.transform.position + Vector3.up * 0.85f) - muzzlePos;
                float d = toEn.magnitude;
                if (d < bestDist && Vector3.Dot(shootDir, toEn.normalized) > 0.35f)
                {
                    bestDist = d;
                    bestEn = en;
                }
            }
            if (bestEn != null)
                shootDir = ((bestEn.transform.position + Vector3.up * 0.85f) - muzzlePos).normalized;
        }

        Color bulletCol = new Color(1f, 0.18f, 0.58f);
        if (EquippedSkin == 1) bulletCol = new Color(1f, 0.84f, 0.12f);
        else if (EquippedSkin == 2) bulletCol = new Color(0.22f, 0.78f, 0.99f);

        GameObject bGo = new GameObject("JuiceBullet");
        bGo.transform.position = muzzlePos;
        JuiceBullet jb = bGo.AddComponent<JuiceBullet>();
        jb.Init(shootDir * 30f, bulletCol, 1);
        gm.ActiveBullets.Add(jb);

        if (gm.LightPool != null)
            gm.LightPool.TriggerFlash(muzzlePos, bulletCol, 4.2f, 0.14f, 10f);
        gm.PlaySFX(Aadish3DGame.SFXType.Shoot);
    }

    void SetRenderersVisible(bool visible)
    {
        if (m_CachedRenderers == null) return;
        for (int i = 0; i < m_CachedRenderers.Length; i++)
        {
            if (m_CachedRenderers[i] != null)
                m_CachedRenderers[i].enabled = visible;
        }
    }
}

public class JuiceBullet : MonoBehaviour
{
    public Vector3 Velocity;
    public Color BulletColor;
    public float LifeTime = 2.6f;
    public int Damage = 1;

    private float m_TrailTimer = 0f;

    public void Init(Vector3 vel, Color col, int damage = 1)
    {
        Velocity = vel;
        BulletColor = col;
        Damage = damage;

        GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.transform.SetParent(transform, false);
        core.transform.localScale = Vector3.one * 0.44f;
        DestroyImmediate(core.GetComponent<Collider>());
        core.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial(
            "BulletCore_" + col.GetHashCode(), col, null, 0.85f, 0f, col * 1.4f);

        GameObject halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        halo.transform.SetParent(transform, false);
        halo.transform.localScale = Vector3.one * 1.45f;
        DestroyImmediate(halo.GetComponent<Collider>());
        halo.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedAdditiveGlowMaterial(
            "BulletHalo_" + col.GetHashCode(), col, ProceduralTextureFactory.GetSoftRadialGlowTexture());
        halo.AddComponent<BillboardToCamera>();
    }

    void Update()
    {
        Aadish3DGame gm = Aadish3DGame.Instance;
        if (gm == null) { Destroy(gameObject); return; }
        if (gm.CurrentState != Aadish3DGame.GameState.Playing) return;

        float dt = Time.deltaTime;
        Vector3 prevPos = transform.position;
        Vector3 nextPos = prevPos + Velocity * dt;

        // Zero-tunneling Swept Segment Hit Detection against Boss
        if (gm.BossAlive && gm.ActiveBoss != null)
        {
            Vector3 bossCenter = gm.ActiveBoss.transform.position + Vector3.up * 1.65f;
            float hitRadius = (gm.ActiveBoss.BossType == 5) ? 3.85f : 3.25f;
            Vector3 closest = ClosestPointOnSegment(prevPos, nextPos, bossCenter);
            if ((closest - bossCenter).sqrMagnitude <= hitRadius * hitRadius)
            {
                transform.position = closest;
                gm.DamageBoss(Damage, closest, Velocity.normalized);
                gm.ActiveBullets.Remove(this);
                Destroy(gameObject);
                return;
            }
        }

        // Zero-tunneling Swept Segment Hit Detection against Active Enemies
        for (int i = gm.ActiveEnemies.Count - 1; i >= 0; i--)
        {
            EnemyController en = gm.ActiveEnemies[i];
            if (en == null) continue;
            Vector3 enCenter = en.transform.position + Vector3.up * 0.95f;
            Vector3 closest = ClosestPointOnSegment(prevPos, nextPos, enCenter);
            if ((closest - enCenter).sqrMagnitude <= 1.50f * 1.50f)
            {
                transform.position = closest;
                gm.OnEnemyHitByBullet(en, this);
                return;
            }
        }

        transform.position = nextPos;

        m_TrailTimer -= dt;
        if (m_TrailTimer <= 0f)
        {
            m_TrailTimer = 0.045f;
            if (gm.ParticlePool != null)
                gm.ParticlePool.SpawnBurst(transform.position, new Color[] { BulletColor }, 1, 2.2f);
        }

        LifeTime -= dt;
        if (LifeTime <= 0f)
        {
            gm.ActiveBullets.Remove(this);
            Destroy(gameObject);
        }
    }

    static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 0.00001f) return a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSq);
        return a + ab * t;
    }
}

public enum BossAIState
{
    ArenaIdleWaiting,
    PreFightDialogue,
    RelentlessChase,
    AttackTelegraph,
    SignaturePursuitAttack,
    RecoveryWindow,
    Phase2EnrageOverdrive,
    CinematicDefeatExplosion
}

public class BossController : MonoBehaviour
{
    public int BossType = 1; // 1=Burger, 2=Fry, 3=Frosty, 4=Hotdog, 5=PizzaUFO
    public bool IsActiveAI = false;
    public BossAIState State = BossAIState.ArenaIdleWaiting;
    public bool IsCharging = false;
    public bool IsPhase2Enraged = false;

    private GameObject m_Rig;
    private Transform m_LeftPupil;
    private Transform m_RightPupil;
    private Transform m_ContactShadow;
    private GameObject m_DizzyStarsRoot;
    private GameObject m_BeamCylinder;

    private readonly List<Renderer> m_Renderers = new List<Renderer>();
    private readonly List<Material[]> m_OrigMats = new List<Material[]>();
    private static Material s_WhiteFlashMat;

    private float m_StateTimer = 0f;
    private float m_ChaseDuration = 2.4f;
    private float m_HitFlashTimer = 0f;
    private float m_RecoilScale = 1f;
    private float m_DefeatWaveTimer = 0f;
    private float m_SkidSparkTimer = 0f;
    private float m_WallStaggerTimer = 0f;
    private float m_AirCheckTimer = 0f;
    private float m_AttackSubTimer = 0f;
    private int m_AttackPattern = 0; // alternates 0 (Charge/Slam) and 1 (Volley/Barrage)
    private bool m_AttackExecuted = false;

    private Vector3 m_CurrentVel = Vector3.zero;
    private Vector3 m_LockedDir = -Vector3.forward;
    private Vector3 m_LockedTargetPos = Vector3.zero;
    private Color m_AuraColor = new Color(1f, 0.55f, 0.12f);

    private const float ArenaClamp = 16.6f;

    // -----------------------------------------------------------------------
    // UPGRADED HIGH-DETAIL PBR BOSS MODELS (SAVES ORIGINAL MATERIALS!)
    // -----------------------------------------------------------------------
    public void BuildKingCheeseburg()
    {
        m_AuraColor = Aadish3DGame.HexColor("#F59E0B");
        InitRig();

        // Bottom Bun
        CreatePart("BotBun", LowPolyMeshHelper.GetTaperedFrustum(2.3f, 2.5f, 0.9f, 14), new Vector3(0f, 0.45f, 0f), Vector3.one,
            "BurgerBun_Boss", Aadish3DGame.HexColor("#D97706"), 0.45f, 0.04f);

        // Juicy Char-Grilled Patty + Glowing Crust
        CreatePart("Patty", LowPolyMeshHelper.GetOctagonPrism(2.6f, 0.72f), new Vector3(0f, 1.25f, 0f), Vector3.one,
            "BossPatty", Aadish3DGame.HexColor("#451A03"), 0.35f, 0.05f, Aadish3DGame.HexColor("#9A3412"));

        // Melted Cheddar Cheese Slice (corners drooping down!)
        CreatePart("CheeseSlice", LowPolyMeshHelper.GetBeveledSlabMesh(4.9f, 0.24f, 4.9f, 0.08f), new Vector3(0f, 1.68f, 0f),
            new Vector3(1f, 1f, 1f), "BossCheese", Aadish3DGame.HexColor("#FACC15"), 0.80f, 0.08f, null, Quaternion.Euler(0f, 45f, 0f));

        // Crisp Ruffled Lettuce Skirt
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            CreatePart("Lettuce_" + i, LowPolyMeshHelper.GetBeveledSlabMesh(1.3f, 0.22f, 1.1f, 0.06f),
                new Vector3(Mathf.Sin(a) * 2.15f, 1.48f, Mathf.Cos(a) * 2.15f), Vector3.one,
                "BossLettuce", Aadish3DGame.HexColor("#22C55E"), 0.6f, 0f, null, Quaternion.Euler(14f * Mathf.Cos(a), i * 45f, 0f));
        }

        // Top Dome Crown Bun + Golden Sesame Seeds
        CreatePart("TopBun", PrimitiveType.Sphere, new Vector3(0f, 2.35f, 0f), new Vector3(4.9f, 2.3f, 4.9f),
            "BurgerTopBun_Boss", Aadish3DGame.HexColor("#EA580C"), 0.48f);
        for (int s = 0; s < 10; s++)
        {
            float sa = s * 36f * Mathf.Deg2Rad;
            CreatePart("Sesame_" + s, PrimitiveType.Sphere, new Vector3(Mathf.Sin(sa) * 1.45f, 3.22f, Mathf.Cos(sa) * 1.45f),
                new Vector3(0.28f, 0.14f, 0.18f), "SesameMat", Aadish3DGame.HexColor("#FEF3C7"), 0.6f);
        }

        // Royal Golden Crown atop the Bun!
        CreatePart("CrownBase", LowPolyMeshHelper.GetOctagonPrism(1.25f, 0.45f), new Vector3(0f, 3.65f, 0f), Vector3.one,
            "BossGoldCrown", Aadish3DGame.HexColor("#FACC15"), 0.90f, 0.85f, Aadish3DGame.HexColor("#B45309"));
        for (int c = 0; c < 6; c++)
        {
            float ca = c * 60f * Mathf.Deg2Rad;
            CreatePart("CrownSpike_" + c, LowPolyMeshHelper.GetSharpCone(6, 0.25f, 0.72f),
                new Vector3(Mathf.Sin(ca) * 0.95f, 4.18f, Mathf.Cos(ca) * 0.95f), Vector3.one,
                "BossGoldCrownSpike", Aadish3DGame.HexColor("#FDE047"), 0.92f, 0.88f, Aadish3DGame.HexColor("#D97706"));
        }

        AttachBossAngryEyes(new Vector3(0f, 2.35f, 2.25f), 0.78f, 0.42f);
        FinalizeBossModel();
    }

    public void BuildCaptainCrispyFry()
    {
        m_AuraColor = Aadish3DGame.HexColor("#EF4444");
        InitRig();

        // Pirate Fry Carton Body with Golden Emblem
        CreatePart("CartonBox", LowPolyMeshHelper.GetTaperedFrustum(1.95f, 2.6f, 2.7f, 4), new Vector3(0f, 1.35f, 0f), Vector3.one,
            "BossFryCarton", Aadish3DGame.HexColor("#DC2626"), 0.55f, 0.08f, null, Quaternion.Euler(0f, 45f, 0f));
        CreatePart("GoldStripe", LowPolyMeshHelper.GetBeveledSlabMesh(3.4f, 0.35f, 3.4f, 0.06f), new Vector3(0f, 1.35f, 0f), Vector3.one,
            "BossGoldTrim", Aadish3DGame.HexColor("#FACC15"), 0.88f, 0.80f);

        // 11 Towering Crisp Golden French Fries fanned out like a Pirate Crest
        for (int f = 0; f < 11; f++)
        {
            float t = (f / 10f) - 0.5f;
            float rx = Mathf.Sin(f * 1.4f) * 10f;
            float rz = -t * 34f;
            float h = 2.1f + (1f - Mathf.Abs(t)) * 1.2f;
            CreatePart("Fry_" + f, LowPolyMeshHelper.GetBeveledSlabMesh(0.45f, h, 0.45f, 0.05f),
                new Vector3(t * 2.5f, 2.85f + (1f - Mathf.Abs(t)) * 0.3f, Mathf.Cos(f) * 0.45f), Vector3.one,
                "BossGoldenFry", Aadish3DGame.HexColor("#FBBF24"), 0.52f, 0.05f, null, Quaternion.Euler(rx, 0f, rz));
        }

        // Giant Captain Pirate Tricorn Hat & Glowing Fry-Cutlass Sword
        CreatePart("PirateHat", LowPolyMeshHelper.GetTaperedFrustum(1.2f, 2.6f, 0.9f, 3), new Vector3(0f, 4.4f, 0f), Vector3.one,
            "BossPirateHat", Aadish3DGame.HexColor("#1E293B"), 0.5f, 0.1f);
        CreatePart("SwordBlade", LowPolyMeshHelper.GetBeveledSlabMesh(0.28f, 0.16f, 3.2f, 0.04f), new Vector3(2.4f, 1.6f, 1.4f), Vector3.one,
            "BossSwordGlow", Aadish3DGame.HexColor("#38BDF8"), 0.92f, 0.85f, Aadish3DGame.HexColor("#0284C7"));

        AttachBossAngryEyes(new Vector3(0f, 1.85f, 1.85f), 0.72f, 0.40f);
        FinalizeBossModel();
    }

    public void BuildFrostyConeMonster()
    {
        m_AuraColor = Aadish3DGame.HexColor("#38BDF8");
        InitRig();

        // Waffle Cone Armor Base (inverted cone!)
        CreatePart("ConeBody", LowPolyMeshHelper.GetSharpCone(12, 2.1f, 3.0f), new Vector3(0f, 1.5f, 0f), Vector3.one,
            "BossWaffleCone", Aadish3DGame.HexColor("#D97706"), 0.45f, 0f, null, Quaternion.Euler(180f, 0f, 0f));

        // Massive Swirled Frost Scoops (Lower + Middle + Crystal Horn)
        CreatePart("Scoop1", PrimitiveType.Sphere, new Vector3(0f, 2.6f, 0f), new Vector3(4.2f, 3.1f, 4.2f),
            "BossFrost1", Aadish3DGame.HexColor("#E0F2FE"), 0.85f, 0.05f, Aadish3DGame.HexColor("#0284C7"));
        CreatePart("Scoop2", PrimitiveType.Sphere, new Vector3(0f, 4.2f, 0f), new Vector3(3.1f, 2.5f, 3.1f),
            "BossFrost2", Aadish3DGame.HexColor("#BAE6FD"), 0.88f, 0.08f, Aadish3DGame.HexColor("#0369A1"));

        // Jagged Ice Crystal Crown Spikes
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f * Mathf.Deg2Rad;
            CreatePart("IceSpike_" + i, LowPolyMeshHelper.GetBeveledPyramid(0.75f, 1.5f),
                new Vector3(Mathf.Sin(a) * 1.9f, 3.3f, Mathf.Cos(a) * 1.9f), Vector3.one,
                "BossIceCrystal", Aadish3DGame.HexColor("#7DD3FC"), 0.96f, 0.25f, Aadish3DGame.HexColor("#0EA5E9"));
        }

        // Frosted Berry Horn Top
        CreatePart("IceHorn", LowPolyMeshHelper.GetSharpCone(8, 0.65f, 1.45f), new Vector3(0f, 5.45f, 0f), Vector3.one,
            "BossIceHorn", Aadish3DGame.HexColor("#38BDF8"), 0.94f, 0.2f, Aadish3DGame.HexColor("#0EA5E9"));

        AttachBossAngryEyes(new Vector3(0f, 3.05f, 1.85f), 0.72f, 0.40f);
        FinalizeBossModel();
    }

    public void BuildSirSizzleHotdog()
    {
        m_AuraColor = Aadish3DGame.HexColor("#F97316");
        InitRig();

        // Heavy Armored Grill Bun + Molten Grill Marks
        CreatePart("SizzleBunL", LowPolyMeshHelper.GetBeveledSlabMesh(1.4f, 1.8f, 5.2f, 0.25f), new Vector3(-1.35f, 1.25f, 0f), Vector3.one,
            "BossGrillBun", Aadish3DGame.HexColor("#C2410C"), 0.45f);
        CreatePart("SizzleBunR", LowPolyMeshHelper.GetBeveledSlabMesh(1.4f, 1.8f, 5.2f, 0.25f), new Vector3(1.35f, 1.25f, 0f), Vector3.one,
            "BossGrillBunR", Aadish3DGame.HexColor("#C2410C"), 0.45f);

        // Glowing Magma Sausage Body + Twin Flame Exhaust Stacks
        CreatePart("MagmaSausage", PrimitiveType.Capsule, new Vector3(0f, 1.75f, 0f), new Vector3(2.3f, 2.95f, 2.3f),
            "BossSausageMagma", Aadish3DGame.HexColor("#B91C1C"), 0.72f, 0.12f, Aadish3DGame.HexColor("#F97316"), Quaternion.Euler(90f, 0f, 0f));

        // Mustard Armor Ribs along the spine
        for (int m = -2; m <= 2; m++)
        {
            CreatePart("MustardRib_" + m, LowPolyMeshHelper.GetTorusDonut(14, 8, 1.22f, 0.22f), new Vector3(0f, 1.75f, m * 0.95f), Vector3.one,
                "BossMustardGlow", Aadish3DGame.HexColor("#FACC15"), 0.85f, 0.1f, Aadish3DGame.HexColor("#EA580C"));
        }

        // Knight Flame-Plume Helm
        CreatePart("KnightCrest", LowPolyMeshHelper.GetBeveledPyramid(1.2f, 1.6f), new Vector3(0f, 3.2f, 1.5f), Vector3.one,
            "BossFlamePlume", Aadish3DGame.HexColor("#FF4500"), 0.9f, 0.2f, Aadish3DGame.HexColor("#FF6B00"));

        AttachBossAngryEyes(new Vector3(0f, 2.25f, 2.65f), 0.76f, 0.42f);
        FinalizeBossModel();
    }

    public void BuildGalacticMegaPizza()
    {
        m_AuraColor = Aadish3DGame.HexColor("#D946EF");
        InitRig();

        // Massive Golden Crust UFO Rim + Obsidian Molten Cheese Saucer
        CreatePart("PizzaSaucer", LowPolyMeshHelper.GetOctagonPrism(3.4f, 0.65f), new Vector3(0f, 1.8f, 0f), Vector3.one,
            "BossPizzaBase", Aadish3DGame.HexColor("#FACC15"), 0.75f, 0.1f, Aadish3DGame.HexColor("#D97706"));
        CreatePart("PizzaCrustRing", LowPolyMeshHelper.GetTorusDonut(22, 10, 3.35f, 0.48f), new Vector3(0f, 1.8f, 0f), Vector3.one,
            "BossPizzaCrust", Aadish3DGame.HexColor("#B45309"), 0.45f);

        // Glowing Neon Pepperoni Shields orbiting top deck
        for (int p = 0; p < 6; p++)
        {
            float pa = p * 60f * Mathf.Deg2Rad;
            CreatePart("Pepp_" + p, PrimitiveType.Cylinder, new Vector3(Mathf.Sin(pa) * 2.15f, 2.2f, Mathf.Cos(pa) * 2.15f),
                new Vector3(1.1f, 0.12f, 1.1f), "BossPepperoni", Aadish3DGame.HexColor("#DC2626"), 0.7f, 0.05f, Aadish3DGame.HexColor("#991B1B"));
        }

        // Cosmic Glass Alien Dome & Glowing Magenta Thruster Core
        CreatePart("CosmicDome", PrimitiveType.Sphere, new Vector3(0f, 2.55f, 0f), new Vector3(2.8f, 2.1f, 2.8f),
            "BossUFODome", Aadish3DGame.HexColor("#00F5D4"), 0.96f, 0.2f, Aadish3DGame.HexColor("#06B6D4"));
        CreatePart("PlasmaRing", LowPolyMeshHelper.GetTorusDonut(20, 8, 2.4f, 0.24f), new Vector3(0f, 1.25f, 0f), Vector3.one,
            "BossUFOThruster", Aadish3DGame.HexColor("#E879F9"), 0.95f, 0.2f, Aadish3DGame.HexColor("#D946EF"));

        AttachBossAngryEyes(new Vector3(0f, 2.75f, 1.38f), 0.62f, 0.36f);
        FinalizeBossModel();
    }

    void InitRig()
    {
        if (m_Rig != null) Destroy(m_Rig);
        m_Renderers.Clear();
        m_OrigMats.Clear();

        m_Rig = new GameObject("BossRig");
        m_Rig.transform.SetParent(transform, false);

        SphereCollider sc = GetComponent<SphereCollider>();
        if (sc == null) sc = gameObject.AddComponent<SphereCollider>();
        sc.radius = 2.4f;
        sc.center = new Vector3(0f, 2.0f, 0f);
        sc.isTrigger = true;
    }

    GameObject CreatePart(string name, Mesh mesh, Vector3 pos, Vector3 scale, string matKey, Color col, float smooth, float metal = 0.05f, Color? emit = null, Quaternion? rot = null)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(m_Rig.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (rot.HasValue) go.transform.localRotation = rot.Value;
        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = SafeMaterialFactory.GetCachedMaterial(matKey, col, null, smooth, metal, emit);
        return go;
    }

    GameObject CreatePart(string name, PrimitiveType prim, Vector3 pos, Vector3 scale, string matKey, Color col, float smooth, float metal = 0.05f, Color? emit = null, Quaternion? rot = null)
    {
        GameObject go = GameObject.CreatePrimitive(prim);
        go.name = name;
        go.transform.SetParent(m_Rig.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (rot.HasValue) go.transform.localRotation = rot.Value;
        DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial(matKey, col, null, smooth, metal, emit);
        return go;
    }

    void AttachBossAngryEyes(Vector3 center, float separation, float eyeScale)
    {
        for (int side = 0; side < 2; side++)
        {
            float sx = (side == 0) ? -separation : separation;
            GameObject scl = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            scl.name = "BossEye_" + side;
            scl.transform.SetParent(m_Rig.transform, false);
            scl.transform.localPosition = center + new Vector3(sx, 0f, 0f);
            scl.transform.localScale = Vector3.one * eyeScale;
            DestroyImmediate(scl.GetComponent<Collider>());
            scl.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("BossEyeWhite", Color.white, null, 0.9f);

            GameObject pupil = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pupil.transform.SetParent(scl.transform, false);
            pupil.transform.localPosition = new Vector3(0f, 0f, 0.42f);
            pupil.transform.localScale = Vector3.one * 0.52f;
            DestroyImmediate(pupil.GetComponent<Collider>());
            pupil.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("BossPupilRed",
                Aadish3DGame.HexColor("#DC2626"), null, 0.9f, 0f, Aadish3DGame.HexColor("#EF4444"));
            if (side == 0) m_LeftPupil = pupil.transform;
            else m_RightPupil = pupil.transform;

            GameObject brow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            brow.transform.SetParent(scl.transform, false);
            brow.transform.localPosition = new Vector3(0f, 0.48f, 0.15f);
            brow.transform.localScale = new Vector3(1.1f, 0.25f, 0.5f);
            brow.transform.localRotation = Quaternion.Euler(0f, 0f, (side == 0) ? -22f : 22f);
            DestroyImmediate(brow.GetComponent<Collider>());
            brow.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedMaterial("BossBrowDark", Aadish3DGame.HexColor("#0F172A"));
        }
    }

    void FinalizeBossModel()
    {
        m_Renderers.Clear();
        m_OrigMats.Clear();
        Renderer[] all = m_Rig.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < all.Length; i++)
        {
            m_Renderers.Add(all[i]);
            m_OrigMats.Add(all[i].sharedMaterials);
        }

        if (m_ContactShadow == null)
        {
            GameObject shad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shad.name = "BossContactShadow";
            shad.transform.SetParent(transform, false);
            shad.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            shad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shad.transform.localScale = new Vector3(4.6f, 4.6f, 1f);
            DestroyImmediate(shad.GetComponent<Collider>());
            shad.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedVertexAlphaMaterial(
                "BossShadowMat", new Color(0.02f, 0.02f, 0.06f, 0.56f), ProceduralTextureFactory.GetSoftRadialGlowTexture());
            m_ContactShadow = shad.transform;
        }

        // Orbiting Dizzy Stars ring for Wall-Crash Stagger
        m_DizzyStarsRoot = new GameObject("DizzyStarsRoot");
        m_DizzyStarsRoot.transform.SetParent(transform, false);
        m_DizzyStarsRoot.transform.localPosition = new Vector3(0f, 5.2f, 0f);
        for (int s = 0; s < 3; s++)
        {
            float a = s * 120f * Mathf.Deg2Rad;
            GameObject st = new GameObject("DizzyStar_" + s);
            st.transform.SetParent(m_DizzyStarsRoot.transform, false);
            st.transform.localPosition = new Vector3(Mathf.Sin(a) * 1.9f, 0f, Mathf.Cos(a) * 1.9f);
            st.transform.localScale = Vector3.one * 0.65f;
            MeshFilter mf = st.AddComponent<MeshFilter>();
            mf.sharedMesh = LowPolyMeshHelper.GetFivePointStar(0.55f, 0.25f, 0.16f);
            MeshRenderer mr = st.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SafeMaterialFactory.GetCachedMaterial("DizzyStarMat", Aadish3DGame.HexColor("#FACC15"), null, 0.9f, 0.8f, Aadish3DGame.HexColor("#FDE047"));
        }
        m_DizzyStarsRoot.SetActive(false);

        if (BossType == 5)
        {
            // World 5 UFO Sky-Beam cylinder telegraph
            m_BeamCylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            m_BeamCylinder.name = "UFOTractorBeam";
            m_BeamCylinder.transform.SetParent(transform, false);
            m_BeamCylinder.transform.localPosition = new Vector3(0f, -4.5f, 0f);
            m_BeamCylinder.transform.localScale = new Vector3(3.4f, 4.5f, 3.4f);
            DestroyImmediate(m_BeamCylinder.GetComponent<Collider>());
            m_BeamCylinder.GetComponent<Renderer>().sharedMaterial = SafeMaterialFactory.GetCachedAdditiveGlowMaterial(
                "UFOLaserBeamMat", new Color(0f, 0.96f, 0.83f, 0.65f));
            m_BeamCylinder.SetActive(false);
        }
    }

    // -----------------------------------------------------------------------
    // STATE TRANSITIONS & DAMAGE REACTION
    // -----------------------------------------------------------------------
    public void EnterPreFightDialogueState()
    {
        State = BossAIState.PreFightDialogue;
        IsActiveAI = false;
        IsCharging = false;
        m_CurrentVel = Vector3.zero;
    }

    public void BeginRelentlessChase()
    {
        IsActiveAI = true;
        State = BossAIState.RelentlessChase;
        m_StateTimer = IsPhase2Enraged ? 1.55f : 2.25f;
        m_ChaseDuration = m_StateTimer;
        m_WallStaggerTimer = 0f;
        if (m_DizzyStarsRoot != null) m_DizzyStarsRoot.SetActive(false);
    }

    public void OnTookBulletDamage(int dmg, Vector3 hitDir, int curHP, int maxHP)
    {
        m_HitFlashTimer = 0.08f;
        m_RecoilScale = 0.88f;
        ApplyWhiteFlash(true);

        if (State == BossAIState.RelentlessChase || State == BossAIState.RecoveryWindow)
        {
            Vector3 push = new Vector3(hitDir.x, 0f, hitDir.z).normalized * 2.4f;
            transform.position = ClampArenaPos(transform.position + push * 0.14f);
        }

        if (curHP <= 0)
        {
            EnterDefeatSequence();
            return;
        }

        if (!IsPhase2Enraged && curHP <= maxHP * 0.5f)
        {
            IsPhase2Enraged = true;
            State = BossAIState.Phase2EnrageOverdrive;
            IsCharging = false;
            m_StateTimer = 1.45f;
            m_CurrentVel = Vector3.zero;
            if (m_BeamCylinder != null) m_BeamCylinder.SetActive(false);
            if (Aadish3DGame.Instance != null)
            {
                Aadish3DGame.Instance.TriggerScreenShake(0.42f, 0.55f);
                Aadish3DGame.Instance.PlaySFX(Aadish3DGame.SFXType.UnlockFanfare);
                if (Aadish3DGame.Instance.ParticlePool != null)
                    Aadish3DGame.Instance.ParticlePool.SpawnBurst(transform.position + Vector3.up * 2f, new Color[] { m_AuraColor, Color.white }, 26, 12f);
            }
            BossProjectile.SpawnShockwaveRing(new Vector3(transform.position.x, 0.25f, transform.position.z), m_AuraColor, 17f, 16f);
        }
    }

    void EnterDefeatSequence()
    {
        State = BossAIState.CinematicDefeatExplosion;
        IsActiveAI = false;
        IsCharging = false;
        m_StateTimer = 1.85f;
        m_DefeatWaveTimer = 0f;
        if (m_DizzyStarsRoot != null) m_DizzyStarsRoot.SetActive(false);
        if (m_BeamCylinder != null) m_BeamCylinder.SetActive(false);
        if (Aadish3DGame.Instance != null)
            Aadish3DGame.Instance.TriggerScreenShake(0.6f, 0.55f);
    }

    void ApplyWhiteFlash(bool flash)
    {
        if (s_WhiteFlashMat == null)
            s_WhiteFlashMat = SafeMaterialFactory.GetCachedMaterial("BossHitWhiteFlash", Color.white, null, 0.9f, 0f, Color.white * 1.3f);

        for (int i = 0; i < m_Renderers.Count; i++)
        {
            Renderer r = m_Renderers[i];
            if (r == null) continue;
            if (flash)
            {
                Material[] arr = new Material[m_OrigMats[i].Length];
                for (int m = 0; m < arr.Length; m++) arr[m] = s_WhiteFlashMat;
                r.sharedMaterials = arr;
            }
            else
            {
                r.sharedMaterials = m_OrigMats[i];
            }
        }
    }

    // -----------------------------------------------------------------------
    // 8-STATE BOSS PURSUIT FSM UPDATE LOOP
    // -----------------------------------------------------------------------
    void Update()
    {
        Aadish3DGame gm = Aadish3DGame.Instance;
        if (gm == null) return;

        float dt = Time.deltaTime;

        // Recover PBR colors after brief hit flash!
        if (m_HitFlashTimer > 0f)
        {
            m_HitFlashTimer -= dt;
            if (m_HitFlashTimer <= 0f) ApplyWhiteFlash(false);
        }
        m_RecoilScale = Mathf.Lerp(m_RecoilScale, 1f, dt * 12f);
        if (m_Rig != null)
            m_Rig.transform.localScale = Vector3.one * m_RecoilScale;

        // Ambient Boss Aura Point Light tracking
        if (gm.LightPool != null)
        {
            bool auraOn = (State != BossAIState.CinematicDefeatExplosion);
            float auraInt = IsPhase2Enraged ? 5.6f : (IsCharging ? 5.0f : 3.2f);
            Color auraCol = IsPhase2Enraged ? Color.Lerp(m_AuraColor, Color.magenta, 0.45f) : m_AuraColor;
            gm.LightPool.SetBossAura(auraOn, transform.position + Vector3.up * 2.5f, auraCol, auraInt, 20f);
        }

        // Pause state machine if dialog or pause menu is up (allow PreFight / Defeat animations cleanly)
        if (gm.CurrentState == Aadish3DGame.GameState.Paused || gm.CurrentState == Aadish3DGame.GameState.EndingCelebration)
            return;

        if (State == BossAIState.CinematicDefeatExplosion)
        {
            UpdateCinematicDefeat(gm, dt);
            return;
        }

        if (gm.CurrentState != Aadish3DGame.GameState.Playing && State != BossAIState.PreFightDialogue)
            return;

        // Update Dizzy wall-crash stars spin & player lock-on eyes
        if (m_DizzyStarsRoot != null && m_DizzyStarsRoot.activeSelf)
            m_DizzyStarsRoot.transform.Rotate(Vector3.up, 260f * dt);

        UpdateContactShadow();
        UpdateEyeLockOn(gm.Player);

        switch (State)
        {
            case BossAIState.ArenaIdleWaiting:
                HoverBob(0f, dt);
                FacePlayerSmooth(gm.Player, 4f, dt);
                break;

            case BossAIState.PreFightDialogue:
                HoverBob(0f, dt);
                FacePlayerSmooth(gm.Player, 7f, dt);
                break;

            case BossAIState.Phase2EnrageOverdrive:
                m_StateTimer -= dt;
                if (m_Rig != null) m_Rig.transform.Rotate(Vector3.up, 520f * dt);
                transform.position = Vector3.Lerp(transform.position, new Vector3(transform.position.x, GetHoverTargetY(gm.Player) + 1.5f, transform.position.z), dt * 5f);
                if (m_StateTimer <= 0f)
                {
                    if (m_Rig != null) m_Rig.transform.localRotation = Quaternion.identity;
                    BeginRelentlessChase();
                }
                break;

            case BossAIState.RelentlessChase:
                UpdateRelentlessChase(gm, dt);
                break;

            case BossAIState.AttackTelegraph:
                UpdateAttackTelegraph(gm, dt);
                break;

            case BossAIState.SignaturePursuitAttack:
                UpdateSignatureAttack(gm, dt);
                break;

            case BossAIState.RecoveryWindow:
                UpdateRecoveryWindow(gm, dt);
                break;
        }

        if (State != BossAIState.ArenaIdleWaiting && State != BossAIState.PreFightDialogue && State != BossAIState.CinematicDefeatExplosion)
            CheckPlayerCollision(gm);
    }

    void UpdateRelentlessChase(Aadish3DGame gm, float dt)
    {
        if (gm.Player == null) return;

        // Staggered from hitting an Arena Wall? Spin stars & recover before chasing again!
        if (m_WallStaggerTimer > 0f)
        {
            m_WallStaggerTimer -= dt;
            if (m_DizzyStarsRoot != null) m_DizzyStarsRoot.SetActive(true);
            if (m_Rig != null) m_Rig.transform.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 18f) * 16f, 0f);
            if (m_WallStaggerTimer <= 0f)
            {
                if (m_DizzyStarsRoot != null) m_DizzyStarsRoot.SetActive(false);
                if (m_Rig != null) m_Rig.transform.localRotation = Quaternion.identity;
            }
            return;
        }

        Vector3 playerPos = gm.Player.transform.position;
        Vector3 playerVel = GetPlayerVelocity(gm.Player);
        float dist = Vector3.Distance(new Vector3(playerPos.x, 0f, playerPos.z), new Vector3(transform.position.x, 0f, transform.position.z));

        // Predictive Velocity Intercept: anticipates Berry Dad's dodge path!
        float leadTime = Mathf.Clamp(dist * 0.032f, 0.08f, IsPhase2Enraged ? 0.56f : 0.42f);
        Vector3 predictedXZ = new Vector3(playerPos.x + playerVel.x * leadTime, 0f, playerPos.z + playerVel.z * leadTime);
        Vector3 toPred = predictedXZ - new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 desiredDir = toPred.sqrMagnitude > 0.04f ? toPred.normalized : transform.forward;

        // Elastic Pursuit Speed scaling with World & Phase 2 Enrage
        float baseSpeed = 6.2f + (BossType - 1) * 0.65f;
        float elasticBonus = Mathf.Clamp((dist - 8f) * 0.38f, 0f, 5.5f);
        float phaseMult = IsPhase2Enraged ? 1.38f : 1f;
        float chaseSpeed = (baseSpeed + elasticBonus) * phaseMult;

        Vector3 targetVelXZ = desiredDir * chaseSpeed;
        float sharpTurn = Vector3.Angle(new Vector3(m_CurrentVel.x, 0f, m_CurrentVel.z), desiredDir);
        m_CurrentVel = Vector3.Lerp(m_CurrentVel, targetVelXZ, dt * (IsPhase2Enraged ? 7.2f : 5.4f));

        // Skid-Drift Sparks when carving hard around a fleeing player!
        if (sharpTurn > 42f && m_CurrentVel.magnitude > 6f)
        {
            m_SkidSparkTimer -= dt;
            if (m_SkidSparkTimer <= 0f)
            {
                m_SkidSparkTimer = 0.09f;
                if (gm.ParticlePool != null)
                    gm.ParticlePool.SpawnBurst(new Vector3(transform.position.x, 0.3f, transform.position.z), new Color[] { Aadish3DGame.HexColor("#F97316"), Aadish3DGame.HexColor("#FACC15") }, 3, 5f);
            }
        }

        Vector3 nextPos = transform.position + m_CurrentVel * dt;
        float targetY = GetHoverTargetY(gm.Player);
        nextPos.y = Mathf.Lerp(transform.position.y, targetY, dt * (BossType == 5 ? 6.5f : 8f));
        transform.position = ClampArenaPos(nextPos);

        if (m_CurrentVel.sqrMagnitude > 0.2f)
        {
            Quaternion targetRot = Quaternion.LookRotation(new Vector3(m_CurrentVel.x, 0f, m_CurrentVel.z).normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, dt * 10f);
        }
        HoverBob(m_CurrentVel.magnitude * 0.1f, dt);

        // Anti-Air awareness: fire early leap/pulse if Berry Dad camps high above ground
        m_AirCheckTimer += dt;
        if (playerPos.y > transform.position.y + 3.2f && m_AirCheckTimer > 1.8f && BossType != 5)
        {
            m_AirCheckTimer = 0f;
            BossProjectile.SpawnOrb(transform.position + Vector3.up * 2.8f,
                ((playerPos + Vector3.up * 0.8f) - (transform.position + Vector3.up * 2.8f)).normalized * 17f, m_AuraColor, 0.9f, true);
        }

        m_StateTimer -= dt;
        if (m_StateTimer <= 0f || (dist < 4.5f && m_ChaseDuration - m_StateTimer > 0.9f))
            EnterAttackTelegraph(gm);
    }

    void EnterAttackTelegraph(Aadish3DGame gm)
    {
        State = BossAIState.AttackTelegraph;
        m_AttackPattern = (m_AttackPattern + 1) % 2;
        m_AttackExecuted = false;
        m_AttackSubTimer = 0f;

        Vector3 pPos = (gm.Player != null) ? gm.Player.transform.position : Vector3.zero;
        m_LockedTargetPos = new Vector3(pPos.x, 0.16f, pPos.z);
        Vector3 toP = new Vector3(pPos.x - transform.position.x, 0f, pPos.z - transform.position.z);
        m_LockedDir = (toP.sqrMagnitude > 0.05f) ? toP.normalized : transform.forward;

        float warnTime = IsPhase2Enraged ? 0.56f : 0.74f;
        m_StateTimer = warnTime;

        if (m_AttackPattern == 0)
        {
            if (BossType == 1 || BossType == 2 || BossType == 4)
            {
                BossFloorTelegraph.SpawnChargeLane(new Vector3(transform.position.x, 0.16f, transform.position.z), m_LockedDir, 26f, m_AuraColor, warnTime);
            }
            else if (BossType == 3)
            {
                BossFloorTelegraph.SpawnRing(m_LockedTargetPos, 6.2f, m_AuraColor, warnTime);
            }
            else // Boss 5 UFO Sky-Slam lock
            {
                BossFloorTelegraph.SpawnRing(m_LockedTargetPos, 6.8f, m_AuraColor, warnTime);
                if (m_BeamCylinder != null) m_BeamCylinder.SetActive(true);
            }
        }
        else
        {
            BossFloorTelegraph.SpawnRing(new Vector3(transform.position.x, 0.16f, transform.position.z), 5.4f, m_AuraColor, warnTime);
        }
    }

    void UpdateAttackTelegraph(Aadish3DGame gm, float dt)
    {
        m_StateTimer -= dt;
        FacePlayerSmooth(gm.Player, 12f, dt);

        // Coiled telegraph squat
        if (m_Rig != null)
            m_Rig.transform.localScale = new Vector3(1.12f, 0.84f, 1.12f);

        if (BossType == 5 && m_AttackPattern == 0 && gm.Player != null)
        {
            // Hover directly above locked target XZ during UFO Sky-Slam telegraph
            Vector3 hover = Vector3.Lerp(transform.position, new Vector3(m_LockedTargetPos.x, 9.5f, m_LockedTargetPos.z), dt * 6f);
            transform.position = ClampArenaPos(hover);
        }

        if (m_StateTimer <= 0f)
        {
            if (m_Rig != null) m_Rig.transform.localScale = Vector3.one;
            if (m_BeamCylinder != null) m_BeamCylinder.SetActive(false);
            State = BossAIState.SignaturePursuitAttack;
            IsCharging = true;
            m_AttackExecuted = false;
            m_AttackSubTimer = 0f;
            m_StateTimer = (m_AttackPattern == 0) ? (IsPhase2Enraged ? 1.05f : 1.25f) : (IsPhase2Enraged ? 1.1f : 1.35f);
            gm.PlaySFX(Aadish3DGame.SFXType.Shoot);
        }
    }

    void UpdateSignatureAttack(Aadish3DGame gm, float dt)
    {
        m_StateTimer -= dt;
        m_AttackSubTimer += dt;

        if (m_AttackPattern == 0)
        {
            // Signature Melee Rush / Aerial Slam
            if (BossType == 1 || BossType == 2 || BossType == 4)
            {
                float rushSpeed = (BossType == 4 ? 24.5f : 21.5f) * (IsPhase2Enraged ? 1.22f : 1f);
                Vector3 nextPos = transform.position + m_LockedDir * rushSpeed * dt;
                transform.rotation = Quaternion.LookRotation(m_LockedDir, Vector3.up);

                // World 4 Sir Sizzle leaves blazing flame orbs along charge path!
                if (BossType == 4 && m_AttackSubTimer > 0.20f)
                {
                    m_AttackSubTimer = 0f;
                    BossProjectile.SpawnOrb(transform.position + Vector3.up * 0.9f, -m_LockedDir * 4f, Aadish3DGame.HexColor("#F97316"), 0.85f, false);
                }

                // Wall Crash Detection at ±16.6f Arena Boundary!
                if (Mathf.Abs(nextPos.x) >= ArenaClamp || Mathf.Abs(nextPos.z) >= ArenaClamp)
                {
                    transform.position = ClampArenaPos(nextPos);
                    TriggerWallCrashStagger(gm);
                    return;
                }
                transform.position = nextPos;
            }
            else if (BossType == 3) // Frosty Cone Leap Slam
            {
                float progress = 1f - Mathf.Clamp01(m_StateTimer / 1.15f);
                Vector3 xz = Vector3.Lerp(transform.position, new Vector3(m_LockedTargetPos.x, 1.5f, m_LockedTargetPos.z), dt * 6.5f);
                xz.y = 1.5f + Mathf.Sin(progress * Mathf.PI) * 7.5f;
                transform.position = ClampArenaPos(xz);
                if (!m_AttackExecuted && progress >= 0.82f)
                {
                    m_AttackExecuted = true;
                    BossProjectile.SpawnShockwaveRing(new Vector3(transform.position.x, 0.25f, transform.position.z), m_AuraColor, IsPhase2Enraged ? 16f : 12.5f, 15f);
                    gm.TriggerScreenShake(0.32f, 0.42f);
                    if (gm.ParticlePool != null) gm.ParticlePool.SpawnPuffCloud(transform.position, 14);
                }
            }
            else // Galactic Mega-Pizza UFO Sky Meteor Slam!
            {
                float targetY = (m_StateTimer > 0.35f) ? 2.1f : 4.2f;
                Vector3 slamPos = Vector3.Lerp(transform.position, new Vector3(m_LockedTargetPos.x, targetY, m_LockedTargetPos.z), dt * 11f);
                transform.position = ClampArenaPos(slamPos);
                if (!m_AttackExecuted && transform.position.y <= 2.65f)
                {
                    m_AttackExecuted = true;
                    BossProjectile.SpawnShockwaveRing(new Vector3(transform.position.x, 0.28f, transform.position.z), m_AuraColor, IsPhase2Enraged ? 17.5f : 14f, 16.5f);
                    gm.TriggerScreenShake(0.4f, 0.45f);
                }
            }
        }
        else
        {
            // Signature Radial / Aimed Barrage
            FacePlayerSmooth(gm.Player, 10f, dt);
            if (!m_AttackExecuted)
            {
                m_AttackExecuted = true;
                FireBossBarrage(gm);
            }
            if (IsPhase2Enraged && m_AttackSubTimer > 0.55f)
            {
                m_AttackSubTimer = -99f;
                FireBossBarrage(gm);
            }
        }

        if (m_StateTimer <= 0f)
        {
            IsCharging = false;
            State = BossAIState.RecoveryWindow;
            m_StateTimer = IsPhase2Enraged ? 0.55f : 0.85f;
        }
    }

    void FireBossBarrage(Aadish3DGame gm)
    {
        Vector3 origin = transform.position + Vector3.up * 1.85f;
        Vector3 pPos = (gm.Player != null) ? gm.Player.transform.position + Vector3.up * 0.9f : Vector3.zero;
        Vector3 dir = (pPos - origin).sqrMagnitude > 0.05f ? (pPos - origin).normalized : transform.forward;

        if (BossType == 1)
        {
            BossProjectile.SpawnShockwaveRing(new Vector3(transform.position.x, 0.25f, transform.position.z), m_AuraColor, IsPhase2Enraged ? 15f : 11.5f, 13.5f);
        }
        else if (BossType == 2 || BossType == 4)
        {
            int count = IsPhase2Enraged ? 5 : 3;
            float spread = 16f;
            for (int i = 0; i < count; i++)
            {
                float ang = (i - (count - 1) * 0.5f) * spread;
                Vector3 shotDir = Quaternion.Euler(0f, ang, 0f) * dir;
                BossProjectile.SpawnOrb(origin, shotDir * 16.5f, m_AuraColor, 0.95f, false);
            }
        }
        else if (BossType == 3)
        {
            int ringCount = IsPhase2Enraged ? 8 : 6;
            for (int i = 0; i < ringCount; i++)
            {
                float a = i * (360f / ringCount) * Mathf.Deg2Rad;
                Vector3 shotDir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                BossProjectile.SpawnOrb(origin, shotDir * 14f, Aadish3DGame.HexColor("#7DD3FC"), 0.95f, false);
            }
        }
        else // World 5 UFO Homing Plasma Pepperoni barrage
        {
            int bolts = IsPhase2Enraged ? 4 : 3;
            for (int i = 0; i < bolts; i++)
            {
                float ang = (i - (bolts - 1) * 0.5f) * 24f;
                Vector3 shotDir = Quaternion.Euler(0f, ang, 0f) * dir;
                BossProjectile.SpawnOrb(origin, shotDir * 15f, Aadish3DGame.HexColor("#E879F9"), 1.0f, true);
            }
        }
    }

    void TriggerWallCrashStagger(Aadish3DGame gm)
    {
        IsCharging = false;
        State = BossAIState.RelentlessChase;
        m_WallStaggerTimer = 1.0f;
        m_StateTimer = 1.6f;
        m_CurrentVel = Vector3.zero;
        if (m_DizzyStarsRoot != null) m_DizzyStarsRoot.SetActive(true);
        gm.TriggerScreenShake(0.45f, 0.48f);
        gm.PlaySFX(Aadish3DGame.SFXType.Hit);
        if (gm.ParticlePool != null)
            gm.ParticlePool.SpawnPuffCloud(transform.position + Vector3.up * 1.4f, 14);
    }

    void UpdateRecoveryWindow(Aadish3DGame gm, float dt)
    {
        m_StateTimer -= dt;
        m_CurrentVel = Vector3.Lerp(m_CurrentVel, Vector3.zero, dt * 6f);
        FacePlayerSmooth(gm.Player, 5f, dt);
        HoverBob(0f, dt);
        if (m_StateTimer <= 0f)
        {
            State = BossAIState.RelentlessChase;
            m_StateTimer = IsPhase2Enraged ? 1.45f : 2.15f;
            m_ChaseDuration = m_StateTimer;
        }
    }

    void UpdateCinematicDefeat(Aadish3DGame gm, float dt)
    {
        m_StateTimer -= dt;
        m_DefeatWaveTimer -= dt;

        if (m_Rig != null)
        {
            m_Rig.transform.Rotate(Vector3.up, 380f * dt);
            float shrink = Mathf.Clamp01(m_StateTimer / 1.85f);
            m_Rig.transform.localScale = Vector3.one * (0.25f + shrink * 0.75f);
        }

        if (m_DefeatWaveTimer <= 0f)
        {
            m_DefeatWaveTimer = 0.24f;
            Vector3 burstPos = transform.position + new Vector3(UnityEngine.Random.Range(-1.8f, 1.8f), UnityEngine.Random.Range(0.8f, 3.2f), UnityEngine.Random.Range(-1.8f, 1.8f));
            if (gm.ParticlePool != null)
                gm.ParticlePool.SpawnBurst(burstPos, new Color[] { m_AuraColor, Color.white }, 12, 10f);
            if (gm.LightPool != null)
                gm.LightPool.TriggerFlash(burstPos, m_AuraColor, 6f, 0.2f, 16f);
            gm.PlaySFX(Aadish3DGame.SFXType.Pop);
        }

        if (m_StateTimer <= 0f)
        {
            if (gm.ParticlePool != null)
                gm.ParticlePool.SpawnConfettiBurst(transform.position + Vector3.up * 1.8f, 32);
            gm.PlaySFX(Aadish3DGame.SFXType.UnlockFanfare);
            gm.OnBossDefeated(this);
            Destroy(gameObject);
        }
    }

    void CheckPlayerCollision(Aadish3DGame gm)
    {
        if (gm.Player == null || m_WallStaggerTimer > 0f) return;
        Vector3 p = gm.Player.transform.position;
        float dx = p.x - transform.position.x;
        float dz = p.z - transform.position.z;
        float dy = Mathf.Abs((p.y + 0.8f) - (transform.position.y + 1.6f));
        float hitR = IsCharging ? 3.25f : 2.75f;
        if (dx * dx + dz * dz <= hitR * hitR && dy <= 2.65f)
            gm.DamagePlayer(1, transform.position);
    }

    float GetHoverTargetY(BerryDadController player)
    {
        if (BossType == 5)
        {
            float py = (player != null) ? player.transform.position.y : 0f;
            return Mathf.Clamp(py + 2.2f, 3.2f, 11.2f);
        }
        return 0.55f;
    }

    void HoverBob(float speedMult, float dt)
    {
        if (m_Rig == null) return;
        float yOff = Mathf.Sin(Time.time * (3.2f + speedMult)) * 0.18f;
        m_Rig.transform.localPosition = new Vector3(0f, yOff, 0f);
    }

    void FacePlayerSmooth(BerryDadController player, float rotSpeed, float dt)
    {
        if (player == null) return;
        Vector3 d = player.transform.position - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude > 0.05f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d.normalized, Vector3.up), dt * rotSpeed);
    }

    void UpdateEyeLockOn(BerryDadController player)
    {
        if (player == null || (m_LeftPupil == null && m_RightPupil == null)) return;
        Vector3 localDir = transform.InverseTransformDirection((player.transform.position - transform.position).normalized);
        Vector3 off = new Vector3(Mathf.Clamp(localDir.x, -0.25f, 0.25f), Mathf.Clamp(localDir.y, -0.2f, 0.25f), 0.42f);
        if (m_LeftPupil != null) m_LeftPupil.localPosition = off;
        if (m_RightPupil != null) m_RightPupil.localPosition = off;
    }

    void UpdateContactShadow()
    {
        if (m_ContactShadow == null) return;
        m_ContactShadow.position = new Vector3(transform.position.x, 0.05f, transform.position.z);
        float h = Mathf.Clamp01(1f - transform.position.y / 14f);
        float s = Mathf.Lerp(2.2f, 4.8f, h);
        m_ContactShadow.localScale = new Vector3(s, s, 1f);
    }

    static Vector3 ClampArenaPos(Vector3 pos)
    {
        pos.x = Mathf.Clamp(pos.x, -ArenaClamp, ArenaClamp);
        pos.z = Mathf.Clamp(pos.z, -ArenaClamp, ArenaClamp);
        if (pos.y < 0.25f) pos.y = 0.25f;
        return pos;
    }

    static Vector3 GetPlayerVelocity(BerryDadController p)
    {
        Rigidbody rb = p.GetComponent<Rigidbody>();
        if (rb == null) return Vector3.zero;
#if UNITY_6000_0_OR_NEWER
        return rb.linearVelocity;
#else
        return rb.velocity;
#endif
    }
}

public class EnemyController : MonoBehaviour
{
    public float PatrolRange = 2.6f;
    public float MoveSpeed = 2.4f;

    private Vector3 m_StartPos;
    private float m_Phase = 0f;
    private float m_LastX = 0f;

    void Start()
    {
        m_StartPos = transform.position;
        m_LastX = m_StartPos.x;
        m_Phase = (m_StartPos.z * 0.35f) % 6.28f;
    }

    void Update()
    {
        Aadish3DGame gm = Aadish3DGame.Instance;
        if (gm == null || gm.CurrentState != Aadish3DGame.GameState.Playing) return;

        // Distance culling beyond 78m on Marathon courses for locked 60 FPS!
        if (gm.Player != null && gm.LevelFinishZ > 120f)
        {
            if (Mathf.Abs(transform.position.z - gm.Player.transform.position.z) > 78f)
                return;
        }

        float newX = m_StartPos.x + Mathf.Sin(Time.time * MoveSpeed + m_Phase) * PatrolRange;
        float dx = newX - m_LastX;
        m_LastX = newX;
        transform.position = new Vector3(newX, m_StartPos.y, m_StartPos.z);

        if (Mathf.Abs(dx) > 0.0005f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(new Vector3(dx > 0f ? 1f : -1f, 0f, 0f), Vector3.up), Time.deltaTime * 10f);

        if (gm.Player != null)
        {
            Vector3 p = gm.Player.transform.position;
            float distSq = (p.x - transform.position.x) * (p.x - transform.position.x) +
                           (p.z - transform.position.z) * (p.z - transform.position.z);
            float dy = Mathf.Abs((p.y + 0.8f) - (transform.position.y + 0.9f));
            if (distSq < 1.32f * 1.32f && dy < 1.45f)
                gm.DamagePlayer(1, transform.position);
        }
    }

    public void Defeat()
    {
        Aadish3DGame gm = Aadish3DGame.Instance;
        if (gm != null)
        {
            gm.ActiveEnemies.Remove(this);
            gm.PlaySFX(Aadish3DGame.SFXType.Pop);
            if (gm.ParticlePool != null)
                gm.ParticlePool.SpawnPuffCloud(transform.position + Vector3.up * 0.8f, 10);
        }
        Destroy(gameObject);
    }
}

public class CheckpointComp : MonoBehaviour
{
    public bool Activated = false;
    public Renderer BannerRenderer;

    void Start()
    {
        if (BannerRenderer == null)
        {
            Transform flagMesh = transform.Find("Flag");
            if (flagMesh != null) BannerRenderer = flagMesh.GetComponent<Renderer>();
        }
    }

    void Update()
    {
        if (Activated) return;
        Aadish3DGame gm = Aadish3DGame.Instance;
        if (gm == null || gm.Player == null || gm.CurrentState != Aadish3DGame.GameState.Playing) return;

        Vector3 p = gm.Player.transform.position;
        if ((p - transform.position).sqrMagnitude <= 2.6f * 2.6f)
        {
            Activated = true;
            gm.CheckpointPoint = transform.position;
            gm.CheckpointActivated = true;
            gm.PlaySFX(Aadish3DGame.SFXType.UnlockFanfare);
            if (gm.ParticlePool != null)
                gm.ParticlePool.SpawnConfettiBurst(transform.position + Vector3.up * 2f, 16);
            if (BannerRenderer != null)
                BannerRenderer.sharedMaterial = SafeMaterialFactory.GetCachedMaterial(
                    "CheckpointGreenMat", Aadish3DGame.HexColor("#22C55E"), null, 0.7f, 0f, Aadish3DGame.HexColor("#16A34A"));
        }
    }
}

public class DonutSpringComp : MonoBehaviour
{
    public float BounceForce = 24f;
    public Transform VisualRoot;
    private float m_SquishTimer = 0f;
    private float m_Cooldown = 0f;

    void Update()
    {
        Aadish3DGame gm = Aadish3DGame.Instance;
        if (gm == null) return;

        if (m_Cooldown > 0f) m_Cooldown -= Time.deltaTime;
        Transform animTr = (VisualRoot != null) ? VisualRoot : transform;
        if (m_SquishTimer > 0f)
        {
            m_SquishTimer -= Time.deltaTime;
            float s = 1f + Mathf.Sin(m_SquishTimer * 28f) * 0.25f;
            animTr.localScale = new Vector3(s, 1f / s, s);
            if (m_SquishTimer <= 0f) animTr.localScale = Vector3.one;
        }

        if (gm.Player == null || gm.CurrentState != Aadish3DGame.GameState.Playing || m_Cooldown > 0f) return;

        Vector3 p = gm.Player.transform.position;
        float dx = p.x - transform.position.x;
        float dz = p.z - transform.position.z;
        float dy = p.y - transform.position.y;
        if (dx * dx + dz * dz <= 2.15f * 2.15f && dy >= -1.2f && dy <= 1.65f)
        {
            m_Cooldown = 0.28f;
            m_SquishTimer = 0.28f;
            gm.Player.ApplyDonutBounce(BounceForce);
            gm.PlaySFX(Aadish3DGame.SFXType.Jump);
            if (gm.ParticlePool != null)
                gm.ParticlePool.SpawnConfettiBurst(transform.position + Vector3.up * 0.4f, 12);
        }
    }
}

public class GoldenStar : MonoBehaviour
{
    public int LevelNumber = 1;
    private float m_BaseY;

    void Start()
    {
        m_BaseY = transform.position.y;
    }

    void Update()
    {
        Aadish3DGame gm = Aadish3DGame.Instance;
        if (gm == null) return;

        if (gm.Player != null && gm.LevelFinishZ > 120f)
        {
            if (Mathf.Abs(transform.position.z - gm.Player.transform.position.z) > 78f)
                return;
        }

        transform.Rotate(Vector3.up, 110f * Time.deltaTime);
        transform.position = new Vector3(transform.position.x, m_BaseY + Mathf.Sin(Time.time * 2.8f) * 0.26f, transform.position.z);

        if (gm.Player != null && gm.CurrentState == Aadish3DGame.GameState.Playing)
        {
            if ((gm.Player.transform.position + Vector3.up * 0.8f - transform.position).sqrMagnitude <= 2.1f * 2.1f)
            {
                gm.SetStarCollected(LevelNumber);
                gm.PlaySFX(Aadish3DGame.SFXType.StarDing);
                if (gm.ParticlePool != null)
                    gm.ParticlePool.SpawnConfettiBurst(transform.position, 18);
                Destroy(gameObject);
            }
        }
    }
}

public class MiniBerry : MonoBehaviour
{
    public int LevelNumber = 1;
    public string KidName = "Pip";
    public Color KidColor = Color.magenta;
    private float m_BaseY;
    private bool m_BeaconMode = false;
    private bool m_Rescued = false;

    void Start()
    {
        m_BaseY = transform.position.y;
    }

    public void PlayCelebrationBeacon()
    {
        m_BeaconMode = true;
    }

    void Update()
    {
        if (m_Rescued) return;
        Aadish3DGame gm = Aadish3DGame.Instance;
        if (gm == null) return;

        float freq = m_BeaconMode ? 4.8f : 2.8f;
        float amp = m_BeaconMode ? 0.35f : 0.16f;
        transform.position = new Vector3(transform.position.x, m_BaseY + Mathf.Abs(Mathf.Sin(Time.time * freq)) * amp, transform.position.z);
        transform.Rotate(Vector3.up, (m_BeaconMode ? 90f : 35f) * Time.deltaTime);

        if (gm.Player != null && gm.CurrentState == Aadish3DGame.GameState.Playing)
        {
            if (gm.BossAlive) return;
            if ((gm.Player.transform.position + Vector3.up * 0.7f - transform.position).sqrMagnitude <= 2.4f * 2.4f)
            {
                m_Rescued = true;
                gm.StartMiniBerryRescue(this);
            }
        }
    }
}

public class WaferPlatform : MonoBehaviour
{
    public Vector3 Size = new Vector3(4f, 0.8f, 4f);
    private Vector3 m_OriginPos;
    private bool m_Triggered = false;
    private bool m_Broken = false;
    private float m_Timer = 0f;
    private Collider m_Col;
    private Renderer[] m_Rends;

    void Start()
    {
        m_OriginPos = transform.position;
        m_Col = GetComponent<Collider>();
        m_Rends = GetComponentsInChildren<Renderer>(true);
        BoxCollider bc = m_Col as BoxCollider;
        if (bc != null) Size = bc.size;
    }

    void Update()
    {
        Aadish3DGame gm = Aadish3DGame.Instance;
        if (gm == null || gm.CurrentState != Aadish3DGame.GameState.Playing) return;

        if (!m_Triggered && !m_Broken && gm.Player != null && gm.LevelFinishZ > 120f)
        {
            if (Mathf.Abs(transform.position.z - gm.Player.transform.position.z) > 78f)
                return;
        }

        if (m_Broken)
        {
            m_Timer += Time.deltaTime;
            if (m_Timer >= 2.8f)
            {
                m_Broken = false;
                m_Triggered = false;
                transform.position = m_OriginPos;
                if (m_Col != null) m_Col.enabled = true;
                SetRenderers(true);
            }
            return;
        }

        if (m_Triggered)
        {
            m_Timer += Time.deltaTime;
            transform.position = m_OriginPos + new Vector3(Mathf.Sin(Time.time * 55f) * 0.14f, 0f, Mathf.Cos(Time.time * 48f) * 0.14f);
            if (m_Timer >= 0.65f)
            {
                m_Broken = true;
                m_Timer = 0f;
                if (m_Col != null) m_Col.enabled = false;
                SetRenderers(false);
                if (gm.ParticlePool != null)
                    gm.ParticlePool.SpawnPuffCloud(m_OriginPos, 10);
                gm.PlaySFX(Aadish3DGame.SFXType.Pop);
            }
            return;
        }

        if (gm.Player != null)
        {
            Vector3 p = gm.Player.transform.position;
            float halfX = Size.x * 0.5f + 0.35f;
            float halfZ = Size.z * 0.5f + 0.35f;
            float topY = m_OriginPos.y + Size.y * 0.5f;
            if (Mathf.Abs(p.x - m_OriginPos.x) <= halfX &&
                Mathf.Abs(p.z - m_OriginPos.z) <= halfZ &&
                p.y >= topY - 0.35f && p.y <= topY + 0.95f)
            {
                m_Triggered = true;
                m_Timer = 0f;
            }
        }
    }

    void SetRenderers(bool on)
    {
        if (m_Rends == null) return;
        for (int i = 0; i < m_Rends.Length; i++)
            if (m_Rends[i] != null) m_Rends[i].enabled = on;
    }
}

public class MovingPlatform : MonoBehaviour
{
    public Vector3 MoveAxis = Vector3.right;
    public float Distance = 4f;
    public float Speed = 1.8f;
    public Vector3 Size = new Vector3(4.5f, 1.0f, 4.5f);

    private Vector3 m_OriginPos;
    private Vector3 m_LastPos;
    private float m_Phase = 0f;

    void Start()
    {
        m_OriginPos = transform.position;
        m_LastPos = m_OriginPos;
        m_Phase = (m_OriginPos.z * 0.17f) % 6.28f;
        BoxCollider bc = GetComponent<BoxCollider>();
        if (bc != null) Size = bc.size;
    }

    void FixedUpdate()
    {
        Aadish3DGame gm = Aadish3DGame.Instance;
        if (gm == null || gm.CurrentState != Aadish3DGame.GameState.Playing) return;

        if (gm.Player != null && gm.LevelFinishZ > 120f)
        {
            if (Mathf.Abs(transform.position.z - gm.Player.transform.position.z) > 78f)
                return;
        }

        Vector3 axis = (MoveAxis.sqrMagnitude > 0.001f) ? MoveAxis.normalized : Vector3.right;
        Vector3 newPos = m_OriginPos + axis * (Distance * Mathf.Sin(Time.time * Speed + m_Phase));
        Vector3 delta = newPos - m_LastPos;
        m_LastPos = newPos;
        transform.position = newPos;

        // Carry standing player directly via world delta (zero child scale distortion!)
        if (gm.Player != null)
        {
            Vector3 p = gm.Player.transform.position;
            float halfX = Size.x * 0.5f + 0.45f;
            float halfZ = Size.z * 0.5f + 0.45f;
            float topY = newPos.y + Size.y * 0.5f;
            if (Mathf.Abs(p.x - newPos.x) <= halfX &&
                Mathf.Abs(p.z - newPos.z) <= halfZ &&
                p.y >= topY - 0.45f && p.y <= topY + 1.15f)
            {
                gm.Player.transform.position = p + delta;
            }
        }
    }
}

public class HazardFloorComp : MonoBehaviour
{
    private Vector3 m_Size = new Vector3(6f, 0.5f, 6f);

    void Start()
    {
        BoxCollider bc = GetComponent<BoxCollider>();
        if (bc != null) m_Size = Vector3.Scale(bc.size, transform.lossyScale);
        else m_Size = transform.lossyScale;
    }

    void Update()
    {
        Aadish3DGame gm = Aadish3DGame.Instance;
        if (gm == null || gm.Player == null || gm.CurrentState != Aadish3DGame.GameState.Playing) return;

        Vector3 p = gm.Player.transform.position;
        Vector3 c = transform.position;
        if (Mathf.Abs(p.x - c.x) <= m_Size.x * 0.5f + 0.35f &&
            Mathf.Abs(p.z - c.z) <= m_Size.z * 0.5f + 0.35f &&
            p.y >= c.y - 0.8f && p.y <= c.y + m_Size.y * 0.5f + 0.9f)
        {
            gm.DamagePlayer(1, c);
        }
    }
}

public class ShrinkAndDie : MonoBehaviour
{
    public float Duration = 0.5f;
    private float m_Timer = 0f;
    private Vector3 m_StartScale;

    void Start()
    {
        m_StartScale = transform.localScale;
    }

    void Update()
    {
        m_Timer += Time.deltaTime;
        float t = 1f - Mathf.Clamp01(m_Timer / Mathf.Max(0.05f, Duration));
        transform.localScale = m_StartScale * t;
        if (m_Timer >= Duration) Destroy(gameObject);
    }
}
