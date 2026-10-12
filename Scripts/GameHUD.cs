using UnityEngine;
using UnityEngine.InputSystem;

// Overlay drawn like an old camcorder's on-screen display (the found-footage look of the Catalogue):
//  - the night clock in the corner (pulses when the hour turns)
//  - the briefing card before the first night (time stands still until you click)
//  - the end screens (death / survived), called by ThreatManager
public class GameHUD : MonoBehaviour
{
    public static GameHUD I;
    public static bool Blocking => I && I.briefing;

    public NightTimer timer;
    public ThreatManager threats;
    public BedroomDirector bedroom;
    public GarageDirector garage;
    public bool briefingEveryNight;

    bool briefing; float briefT, hourPulse, reminderUntil;
    GUIStyle sTitle, sText, sSmall, sOsd, sEnd, sEndSub;
    Texture2D noise; float nextNoise;
    int lastH;

    static readonly string[] Rules =
    {
        "The DOOR creaks open  —  run to it and slam it shut",
        "A figure in the WINDOW with a familiar voice  —  close the curtains",
        "FOOTSTEPS on the stairs  —  go and keep your flashlight on it",
        "A FACE on the TV  —  pull the plug",
        "The M.A.D. SIGNAL  —  cover your ears until it stops",
        "The WARDROBE  —  you can hide, but not for long",
    };
    static readonly string[] Rules2 =
    {
        "A KNOCK at the door  —  3 seconds later they come in. Hide UNDER THE DESK",
        "SHUT DOWN the laptop first (Start → Shut Down)  —  a glowing screen gives you away",
        "No flashlight under the desk, and do not come out while it is in the room",
        "In bed or at the laptop they will find you at once",
        "The LAPTOP  —  files only decrypt while you sit at it with DECRYPT.EXE running",
        "The NEWSPAPERS on the nightstand  —  worth a read",
    };
    static readonly string[] Rules3 =
    {
        "The HANDLE rattles on the house door  —  run and LOCK THE BOLT",
        "BANGING on the roll-up door  —  FREEZE. No noise, no light until it stops",
        "Three SLOW KNOCKS  —  get in the car and DUCK. Do not look. No light",
        "Find the KEYS, FILL the tank, raise the DOOR with the chain  —  then start the engine",
        "Searching, pouring and the chain make NOISE  —  too much and they come",
        "Your wife is in the house. She keeps calling you",
    };
    const string Controls3 = "LMB — interact / hold        RMB (hold) — flashlight        In the car: LMB — duck   Space — get out";
    const string Controls1 = "LMB — interact        RMB (hold) — flashlight        Space / wheel — cover ears";
    const string Controls2 = "LMB — interact        RMB (hold) — flashlight        RMB / Space — stand up from the laptop";
    string Controls => timer && timer.night == 3 ? Controls3 : timer && timer.night == 2 ? Controls2 : Controls1;
    string[] RulesNow => timer && timer.night == 3 ? Rules3 : timer && timer.night == 2 ? Rules2 : Rules;
    public static bool HideClock;

    void Awake() { I = this; }

    void Start()
    {
        if (!timer) timer = FindFirstObjectByType<NightTimer>();
        if (!threats) threats = FindFirstObjectByType<ThreatManager>();
        if (!bedroom) bedroom = FindFirstObjectByType<BedroomDirector>();
        if (!garage) garage = FindFirstObjectByType<GarageDirector>();
        if (NightRouter.IsDay || NightRouter.InMenu || NightRouter.IsIntro) timer = null;    // by day: no clock, no briefing (DayDirector draws its own OSD)
        noise = new Texture2D(96, 54, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        if (timer)
        {
            timer.OnHour += h => hourPulse = 1f;
            if (timer.night <= 3 || briefingEveryNight) { briefing = true; timer.Pause(); }
            else reminderUntil = Time.time + 6f;
        }
    }

    void Update()
    {
        hourPulse = Mathf.MoveTowards(hourPulse, 0f, Time.deltaTime * 0.5f);
        if (!briefing || GameMenu.Paused) return;
        briefT += Time.unscaledDeltaTime;
        var m = Mouse.current; var kb = Keyboard.current;
        bool go = (m != null && m.leftButton.wasReleasedThisFrame) || (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame));
        if (briefT > 1.2f && go) Dismiss();
    }

    public void Dismiss()
    {
        if (!briefing) return;
        briefing = false;
        if (timer) timer.Resume();
        hourPulse = 1f;
    }

