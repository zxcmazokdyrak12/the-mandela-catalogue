using UnityEngine;

// Builds a cone mesh along +Z matching the spot light's outer angle and drives the BeamCone material.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class BeamCone : MonoBehaviour
{
    public Light source;
    public float length = 5f;
    public float startRadius = 0.025f;
    [Range(0.2f, 1f)] public float angleScale = 0.7f;   // the visible shaft is narrower than the light's falloff
    public float intensity = 0.55f;
    public int segments = 32;

    MeshRenderer mr; Material mat; float baseLightIntensity = -1f;

    void Awake()
    {
        mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mat = mr.material;
        Build();
    }

    void Build()
    {
        float angle = source ? source.spotAngle : 50f;
        float endRadius = Mathf.Tan(angle * 0.5f * angleScale * Mathf.Deg2Rad) * length + startRadius;
        var v = new Vector3[(segments + 1) * 2]; var n = new Vector3[v.Length]; var uv = new Vector2[v.Length];
        var t = new int[segments * 6];
        float slope = (endRadius - startRadius) / length;
        for (int i = 0; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Vector3 normal = new Vector3(d.x, d.y, -slope).normalized;
            v[i * 2] = new Vector3(d.x * startRadius, d.y * startRadius, 0f);
            v[i * 2 + 1] = new Vector3(d.x * endRadius, d.y * endRadius, length);
            n[i * 2] = n[i * 2 + 1] = normal;
            uv[i * 2] = new Vector2(i / (float)segments, 0f);
            uv[i * 2 + 1] = new Vector2(i / (float)segments, 1f);
        }
        for (int i = 0; i < segments; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3, k = i * 6;
            t[k] = a; t[k + 1] = b; t[k + 2] = c; t[k + 3] = c; t[k + 4] = b; t[k + 5] = d;
        }
        var m = new Mesh { name = "BeamCone" };
        m.vertices = v; m.normals = n; m.uv = uv; m.triangles = t;
        m.bounds = new Bounds(new Vector3(0, 0, length * 0.5f), new Vector3(endRadius * 2f, endRadius * 2f, length));
        GetComponent<MeshFilter>().sharedMesh = m;
    }

    void LateUpdate()
    {
        bool on = source && source.enabled && source.isActiveAndEnabled && source.intensity > 0.01f;
        if (mr.enabled != on) mr.enabled = on;
        if (!on) return;
        if (baseLightIntensity < 0f) baseLightIntensity = Mathf.Max(0.01f, source.intensity);
        mat.SetFloat("_Intensity", intensity * source.intensity / baseLightIntensity);
    }
}
