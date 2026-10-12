using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Daytime between the nights. Switched on by NightRouter instead of the night systems.
//   day 1 (after night 1): wake up downstairs, look around the wrecked house, go up the stairs, the bedroom door -> night 2
//   day 2 (after night 2): wake up in the bedroom, look around, go down to the kitchen, the garage door -> night 3
// Grey overcast daylight, no threats. Camcorder OSD in the corner, subtitles for what Mark notices.
public class DayDirector : MonoBehaviour
{
    public static DayDirector I;

    [System.Serializable]
    public class Pose
    {
        public Transform t;
        public Vector3 localEuler;
        [Tooltip("0 = both days")] public int day;
    }

    public DayPlayer player;
    public Light sun;
    public PSXMode psx;

    [Header("What exists on which day (children of the router's dayRoot)")]
    public GameObject[] day1Objects;
    public GameObject[] day2Objects;
    [Tooltip("Doors etc. moved for the day (open the kitchen door, the bedroom door on day 2)")]
    public Pose[] poses;
    [Tooltip("Scene objects hidden on that day (e.g. a photo that is lying on the floor instead)")]
    public GameObject[] hideDay1, hideDay2;

    [Header("Start")]
    public Vector3 day1Eye = new Vector3(0.1f, 1.62f, 0.2f);
    public Vector3 day1Look = new Vector3(0.6f, 1.3f, 4.8f);
    public Vector3 day2Eye = new Vector3(0.6f, 4.48f, -3.6f);
    public Vector3 day2Look = new Vector3(0.6f, 4.2f, -1.0f);
    [TextArea(1, 2)] public string[] day1Intro = { "Six in the morning. It stopped.", "Lynn still isn't back." };
    [TextArea(1, 2)] public string[] day2Intro = { "Morning again.", "I can't stay here another night. The car is in the garage." };
    public string day1Goal = "Upstairs - the bedroom";
    public string day2Goal = "The kitchen - the garage door";
    public string day1Clock = "SAT", day2Clock = "SUN";
    public int startMinutes = 9 * 60 + 12;

    [Header("Daylight")]
    public Color sunColor = new Color(0.82f, 0.85f, 0.88f);
    public float sunIntensity = 0.9f;
    public Vector3 sunEuler = new Vector3(38f, 200f, 0f);
    public Color ambientSky = new Color(0.62f, 0.64f, 0.67f);
    public Color ambientEquator = new Color(0.52f, 0.51f, 0.49f);
    public Color ambientGround = new Color(0.30f, 0.28f, 0.26f);
    public Color skyColor = new Color(0.62f, 0.65f, 0.68f);
    public float fogDensity = 0.035f;
    [Tooltip("Night-only backdrop / window glass - tinted to the day sky colour")]
    public Renderer[] tintToSky;
    public Color tint = new Color(0.78f, 0.80f, 0.82f);
    [Header("Post (the night grade is too dark and blue for the day)")]
    public Volume volume;
    public float postExposure = 1.55f;
    public float saturation = -22f;
    public float temperature = 6f;
    public float vignette = 0.3f;

    [Header("Sound")]
    [Range(0f, 1f)] public float outsideVolume = 0.16f;
    public Transform[] birdPoints;
    public AudioClip exitClip;

    // ---- state
    public int Day { get; private set; }
    float fade = 1f; Color fadeColor = new Color(0.85f, 0.82f, 0.75f);
    string sub; float subUntil; Coroutine talk;
    bool busy; float t0, hintUntil;
    AudioSource outside, voice; AudioClip birdClip; float nextBird;
    GUIStyle sSub, sOsd, sLabel, sHint;

    void OnEnable() { I = this; }

