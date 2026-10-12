using System.Collections;
using UnityEngine;

// The curtains have been closed too long: a gust bursts the window open, the curtains fly up into the room
// and flap, then the wind drops, they sink back and the window bangs shut. The curtains are left open.
public class WindowGust : MonoBehaviour
{
    public PanicPlayer player;
    public Transform leafL, leafR;           // hinged window leaves (pivot on the outer edge)
    public Transform curtainL, curtainR;
    public float rodHeight = 2.38f;
    public AudioClip wind, bang, creak;
    [Tooltip("Recorded window: the latch and sash opening, then the outside noise rushing in.")]
    public AudioClip openClip;
    [Tooltip("Recorded window being shut and latched.")]
    public AudioClip closeClip;
    [Range(0f, 1f)] public float windLayer = 0.35f;
    public float openAngle = 72f;
    public float blowTime = 2.6f;

    Transform pivotL, pivotR;
    Quaternion leafLRest, leafRRest;
    AudioSource windSrc;
    bool busy;
    public bool Busy => busy;

    void Start()
    {
        if (!player) player = FindFirstObjectByType<PanicPlayer>();
        if (leafL) leafLRest = leafL.localRotation;
        if (leafR) leafRRest = leafR.localRotation;
        pivotL = MakePivot(curtainL, "CurtainL_Pivot");
        pivotR = MakePivot(curtainR, "CurtainR_Pivot");
        windSrc = gameObject.AddComponent<AudioSource>();
        windSrc.spatialBlend = 1f; windSrc.minDistance = 1.5f; windSrc.maxDistance = 20f; windSrc.playOnAwake = false;
    }

    // hang each curtain from a pivot on the rod so it can swing up into the room
    Transform MakePivot(Transform curtain, string name)
    {
        if (!curtain) return null;
        var r = curtain.GetComponent<Renderer>();
        Vector3 c = r ? r.bounds.center : curtain.position;
        var p = new GameObject(name).transform;
        p.SetParent(curtain.parent, false);
        p.position = new Vector3(c.x, rodHeight, c.z);
        p.rotation = Quaternion.identity;
        curtain.SetParent(p, true);
        return p;
    }

    public void Blow() { if (!busy) StartCoroutine(Gust()); }

    IEnumerator Gust()
    {
        busy = true;
        Vector3 at = (leafL ? leafL.position : transform.position) + Vector3.up * 0.4f;
        windSrc.transform.position = at;
        float wv = openClip ? windLayer : 0.9f;
        if (wind) { windSrc.clip = wind; windSrc.volume = wv; windSrc.pitch = Random.Range(0.92f, 1.05f); windSrc.Play(); }
        if (openClip) PlayAt(openClip, at, 1f);
        else if (creak) AudioSource.PlayClipAtPoint(creak, at, 0.5f);

        // 1. the leaves burst open, the curtains fly apart and up
        float curtainFrom = player ? (player.CurtainsClosed ? 1f : 0f) : 1f;
        if (player) player.WindOpenedCurtains();
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
        {
            float e = 1f - Mathf.Pow(1f - t, 3f);
            float over = Mathf.Sin(t * Mathf.PI) * 8f;
            Leaves(openAngle * e + over);
            if (player) player.SetCurtainAmount(Mathf.Lerp(curtainFrom, 0f, e));
            Billow(e * 55f, t * 0.35f);
            yield return null;
        }

        // 2. it blows: the curtains flap, the leaves rattle on their hinges
        for (float t = 0f; t < blowTime; t += Time.deltaTime)
        {
            float k = 1f - Mathf.SmoothStep(0f, 1f, t / blowTime) * 0.35f;
            Leaves(openAngle + Mathf.Sin(t * 13f) * 3f * k + (Mathf.PerlinNoise(t * 3f, 1.7f) - 0.5f) * 10f);
            Billow((45f + Mathf.PerlinNoise(t * 1.8f, 0.4f) * 25f) * k, t + 0.35f);
            yield return null;
        }

        // 3. the wind drops, the curtains sink back down
        float lift0 = 45f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 1.2f)
        {
            float e = t * t * (3f - 2f * t);
            Billow(Mathf.Lerp(lift0, 0f, e) + Mathf.Sin(t * 20f) * 6f * (1f - t), t + blowTime);
            Leaves(Mathf.Lerp(openAngle, openAngle - 15f, e));
            if (windSrc.isPlaying) windSrc.volume = Mathf.Lerp(wv, wv * 0.3f, e);
            yield return null;
        }
        Billow(0f, 0f);

        // 4. ...and one last push slams the window shut
        yield return new WaitForSeconds(0.35f);
        float a0 = openAngle - 15f;
        if (closeClip) PlayAt(closeClip, at, 1f);      // its first clunk lands as the leaves meet the frame
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.16f) { Leaves(Mathf.Lerp(a0, 0f, t * t)); yield return null; }
        Leaves(0f);
        if (!closeClip && bang) AudioSource.PlayClipAtPoint(bang, at, 0.85f);
        if (player) player.Jolt(0.03f, 0.25f);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.8f) { windSrc.volume = Mathf.Lerp(wv * 0.3f, 0f, t); yield return null; }
        windSrc.Stop();
        busy = false;
    }

    static void PlayAt(AudioClip c, Vector3 p, float v)
    {
        var go = new GameObject("WindowSfx"); go.transform.position = p;
        var a = go.AddComponent<AudioSource>();
        a.clip = c; a.spatialBlend = 1f; a.minDistance = 2.5f; a.maxDistance = 25f; a.volume = v; a.dopplerLevel = 0f;
        a.Play(); Destroy(go, c.length + 0.1f);
    }

    // leaves swing into the room: left one about its left edge, right one about its right edge
    void Leaves(float a)
    {
        if (leafL) leafL.localRotation = leafLRest * Quaternion.Euler(0f, a, 0f);
        if (leafR) leafR.localRotation = leafRRest * Quaternion.Euler(0f, -a, 0f);
    }

    // lift the curtains about the rod (bottom goes into the room) with a little flutter
    void Billow(float lift, float t)
    {
        if (pivotL) pivotL.rotation = Quaternion.Euler(lift + Mathf.Sin(t * 9.3f) * lift * 0.12f, Mathf.Sin(t * 5.1f) * lift * 0.25f, Mathf.Sin(t * 7.7f) * lift * 0.08f);
        if (pivotR) pivotR.rotation = Quaternion.Euler(lift * 0.9f + Mathf.Sin(t * 8.1f + 1.3f) * lift * 0.12f, -Mathf.Sin(t * 4.7f + 0.5f) * lift * 0.25f, -Mathf.Sin(t * 6.9f) * lift * 0.08f);
    }
}
