using UnityEngine;
using System.Collections.Generic;

// Round cable from the TV plug down to the floor and into the TV stand.
// Rebuilt whenever the plug moves, so it stays attached when the plug is ripped out.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class Cord : MonoBehaviour
{
    public Transform plug;
    public Vector3 plugTailWorld = new Vector3(-1.55f, 0.3f, 0.75f);
    public Vector3 outDirWorld = Vector3.right;
    public Vector3[] restPoints = { new Vector3(-1.5f, 0.012f, 0.66f), new Vector3(-1.52f, 0.012f, 0.5f), new Vector3(-1.56f, 0.25f, 0.35f) };
    public float radius = 0.0045f;
    public int sides = 8, samplesPerSpan = 10;

    Mesh mesh; Vector3 tailLocal, outLocal, lastPos; Quaternion lastRot;

    void Start()
    {
        mesh = new Mesh { name = "CordMesh" };
        mesh.MarkDynamic();
        GetComponent<MeshFilter>().sharedMesh = mesh;
        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        transform.localScale = Vector3.one;
        tailLocal = plug.InverseTransformPoint(plugTailWorld);
        outLocal = plug.InverseTransformDirection(outDirWorld);
        Build();
    }

    void LateUpdate()
    {
        if (plug.position != lastPos || plug.rotation != lastRot) Build();
    }

    static Vector3 CR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    void Build()
    {
        lastPos = plug.position; lastRot = plug.rotation;
        Vector3 a = plug.TransformPoint(tailLocal);
        Vector3 dir = plug.TransformDirection(outLocal).normalized;
        var ctrl = new List<Vector3> { a - dir * 0.05f, a, a + dir * 0.06f + Vector3.down * 0.03f };
        ctrl.AddRange(restPoints);
        int L = restPoints.Length;
        ctrl.Add(restPoints[L - 1] + (restPoints[L - 1] - restPoints[L - 2]));
        var pts = new List<Vector3>();
        for (int s = 1; s < ctrl.Count - 2; s++)
            for (int k = 0; k < samplesPerSpan; k++)
                pts.Add(CR(ctrl[s - 1], ctrl[s], ctrl[s + 1], ctrl[s + 2], k / (float)samplesPerSpan));
        pts.Add(ctrl[ctrl.Count - 2]);
        for (int i = 0; i < pts.Count; i++) if (pts[i].y < radius) pts[i] = new Vector3(pts[i].x, radius, pts[i].z);

        int rings = pts.Count;
        var v = new Vector3[rings * sides]; var nrm = new Vector3[rings * sides]; var uv = new Vector2[rings * sides];
        var tri = new int[(rings - 1) * sides * 6];
        Vector3 prevN = Vector3.up; float len = 0f;
        for (int i = 0; i < rings; i++)
        {
            Vector3 tng = (i < rings - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).normalized;
            Vector3 n = Vector3.ProjectOnPlane(prevN, tng);
            if (n.sqrMagnitude < 1e-6f) n = Vector3.ProjectOnPlane(Vector3.forward, tng);
            n.Normalize(); prevN = n;
            Vector3 b = Vector3.Cross(tng, n);
            if (i > 0) len += (pts[i] - pts[i - 1]).magnitude;
            for (int s = 0; s < sides; s++)
            {
                float ang = s / (float)sides * Mathf.PI * 2f;
                Vector3 off = n * Mathf.Cos(ang) + b * Mathf.Sin(ang);
                v[i * sides + s] = pts[i] + off * radius;
                nrm[i * sides + s] = off;
                uv[i * sides + s] = new Vector2(s / (float)sides, len * 20f);
            }
        }
        int ti = 0;
        for (int i = 0; i < rings - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                int a0 = i * sides + s, a1 = i * sides + (s + 1) % sides, b0 = a0 + sides, b1 = a1 + sides;
                tri[ti++] = a0; tri[ti++] = a1; tri[ti++] = b0;
                tri[ti++] = a1; tri[ti++] = b1; tri[ti++] = b0;
            }
        mesh.Clear();
        mesh.vertices = v; mesh.normals = nrm; mesh.uv = uv; mesh.triangles = tri;
        mesh.RecalculateBounds();
    }
}