    void Styles()
    {
        if (sTitle != null) return;
        sTitle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        sText = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
        sSmall = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
        sOsd = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Bold };
        sEnd = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
        sEndSub = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
    }

    void OnGUI()
    {
        Styles();
        float h = Screen.height, w = Screen.width, u = h / 720f;
        bool over = HideClock || (threats && threats.isActiveAndEnabled && threats.Over) || (bedroom && bedroom.isActiveAndEnabled && bedroom.Over) || (garage && garage.isActiveAndEnabled && garage.Over);

        // ---- clock (camcorder OSD, top right)
        if (timer && !briefing && !over)
        {
            sOsd.fontSize = Mathf.RoundToInt(30 * u * (1f + hourPulse * 0.25f));
            float a = 0.72f + 0.28f * hourPulse;
            Rect r = new Rect(0f, 22f * u, w - 34f * u, 50f * u);
            Shadowed(r, timer.TimeText, sOsd, new Color(0.92f, 0.92f, 0.86f, a));
            sOsd.fontSize = Mathf.RoundToInt(17 * u);
            Shadowed(new Rect(0f, 60f * u, w - 36f * u, 30f * u), "NIGHT " + timer.night, sOsd, new Color(0.85f, 0.85f, 0.8f, 0.55f));
            if (Time.time < reminderUntil)
            {
                sSmall.fontSize = Mathf.RoundToInt(16 * u);
                Shadowed(new Rect(0f, h - 54f * u, w, 30f * u), Controls, sSmall, new Color(1f, 1f, 1f, Mathf.Clamp01(reminderUntil - Time.time) * 0.7f));
            }
        }

        if (briefing) Briefing(w, h, u);
    }

    void Briefing(float w, float h, float u)
    {
        float a = Mathf.Clamp01(briefT * 2f);
        GUI.color = new Color(0f, 0f, 0f, 0.86f * a);
        GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
        Noise(w, h, 0.05f * a);
        float cw = Mathf.Min(980f * u, w - 40f), x0 = (w - cw) / 2f;
        float y = h * 0.12f;

        sTitle.fontSize = Mathf.RoundToInt(46 * u);
        Shadowed(new Rect(0, y, w, 60 * u), "NIGHT " + (timer ? timer.night : 1), sTitle, new Color(0.85f, 0.12f, 0.1f, a));
        y += 62 * u;
        sSmall.fontSize = Mathf.RoundToInt(20 * u);
        Shadowed(new Rect(0, y, w, 30 * u), timer && timer.night == 3 ? "The last night. You are in the garage. Get the car out before they get in." : timer && timer.night == 2 ? "The second night. Survive until 6:00 and find out what is in the archive." : "Survive until 6:00 AM. They look like people you know. Do not let them in.", sSmall, new Color(0.85f, 0.85f, 0.8f, a));
        y += 56 * u;

        sText.fontSize = Mathf.RoundToInt(21 * u);
        var rules = RulesNow;
        for (int i = 0; i < rules.Length; i++)
        {
            Shadowed(new Rect(x0 + 40 * u, y, cw, 34 * u), "•  " + Loc.T(rules[i]), sText, new Color(0.93f, 0.93f, 0.88f, a));
            y += 38 * u;
        }
        y += 22 * u;
        GUI.color = new Color(1f, 1f, 1f, 0.15f * a);
        GUI.DrawTexture(new Rect(x0, y, cw, 1f), Texture2D.whiteTexture);
        y += 16 * u;
        sSmall.fontSize = Mathf.RoundToInt(18 * u);
        Shadowed(new Rect(0, y, w, 30 * u), Controls, sSmall, new Color(0.75f, 0.75f, 0.7f, a));

        float blink = briefT > 1.2f ? (0.45f + 0.55f * Mathf.Abs(Mathf.Sin(briefT * 2.4f))) : 0f;
        sSmall.fontSize = Mathf.RoundToInt(20 * u);
        Shadowed(new Rect(0, h - 90 * u, w, 30 * u), "▶  LMB — start", sSmall, new Color(1f, 1f, 1f, blink * a));
        GUI.color = Color.white;
    }

    // ---- end screens (called from ThreatManager.OnGUI)
    public static void DrawEnd(float fade, Color bg, string title, string sub, bool dead)
    {
        if (!I) return;
        I.Styles();
        float h = Screen.height, w = Screen.width, u = h / 720f;
        GUI.color = new Color(bg.r, bg.g, bg.b, fade);
        GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
        if (string.IsNullOrEmpty(title)) { GUI.color = Color.white; return; }
        float t = Time.unscaledTime;
        if (dead)
        {
            I.Broadcast(w, h, u, title, sub);
            GUI.color = Color.white;
            return;
        }
        else
        {
            I.sEnd.fontSize = Mathf.RoundToInt(58 * u);
            GUI.color = new Color(0.12f, 0.12f, 0.12f, fade);
            GUI.Label(new Rect(0, h * 0.34f, w, 80 * u), Loc.T("6:00 AM"), I.sEnd);
            I.sEndSub.fontSize = Mathf.RoundToInt(24 * u);
            GUI.Label(new Rect(0, h * 0.34f + 84 * u, w, 40 * u), Loc.T(title), I.sEndSub);
            if (!string.IsNullOrEmpty(sub)) { GUI.color = new Color(0.25f, 0.25f, 0.25f, fade * 0.8f); I.sEndSub.fontSize = Mathf.RoundToInt(18 * u); GUI.Label(new Rect(0, h * 0.34f + 126 * u, w, 30 * u), Loc.T(sub), I.sEndSub); }
        }
        GUI.color = Color.white;
    }

    // ---- death: the tape tears and Mark flashes through it, the case text squashing and stretching
    public static string EndInfo = "";
    public Texture2D subliminal;          // unused now, kept so the scene reference survives
    float bcStart = -1f; string bcFor;

    [Header("Death flashes")]
    [Tooltip("0 raw, 1 black-and-white, 2 inverted, 3 red. Flashed in torn cuts between the case text.")]
    public Texture2D[] deathFlashes;
    public Font typeFont;
    public const float BurstTime = 7.6f;
    // how long the managers hold the death screen before reloading
    public static float DeathHold => 0.32f + ((I && I.HasBurst) ? BurstTime : 2f) + 0.4f;
    public static bool BurstOn => I && I.HasBurst;
    struct Cut { public float start, end; public int kind, tex, line; public Vector2 off, s0, s1; public float zoom; }
    readonly System.Collections.Generic.List<Cut> cuts = new System.Collections.Generic.List<Cut>();
    GUIStyle sType, sLine;
    AudioSource burstSrc;
    static readonly string[] DeathText =
    {
        "MARK HEATHCLIFF, 34.",
        "Found in the upstairs bedroom, 6:04 AM.",
        "The door was locked from the inside.",
        "There was no one else in the house.",
        "His wife says she was at her mother's.",
    };
    public bool HasBurst => deathFlashes != null && deathFlashes.Length >= 4;

    // kinds: 0 black, 1 picture, 2 text block (squash/stretch), 3 picture + text, 4 final chewed text, 5 one line huge (squash/stretch)
    void BuildBurst()
    {
        cuts.Clear();
        float t = 0f;
        void Add(float d, int kind, int tex, int line, Vector2 s0, Vector2 s1)
        {
            cuts.Add(new Cut { start = t, end = t + d, kind = kind, tex = tex, line = line, s0 = s0, s1 = s1,
                off = new Vector2(Random.Range(-0.06f, 0.06f), Random.Range(-0.05f, 0.05f)), zoom = Random.Range(1f, 1.25f) });
            t += d;
        }
        void C(float d, int kind, int tex) => Add(d, kind, tex, 0, Vector2.one, Vector2.one);
        Vector2 RS() => Random.value < 0.5f ? new Vector2(Random.Range(0.45f, 0.75f), Random.Range(1.6f, 2.8f))   // thin and tall
                                            : new Vector2(Random.Range(1.5f, 2.6f), Random.Range(0.35f, 0.7f));  // wide and flat

        C(0.20f, 1, 3); C(0.05f, 0, 0); C(0.07f, 1, 1); C(0.04f, 0, 0); C(0.06f, 1, 1);
        Add(0.55f, 2, 0, 0, new Vector2(1f, 1f), new Vector2(0.55f, 2.1f));
        C(0.09f, 3, 1); C(0.05f, 0, 0); C(0.10f, 1, 0); C(0.04f, 1, 2);
        Add(0.60f, 5, 0, 0, new Vector2(2.6f, 0.55f), new Vector2(0.6f, 2.6f));
        C(0.06f, 0, 0); C(0.08f, 1, 3); C(0.05f, 0, 0);
        Add(0.45f, 2, 0, 0, new Vector2(1.9f, 0.5f), new Vector2(1f, 1f));
        int line = 1;
        while (t < BurstTime - 1.75f)
        {
            float r = Random.value;
            if (r < 0.16f) Add(Random.Range(0.3f, 0.55f), 5, 0, line++ % DeathText.Length, RS(), RS());
            else if (r < 0.28f) Add(Random.Range(0.3f, 0.5f), 2, 0, 0, RS(), RS());
            else if (r < 0.45f) C(Random.Range(0.03f, 0.08f), 0, 0);
            else if (r < 0.8f) C(Random.Range(0.04f, 0.12f), 1, Random.Range(0, 4));
            else C(Random.Range(0.06f, 0.14f), 3, Random.Range(0, 3));
        }
        C(0.08f, 1, 3); C(0.05f, 0, 0);
        Add(BurstTime - 0.2f - t, 4, 0, 0, new Vector2(1.15f, 2.7f), new Vector2(1.15f, 2.7f));
        C(BurstTime - t, 0, 0);
        PlayBurst();
    }

    // the sound: a pre-made bed (drone, slowed reversed voices, heartbeat, tinnitus, the tape stopping at the end)
    // cut to the pictures - harsh clipped static on every flash, near silence on every black frame
    [Tooltip("death_bed.wav - the long layer the cuts chop up")]
    public AudioClip deathBed;
    void PlayBurst()
    {
        if (!burstSrc) { burstSrc = gameObject.AddComponent<AudioSource>(); burstSrc.spatialBlend = 0f; burstSrc.playOnAwake = false; burstSrc.ignoreListenerPause = true; burstSrc.ignoreListenerVolume = true; }
        int rate = 22050, n = (int)(rate * BurstTime); var d = new float[n];
        float[] bed = null; int bedRate = rate;
        if (deathBed && deathBed.loadState == AudioDataLoadState.Loaded || deathBed && deathBed.LoadAudioData())
        {
            bed = new float[deathBed.samples * deathBed.channels];
            if (!deathBed.GetData(bed, 0)) bed = null;
            bedRate = deathBed.frequency;
        }
        var rng = new System.Random(Random.Range(0, 99999)); float lp = 0f, ph = 0f, gain = 0f;
        int ci = 0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            while (ci < cuts.Count - 1 && t >= cuts[ci].end) ci++;
            var c = cuts[ci]; int k = c.kind;
            float p = Mathf.Clamp01((t - c.start) / Mathf.Max(0.01f, c.end - c.start));
            float wn = (float)(rng.NextDouble() * 2 - 1);
            lp += (wn - lp) * 0.5f;
            float b = 0f;
            if (bed != null) { int j = (int)(t * bedRate) * deathBed.channels; if (j < bed.Length) b = bed[j]; }
            float target = k == 0 ? 0.06f : k == 1 ? 0.9f : k == 3 ? 1f : 0.85f;
            gain += (target - gain) * 0.02f;
            float s = b * gain;
            if (k == 1) s += Mathf.Clamp(wn * 1.6f, -1f, 1f) * 0.42f;                     // flash: torn static
            else if (k == 3) s += Mathf.Clamp(lp * 2.2f, -1f, 1f) * 0.32f + Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 55f * t)) * 0.12f;
            else if (k == 2 || k == 5)
            {
                Vector2 sc = Vector2.Lerp(c.s0, c.s1, p);
                float f = 70f + 140f * sc.y / Mathf.Max(0.3f, sc.x);
                ph += f / rate; ph -= Mathf.Floor(ph);
                s += (ph < 0.5f ? 0.09f : -0.09f);                                       // the text hums as it is pulled
            }
            d[i] = Mathf.Clamp(s, -1f, 1f);
        }
        if (bed == null) Debug.LogWarning("GameHUD: deathBed missing, death sound is static only");
        var clip = AudioClip.Create("death_burst", n, 1, rate, false); clip.SetData(d, 0);
        burstSrc.clip = clip; burstSrc.volume = 0.85f * GameSettings.Volume; burstSrc.PlayDelayed(0.32f);
    }

    // a scale that moves in steps like a chewed tape, with a twitch on every step
    static Vector2 Squash(Cut c, float bt)
    {
        float p = Mathf.Clamp01((bt - c.start) / Mathf.Max(0.01f, c.end - c.start));
        int step = Mathf.FloorToInt(p * 9f);
        float q = Mathf.SmoothStep(0f, 1f, step / 8f);
        Vector2 s = Vector2.Lerp(c.s0, c.s1, q);
        float j = Mathf.Sin(step * 12.9898f + c.start * 78.233f) * 43758.5453f; j -= Mathf.Floor(j);
        s.x *= 0.9f + 0.2f * j; s.y *= 1.1f - 0.2f * j;
        return s;
    }

    void Burst(float w, float h, float u, float bt)
    {
        GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
        if (cuts.Count == 0) return;
        Cut c = cuts[cuts.Count - 1];
        for (int i = 0; i < cuts.Count; i++) if (bt < cuts[i].end) { c = cuts[i]; break; }
        if (c.kind == 1 || c.kind == 3)
        {
            var tex = deathFlashes[Mathf.Clamp(c.tex, 0, deathFlashes.Length - 1)];
            if (tex)
            {
                float fh = Mathf.Max(h * c.zoom, w * tex.height / tex.width), fw = fh * tex.width / tex.height;
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect((w - fw) / 2f + c.off.x * w, -(fh - h) * 0.12f + c.off.y * h * 0.4f, fw, fh), tex);   // keep the head in frame
            }
        }
        if (sType == null)
        {
            sType = new GUIStyle(GUI.skin.label) { font = typeFont, wordWrap = true, alignment = TextAnchor.UpperLeft, clipping = TextClipping.Overflow };
            sLine = new GUIStyle(GUI.skin.label) { font = typeFont, wordWrap = false, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
        }
        if (c.kind == 2 || c.kind == 3 || c.kind == 4)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in DeathText) sb.Append(Loc.T(l)).Append('\n');
            string text = sb.ToString();
            sType.fontSize = Mathf.RoundToInt(32 * u);
            var m = GUI.matrix;
            if (c.kind == 4)
            {
                // the text stretched tall, breathing in and out like a tape being chewed
                float k = bt - c.start; int step = Mathf.FloorToInt(k * 11f);
                float sy = 2.7f + Mathf.Sin(step * 0.9f) * 0.7f, sx = 1.15f - Mathf.Sin(step * 0.9f) * 0.25f;
                GUIUtility.ScaleAroundPivot(new Vector2(sx, sy), new Vector2(0f, 0f));
                GUI.color = Color.white;
                GUI.Label(new Rect(w * 0.02f / sx, h * 0.02f, w * 0.9f, h), text, sType);
            }
            else
            {
                Vector2 s = c.kind == 2 ? Squash(c, bt) : Vector2.one;
                Rect r = new Rect(w * 0.06f + c.off.x * 60f * u, h * 0.08f, w * 0.62f, h * 0.8f);
                GUIUtility.ScaleAroundPivot(s, new Vector2(r.x, h * 0.25f));
                GUI.color = Color.white;
                GUI.Label(r, text, sType);
            }
            GUI.matrix = m;
        }
        else if (c.kind == 5)
        {
            // one line, huge, pulled wide then crushed thin
            Vector2 s = Squash(c, bt);
            sLine.fontSize = Mathf.RoundToInt(58 * u);
            var m = GUI.matrix;
            GUIUtility.ScaleAroundPivot(s, new Vector2(w / 2f, h / 2f));
            GUI.color = Color.white;
            GUI.Label(new Rect(-w, h / 2f - 40f * u, w * 3f, 80f * u), Loc.T(DeathText[Mathf.Clamp(c.line, 0, DeathText.Length - 1)]), sLine);
            GUI.matrix = m;
        }
        // tracking lines + grain
        GUI.color = new Color(0f, 0f, 0f, 0.3f);
        for (float y = 0; y < h; y += 4f * u) GUI.DrawTexture(new Rect(0, y, w, 1.5f * u), Texture2D.whiteTexture);
        if (c.kind != 0) Noise(w, h, 0.07f);
    }

    void Broadcast(float w, float h, float u, string title, string sub)
    {
        float now = Time.unscaledTime;
        if (bcFor != title) { bcFor = title; bcStart = now; if (HasBurst) BuildBurst(); }
        float t = now - bcStart;

        // the channel drops out: a burst of white static
        if (t < 0.32f)
        {
            GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            Noise(w, h, 0.85f);
            return;
        }
        if (HasBurst && t < 0.32f + BurstTime) { Burst(w, h, u, t - 0.32f); return; }
        // after it: dead air until the tape reloads
        GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
        Noise(w, h, 0.04f);
    }

    void Noise(float w, float h, float alpha)
    {
        if (Time.unscaledTime > nextNoise)
        {
            nextNoise = Time.unscaledTime + 0.05f;
            var px = noise.GetPixels32();
            for (int i = 0; i < px.Length; i++) { byte b = (byte)Random.Range(0, 256); px[i] = new Color32(b, b, b, 255); }
            noise.SetPixels32(px); noise.Apply(false);
        }
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(new Rect(0, 0, w, h), noise);
    }

    static void Shadowed(Rect r, string s, GUIStyle st, Color c)
    {
        s = Loc.T(s);
        GUI.color = new Color(0f, 0f, 0f, c.a * 0.85f);
        GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), s, st);
        GUI.color = c;
        GUI.Label(r, s, st);
    }
}
