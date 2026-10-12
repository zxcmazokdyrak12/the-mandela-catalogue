using UnityEngine;
using System.Collections;

// Old CRT: animated static, flickering glow, hiss, and a plug that can be ripped out of the socket.
public class TVSet : MonoBehaviour
{
    public Renderer screen;
    public Light glow;
    public Transform plug;
    public bool pluggedIn = true;
    public bool intruderShowing;
    [Tooltip("Your own picture of the intruder for the screen")]
    public Texture2D intruderImage;
    public float emission = 2.2f;
    public AudioClip staticClip;
    [Range(0, 1)] public float staticVolume = 0.3f;

    Material mat;
    Texture2D noise;
    Color32[] buf;
    AudioSource hiss;
    float nextNoise, baseIntensity, collapse = -1f, warmup = 1f;
    Vector3 plugInPos; Quaternion plugInRot;
    readonly Color staticTint = new Color(0.62f, 0.72f, 0.85f);
    readonly Color intruderTint = new Color(1f, 0.86f, 0.82f);
    const int W = 160, H = 120;

    void Start()
    {
        mat = screen.material;
        if (intruderImage) FixScreenUVs();
        noise = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        buf = new Color32[W * H];
        mat.EnableKeyword("_EMISSION");
        mat.SetTexture("_EmissionMap", noise);
        mat.SetTexture("_BaseMap", Texture2D.blackTexture);
        mat.SetTextureScale("_BaseMap", Vector2.one); mat.SetTextureOffset("_BaseMap", Vector2.zero);
        baseIntensity = glow.intensity;
        plugInPos = plug.position; plugInRot = plug.rotation;
        hiss = gameObject.AddComponent<AudioSource>();
        hiss.clip = staticClip ? staticClip : SfxBank.TVStatic();
        hiss.loop = true; hiss.spatialBlend = 1f; hiss.rolloffMode = AudioRolloffMode.Logarithmic;
        hiss.minDistance = 0.8f; hiss.maxDistance = 14f; hiss.volume = staticVolume;
        hiss.Play();
    }

    // the screen mesh has world-space UVs from export; rebuild 0..1 UVs so pictures fit the tube
    void FixScreenUVs()
    {
        var mf = screen.GetComponent<MeshFilter>();
        var mesh = mf.mesh;
        var v = mesh.vertices; var uv = new Vector2[v.Length]; var w = new Vector3[v.Length];
        var t = screen.transform;
        float minZ = float.MaxValue, maxZ = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < v.Length; i++)
        {
            w[i] = t.TransformPoint(v[i]);
            minZ = Mathf.Min(minZ, w[i].z); maxZ = Mathf.Max(maxZ, w[i].z);
            minY = Mathf.Min(minY, w[i].y); maxY = Mathf.Max(maxY, w[i].y);
        }
        for (int i = 0; i < v.Length; i++)
            uv[i] = new Vector2((w[i].z - minZ) / (maxZ - minZ), (w[i].y - minY) / (maxY - minY));
        mesh.uv = uv;
    }

    void Update()
    {
        if (!pluggedIn)
        {
            hiss.volume = 0f;
            if (collapse >= 0f)
            {
                collapse += Time.deltaTime;
                float k = Mathf.Clamp01(1f - collapse / 0.22f);
                mat.SetColor("_EmissionColor", Color.white * emission * 2.5f * k);
                glow.intensity = baseIntensity * 1.6f * k;
                if (collapse > 0.22f) collapse = -1f;
            }
            else
            {
                mat.SetColor("_EmissionColor", Color.black);
                glow.enabled = false;
            }
            return;
        }
        glow.enabled = true;
        warmup = Mathf.MoveTowards(warmup, 1f, Time.deltaTime * 1.2f);
        bool showImage = intruderShowing && intruderImage != null;
        if (Time.time > nextNoise)
        {
            nextNoise = Time.time + 0.045f;
            if (!showImage) FillNoise();
            mat.SetTexture("_EmissionMap", showImage ? (Texture)intruderImage : noise);
            mat.SetTextureOffset("_BaseMap", showImage && Random.value < 0.15f ? new Vector2(Random.Range(-0.03f, 0.03f), 0f) : Vector2.zero);
        }
        float flick = (0.78f + 0.22f * Mathf.PerlinNoise(Time.time * 9f, 0.3f) - (Random.value < 0.03f ? 0.35f : 0f)) * warmup;
        Color c = intruderShowing ? intruderTint : staticTint;
        mat.SetColor("_EmissionColor", c * emission * flick);
        glow.color = c;
        glow.intensity = baseIntensity * flick;
        hiss.volume = staticVolume * (intruderShowing ? 1.6f : 1f) * Mathf.Lerp(0.7f, 1f, flick) * warmup;
            if (NightRouter.Night >= 2) hiss.volume = 0f;   // nights 2 and 3 happen elsewhere: no TV static
        hiss.pitch = intruderShowing ? 0.8f + 0.06f * Mathf.Sin(Time.time * 7f) : 1f;
    }

    void FillNoise()
    {
        int bar = (int)(Time.time * 45f) % H;
        for (int y = 0; y < H; y++)
        {
            int roll = Mathf.Abs(y - bar) < 5 ? 55 : 0;
            for (int x = 0; x < W; x++)
            {
                int n = Random.Range(0, 200) + roll;
                if (intruderShowing) n = (n * 3 / 4) + (((x / 8 + y / 6) & 1) == 0 ? 40 : 0);
                byte b = (byte)Mathf.Min(n, 255);
                buf[y * W + x] = new Color32(b, b, b, 255);
            }
        }
        noise.SetPixels32(buf); noise.Apply(false);
    }

    public void Unplug() { pluggedIn = false; collapse = 0f; intruderShowing = false; }
    public void PlugIn() { pluggedIn = true; warmup = 0f; }

    public IEnumerator TugPlug()
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.14f)
        {
            plug.position = plugInPos + Vector3.right * 0.015f * Mathf.Sin(t * Mathf.PI);
            yield return null;
        }
        plug.position = plugInPos;
    }

    public IEnumerator YankPlug()
    {
        Vector3 p0 = plugInPos, p1 = plugInPos + new Vector3(0.28f, -0.05f, -0.05f), p2 = new Vector3(plugInPos.x + 0.34f, 0.02f, plugInPos.z - 0.12f);
        Quaternion r1 = plugInRot * Quaternion.Euler(30f, 70f, 15f);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.08f) { plug.position = Vector3.Lerp(p0, p1, t); yield return null; }
        Unplug();
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
        {
            plug.position = Vector3.Lerp(p1, p2, t * t);
            plug.rotation = Quaternion.Slerp(plugInRot, r1, t);
            yield return null;
        }
        plug.position = p2; plug.rotation = r1;
    }

    public IEnumerator InsertPlug()
    {
        Vector3 a = plug.position; Quaternion ar = plug.rotation;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
        {
            float e = 1f - (1f - t) * (1f - t);
            plug.position = Vector3.Lerp(a, plugInPos, e);
            plug.rotation = Quaternion.Slerp(ar, plugInRot, e);
            yield return null;
        }
        plug.position = plugInPos; plug.rotation = plugInRot;
        PlugIn();
    }
}