    void Start()
    {
        Day = Mathf.Max(1, NightRouter.Day);
        if (!player) player = FindFirstObjectByType<DayPlayer>(FindObjectsInactive.Include);
        if (!psx) psx = FindFirstObjectByType<PSXMode>();
        foreach (var g in day1Objects) if (g) g.SetActive(Day == 1);
        foreach (var g in day2Objects) if (g) g.SetActive(Day == 2);
        var hide = Day == 1 ? hideDay1 : hideDay2;
        if (hide != null) foreach (var g in hide) if (g) g.SetActive(false);
        if (poses != null) foreach (var p in poses) if (p != null && p.t && (p.day == 0 || p.day == Day)) p.t.localEulerAngles = p.localEuler;
        Daylight();
        if (player)
        {
            player.enabled = true;
            if (Day == 1) player.Place(day1Eye, day1Look); else player.Place(day2Eye, day2Look);
        }
        var cam = player ? player.cam : Camera.main;
        if (cam) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = skyColor; }
        Sounds();
        t0 = Time.time; hintUntil = Time.time + 12f;
        StartCoroutine(Intro());
    }

    void Daylight()
    {
        if (!volume) volume = FindFirstObjectByType<Volume>();
        if (volume && volume.sharedProfile)
        {
            var prof = Instantiate(volume.sharedProfile);
            volume.profile = prof;
            if (prof.TryGet(out ColorAdjustments ca)) { ca.postExposure.Override(postExposure); ca.saturation.Override(saturation); }
            if (prof.TryGet(out WhiteBalance wb)) wb.temperature.Override(temperature);
            if (prof.TryGet(out Vignette vg)) { vg.intensity.Override(vignette); vg.color.Override(new Color(0.08f, 0.07f, 0.06f)); }
        }
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l == sun) continue;
            bool day = false;
            for (var p = l.transform; p; p = p.parent) if (p.name.StartsWith("Day")) { day = true; break; }
            if (!day) l.enabled = false;
        }
        if (sun)
        {
            sun.enabled = true; sun.color = sunColor; sun.intensity = sunIntensity;
            sun.transform.rotation = Quaternion.Euler(sunEuler);
        }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;
        RenderSettings.ambientLight = ambientSky;
        RenderSettings.fogColor = skyColor; RenderSettings.fogDensity = fogDensity;
        if (psx) { psx.fogColor = skyColor; psx.fogDensity = fogDensity; }
        var mpb = new MaterialPropertyBlock();
        if (tintToSky != null)
            foreach (var r in tintToSky)
            {
                if (!r) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", tint); mpb.SetColor("_Color", tint);
                mpb.SetTexture("_BaseMap", Texture2D.whiteTexture); mpb.SetTexture("_MainTex", Texture2D.whiteTexture);   // no painted moon
                r.SetPropertyBlock(mpb);
            }
    }

    // ---------------------------------------------------------------- sound
    void Sounds()
    {
        outside = gameObject.AddComponent<AudioSource>();
        outside.clip = MakeOutside(); outside.loop = true; outside.spatialBlend = 0f; outside.volume = 0f; outside.Play();
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false; voice.spatialBlend = 0.85f; voice.minDistance = 1.2f; voice.maxDistance = 14f;
        birdClip = MakeBird();
        nextBird = Time.time + Random.Range(2f, 5f);
    }

    static AudioClip MakeOutside()
    {
        int rate = 22050, n = rate * 12;
        var d = new float[n];
        var rng = new System.Random(5);
        float b = 0f, lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float w = (float)rng.NextDouble() * 2f - 1f;
            b = (b + 0.02f * w) / 1.02f;            // brown-ish
            lp += (b - lp) * 0.08f;
            float t = (float)i / n;
            float swell = 0.65f + 0.35f * Mathf.Sin(t * Mathf.PI * 2f * 2f) * Mathf.Sin(t * Mathf.PI * 2f * 3f + 1f);
            d[i] = lp * 3.2f * swell;
        }
        int x = rate;                                 // crossfade the tail into the head for a seamless loop
        for (int i = 0; i < x; i++) { float k = (float)i / x; d[i] = d[i] * k + d[n - x + i] * (1f - k); }
        var o = new float[n - x]; System.Array.Copy(d, 0, o, 0, n - x);
        var c = AudioClip.Create("day_outside", o.Length, 1, rate, false); c.SetData(o, 0); return c;
    }

    static AudioClip MakeBird()
    {
        int rate = 22050, n = (int)(rate * 0.9f);
        var d = new float[n]; float ph = 0f;
        for (int k = 0; k < 3; k++)
        {
            int s0 = (int)(rate * (0.04f + k * 0.24f)), len = (int)(rate * 0.12f);
            float f0 = 3300f + k * 250f, f1 = 4300f - k * 300f;
            for (int i = 0; i < len && s0 + i < n; i++)
            {
                float t = (float)i / len;
                float f = Mathf.Lerp(f0, f1, Mathf.Sin(t * Mathf.PI));
                ph += 2f * Mathf.PI * f / rate;
                d[s0 + i] += Mathf.Sin(ph) * Mathf.Sin(t * Mathf.PI) * 0.35f;
            }
        }
        var c = AudioClip.Create("day_bird", n, 1, rate, false); c.SetData(d, 0); return c;
    }

    // ---------------------------------------------------------------- flow
    IEnumerator Intro()
    {
        for (float f = 1f; f > 0f; f -= Time.deltaTime / 2.5f) { fade = f; yield return null; }
        fade = 0f;
        yield return new WaitForSeconds(0.6f);
        if (!busy) talk = StartCoroutine(SayLines(Day == 1 ? day1Intro : day2Intro, 3.2f));
    }

    public void Use(DayItem it)
    {
        if (busy) return;
        if (it.kind == DayItem.Kind.Exit) { StartCoroutine(Exit(it)); return; }
        if (talk != null) StopCoroutine(talk);
        talk = StartCoroutine(Look(it));
    }

    IEnumerator Look(DayItem it)
    {
        if (it.clip || it.preClip)
        {
            voice.Stop();
            voice.transform.position = it.transform.position;
            Filter(it.speakerFilter);
            if (it.preClip) { voice.PlayOneShot(it.preClip, it.volume); }
            if (it.clip) { voice.clip = it.clip; voice.volume = it.volume; voice.PlayDelayed(it.clipDelay + (it.preClip ? it.preClip.length : 0f)); }
        }
        yield return SayLines(it.lines, it.lineTime);
    }

    void Filter(bool on)
    {
        var hp = voice.GetComponent<AudioHighPassFilter>(); if (!hp) hp = voice.gameObject.AddComponent<AudioHighPassFilter>();
        var lp = voice.GetComponent<AudioLowPassFilter>(); if (!lp) lp = voice.gameObject.AddComponent<AudioLowPassFilter>();
        var ds = voice.GetComponent<AudioDistortionFilter>(); if (!ds) ds = voice.gameObject.AddComponent<AudioDistortionFilter>();
        hp.enabled = lp.enabled = ds.enabled = on;
        hp.cutoffFrequency = 650f; lp.cutoffFrequency = 3200f; ds.distortionLevel = 0.45f;
    }

    IEnumerator SayLines(string[] lines, float each)
    {
        if (lines == null) yield break;
        foreach (var l in lines)
        {
            if (string.IsNullOrEmpty(l)) continue;
            sub = l; subUntil = Time.time + each;
            yield return new WaitForSeconds(each);
        }
    }

    public void Say(string line, float time = 3.4f) { sub = line; subUntil = Time.time + time; }

    IEnumerator Exit(DayItem it)
    {
        busy = true;
        if (talk != null) StopCoroutine(talk);
        if (player) player.frozen = true;
        if (it.lines != null && it.lines.Length > 0) Say(it.lines[0], 4f);
        var c = it.clip ? it.clip : exitClip;
        if (c) AudioSource.PlayClipAtPoint(c, it.transform.position, it.volume);
        fadeColor = Color.black;
        yield return new WaitForSeconds(0.9f);
        for (float f = 0f; f < 1f; f += Time.deltaTime / 2f)
        {
            fade = f;
            if (outside) outside.volume = outsideVolume * (1f - f);
            yield return null;
        }
        fade = 1f; sub = null;
        yield return new WaitForSeconds(1.2f);
        NightTimer.carryDay = 0;
        NightTimer.carryNight = Day + 1;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void Update()
    {
        if (outside && !busy && outside.volume < outsideVolume) outside.volume = Mathf.MoveTowards(outside.volume, outsideVolume, Time.deltaTime * outsideVolume / 3f);
        if (birdPoints != null && birdPoints.Length > 0 && Time.time > nextBird && !busy)
        {
            nextBird = Time.time + Random.Range(4f, 11f);
            var p = birdPoints[Random.Range(0, birdPoints.Length)];
            if (p)
            {
                var go = new GameObject("Bird"); go.transform.position = p.position;
                var a = go.AddComponent<AudioSource>(); a.clip = birdClip; a.spatialBlend = 1f; a.minDistance = 2f; a.maxDistance = 25f;
                a.pitch = Random.Range(0.85f, 1.2f); a.volume = Random.Range(0.12f, 0.25f); a.Play();
                Destroy(go, 2f);
            }
        }
    }

    // ---------------------------------------------------------------- HUD
    void Styles()
    {
        if (sSub != null) return;
        sSub = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
        sOsd = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Bold };
        sLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
        sHint = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
    }

    static void Shadow(Rect r, string s, GUIStyle st, Color c)
    {
        s = Loc.T(s);
        GUI.color = new Color(0f, 0f, 0f, c.a * 0.8f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, st);
        GUI.color = c; GUI.Label(r, s, st);
    }

    void OnGUI()
    {
        if (GameMenu.Paused) return;
        Styles();
        float w = Screen.width, h = Screen.height, u = h / 720f;
        float a = 1f - fade;

        if (a > 0.01f && !busy)
        {
            // camcorder OSD
            int mins = startMinutes + (int)((Time.time - t0) / 6f);
            int hh = mins / 60 % 12; if (hh == 0) hh = 12;
            sOsd.fontSize = Mathf.RoundToInt(28 * u);
            Shadow(new Rect(0f, 22f * u, w - 34f * u, 44f * u), (Day == 1 ? day1Clock : day2Clock) + "  " + hh + ":" + (mins % 60).ToString("00") + " AM", sOsd, new Color(0.95f, 0.95f, 0.9f, 0.8f * a));
            sOsd.fontSize = Mathf.RoundToInt(16 * u);
            Shadow(new Rect(0f, 58f * u, w - 36f * u, 26f * u), "DAY " + Day, sOsd, new Color(0.9f, 0.9f, 0.85f, 0.55f * a));
            float g = Mathf.Clamp01((Time.time - t0 - 8f) * 0.5f);
            if (g > 0f) Shadow(new Rect(0f, 82f * u, w - 36f * u, 26f * u), "►  " + Loc.T(Day == 1 ? day1Goal : day2Goal), sOsd, new Color(0.9f, 0.9f, 0.85f, 0.5f * a * g));

            // crosshair + what you point at
            var hv = player ? player.Hover : null;
            GUI.color = new Color(1f, 1f, 1f, (hv ? 0.85f : 0.35f) * a);
            float cs = (hv ? 6f : 4f) * u;
            GUI.DrawTexture(new Rect(w / 2 - cs / 2, h / 2 - cs / 2, cs, cs), Texture2D.whiteTexture);
            if (hv)
            {
                sLabel.fontSize = Mathf.RoundToInt(19 * u);
                Shadow(new Rect(0, h / 2 + 16 * u, w, 30 * u), hv.label, sLabel, new Color(0.95f, 0.93f, 0.85f, 0.9f * a));
            }

            if (Time.time < hintUntil)
            {
                sHint.fontSize = Mathf.RoundToInt(16 * u);
                Shadow(new Rect(0f, h - 40f * u, w, 26f * u), "WASD — walk        Shift — run        LMB — look / use", sHint, new Color(1f, 1f, 1f, Mathf.Clamp01(hintUntil - Time.time) * 0.7f * a));
            }
        }

        if (!string.IsNullOrEmpty(sub) && Time.time < subUntil && (GameSettings.Subtitles || !sub.StartsWith("\"")))
        {
            float sa = Mathf.Clamp01((subUntil - Time.time) * 2f);
            sSub.fontSize = Mathf.RoundToInt(22 * u);
            Shadow(new Rect(w * 0.1f, h - 120f * u, w * 0.8f, 60f * u), sub, sSub, new Color(1f, 0.97f, 0.88f, sa));
        }

        if (fade > 0f)
        {
            GUI.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, fade);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
        }
        GUI.color = Color.white;
    }
}
