using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// The opening, before night 1 (only after NEW TAPE):
//   1. a Mandela County public service announcement on tape (skippable: hold SPACE)
//   2. Friday night: Mark parks in the garage, the roll-up door comes down, engine off
//   3. inside: nobody home. Lynn's note on the fridge ("went to Mom's") - the handwriting is off
//   4. the TV switches itself on: Emergency Alert System. Look at it -> midnight -> night 1
public class IntroDirector : MonoBehaviour
{
    public static IntroDirector I;
    public static bool Pending;          // set by the menu for a new game

    public DayPlayer player;
    public NightRouter router;
    public TVSet tv;
    public GarageGate gate;
    public Light[] headlights;
    public float headlightIntensity = 1.6f;   // the gate is right in front of the bumper
    public GameObject[] carLights;       // garage bulb etc. (on only while we sit in the car)
    public GameObject[] houseLights;     // lamps that are on when Mark comes home
    public DayItem tvItem;
    public Transform kitchenDoor;         // open while Mark walks around
    public Vector3 kitchenDoorOpen = new Vector3(0f, 95f, 0f);

    [Header("Car")]
    public Vector3 carEye = new Vector3(-5.38f, 1.21f, -4.5f);
    public Vector3 carLook = new Vector3(-5.2f, 1.0f, -1.0f);
    [Header("House")]
    public Vector3 houseEye = new Vector3(-1.15f, 1.67f, -0.92f);
    public Vector3 houseLook = new Vector3(1.0f, 1.4f, -1.2f);

    [Header("Pictures")]
    public Texture2D note;               // Lynn's note, shown full screen when read
    public Texture2D easScreen;          // what the TV shows

    [Header("Sound")]
    public AudioClip[] psaVoices;        // the announcer
    public AudioClip engineLoop, engineOff, carDoor, gateMotor, houseDoor, easTone;

    enum Phase { Psa, Car, House, Eas, Out }
    Phase phase = Phase.Psa;
    float fade = 1f; Color fadeCol = Color.black;
    string sub; float subUntil; string goal = ""; float goalAt;
    bool noteOpen, tvOn, prompt; float promptA;
    float psaT, skipHold; int slide = -1;
    AudioSource voice, engine, ui; AudioClip bars;
    Texture2D noise; float nextNoise;
    float yaw, pitch;
    bool[] saved;
    bool advance;
    /// testing / scripted: acts as a click on the current prompt
    public void Advance() { advance = true; }
    Texture2D barsTex;
    GUIStyle sBig, sMid, sSmall, sSub, sOsd, sLabel;

    static readonly string[] Slides =
    {
        "A MESSAGE FROM THE MANDELA COUNTY\nDEPARTMENT OF PUBLIC SAFETY",
        "If someone you know comes to your door after dark,\ndo NOT let them in.",
        "They may look like your family.\nThey may sound like your family.\nThey may know things only your family would know.",
        "Do not look at them.\nDo not speak to them.\nStay indoors until sunrise.",
        "If you hear the emergency signal,\ncover your ears.",
        "They are not who they say they are.",
    };
    const float SlideTime = 5.2f;

    void OnEnable() { I = this; }

    void Start()
    {
        Pending = false;
        if (!player) player = FindFirstObjectByType<DayPlayer>(FindObjectsInactive.Include);
        if (tv) { tv.enabled = true; tv.pluggedIn = false; }
        voice = gameObject.AddComponent<AudioSource>(); voice.spatialBlend = 0f; voice.playOnAwake = false;
        engine = gameObject.AddComponent<AudioSource>(); engine.loop = true; engine.spatialBlend = 0f; engine.playOnAwake = false;
        ui = gameObject.AddComponent<AudioSource>(); ui.spatialBlend = 0f; ui.playOnAwake = false;
        noise = new Texture2D(96, 54, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        barsTex = MakeBars();
        bars = MakeTone();
        foreach (var g in carLights) if (g) g.SetActive(false);
        foreach (var g in houseLights) if (g) g.SetActive(false);
        if (tvItem) tvItem.gameObject.SetActive(false);
        if (kitchenDoor) kitchenDoor.localEulerAngles = kitchenDoorOpen;
        // walking needs the day's invisible walls / upstairs floor, but none of the day's mess
        if (router && router.dayRoot)
        {
            router.dayRoot.SetActive(true);
            foreach (Transform c in router.dayRoot.transform) c.gameObject.SetActive(c.name == "DayColliders");
        }
        if (player) player.enabled = false;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        StartCoroutine(Run());
    }

    // ------------------------------------------------------------------ flow
    IEnumerator Run()
    {
        // 1. the tape
        phase = Phase.Psa;
        ui.clip = bars; ui.volume = 0.12f; ui.loop = true; ui.Play();
        for (float t = 0; t < 2.6f && !Skip(); t += Time.deltaTime) { psaT += Time.deltaTime; yield return null; }
        ui.Stop(); ui.loop = false;
        int v = 0;
        for (slide = 0; slide < Slides.Length; slide++)
        {
            if (Skip()) break;
            if ((slide == 1 || slide == 3) && psaVoices != null && v < psaVoices.Length && psaVoices[v]) { voice.clip = psaVoices[v++]; voice.volume = 0.75f; voice.Play(); }
            for (float t = 0; t < SlideTime && !Skip(); t += Time.deltaTime) { psaT += Time.deltaTime; yield return null; }
        }
        voice.Stop();
        slide = 99;
        yield return Fade(1f, 0.3f);

        // 2. the car
        phase = Phase.Car;
        foreach (var g in carLights) if (g) g.SetActive(true);
        if (router && router.notNight3 != null)
        {
            saved = new bool[router.notNight3.Length];
            for (int i = 0; i < saved.Length; i++) { var x = router.notNight3[i]; if (x) { saved[i] = x.enabled; x.enabled = false; } }
        }
        foreach (var l in headlights) if (l) { l.enabled = true; l.intensity = headlightIntensity; }
        if (gate) gate.open = 1f;
        Look(carEye, carLook);
        if (engineLoop) { engine.clip = engineLoop; engine.volume = 0.45f; engine.Play(); }
        SetGoal("");
        yield return Fade(0f, 2f);
        Say("FRI  9:47 PM", 0f);
        if (gateMotor) { voice.clip = gateMotor; voice.volume = 0.5f; voice.time = 0f; voice.Play(); }
        for (float t = 0; t < 5.5f; t += Time.deltaTime) { if (gate) gate.open = 1f - Mathf.SmoothStep(0f, 1f, t / 5.5f); yield return null; }
        if (gate) gate.open = 0f;
        voice.Stop();
        yield return new WaitForSeconds(0.6f);
        engine.Stop();
        if (engineOff) ui.PlayOneShot(engineOff, 0.6f);
        foreach (var l in headlights) if (l) l.enabled = false;
        yield return new WaitForSeconds(1.2f);
        Say("Home.", 2.5f);
        yield return new WaitForSeconds(2.2f);
        prompt = true;
        var m = Mouse.current;
        advance = false;
        while (!(m != null && m.leftButton.wasPressedThisFrame) && !Kb(Key.Space) && !advance) yield return null;
        prompt = false;
        if (carDoor) ui.PlayOneShot(carDoor, 0.7f);
        yield return Fade(1f, 1.0f);

        // 3. the house
        phase = Phase.House;
        foreach (var g in carLights) if (g) g.SetActive(false);
        if (router && router.notNight3 != null && saved != null)
            for (int i = 0; i < saved.Length; i++) { var x = router.notNight3[i]; if (x) x.enabled = saved[i]; }
        foreach (var g in houseLights) if (g) g.SetActive(true);
        if (houseDoor) ui.PlayOneShot(houseDoor, 0.45f);
        if (player) { player.enabled = true; player.Place(houseEye, houseLook); }
        yield return new WaitForSeconds(0.6f);
        yield return Fade(0f, 1.6f);
        Say("Lynn? I'm home.", 3f);
        yield return new WaitForSeconds(4.2f);
        Say("...Lynn?", 2.5f);
        SetGoal("Find Lynn");
    }

    public void Use(DayItem it)
    {
        if (phase != Phase.House) return;
        if (it.name.Contains("Note")) { StartCoroutine(ReadNote()); return; }
        if (it.name.Contains("TV") && tvOn) { StartCoroutine(Eas()); return; }
        if (it.clip) ui.PlayOneShot(it.clip, it.volume);
        StartCoroutine(Lines(it.lines, it.lineTime));
    }

    IEnumerator Lines(string[] lines, float each)
    {
        if (lines == null) yield break;
        foreach (var l in lines) { Say(l, each); yield return new WaitForSeconds(each); }
    }

    IEnumerator ReadNote()
    {
        if (noteOpen) yield break;
        noteOpen = true; if (player) player.frozen = true;
        yield return null;
        var m = Mouse.current;
        float t0 = Time.time;
        advance = false;
        while (Time.time - t0 < 0.6f || !((m != null && m.leftButton.wasPressedThisFrame) || Kb(Key.Space) || Kb(Key.E) || advance)) yield return null;
        noteOpen = false; if (player) player.frozen = false;
        if (tvOn) yield break;
        SetGoal("");
        Say("Her mother's. She didn't say a word about it this morning.", 3.8f);
        yield return new WaitForSeconds(3.9f);
        Say("And that's not her handwriting. Is it?", 3.4f);
        yield return new WaitForSeconds(4.5f);
        // 4. the TV switches itself on
        tvOn = true;
        if (tv) tv.PlugIn();
        yield return new WaitForSeconds(2.5f);
        if (tv && easScreen) { tv.intruderImage = easScreen; tv.intruderShowing = true; }
        if (easTone && tv) AudioSource.PlayClipAtPoint(easTone, tv.transform.position, 0.8f);
        Say("The TV... I didn't turn that on.", 3.4f);
        SetGoal("The TV");
        if (tvItem) tvItem.gameObject.SetActive(true);
    }

    IEnumerator Eas()
    {
        phase = Phase.Eas;
        if (player) player.frozen = true;
        if (easTone) { voice.clip = easTone; voice.volume = 0.9f; voice.Play(); }
        yield return new WaitForSeconds(9f);
        phase = Phase.Out;
        voice.Stop();
        fadeCol = Color.black; fade = 1f;
        yield return new WaitForSeconds(3.2f);
        NightRouter.InMenu = false;
        NightTimer.carryDay = 0; NightTimer.carryNight = 1;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ------------------------------------------------------------------ helpers
    bool Skip()
    {
        if (phase != Phase.Psa) return false;
        var kb = Keyboard.current;
        bool held = kb != null && (kb.spaceKey.isPressed || kb.enterKey.isPressed);
        skipHold = held ? skipHold + Time.unscaledDeltaTime : 0f;
        return skipHold > 1f;
    }
    static bool Kb(Key k) { var kb = Keyboard.current; return kb != null && kb[k].wasPressedThisFrame; }

    IEnumerator Fade(float to, float time)
    {
        float from = fade;
        for (float t = 0; t < time; t += Time.deltaTime) { fade = Mathf.Lerp(from, to, t / time); yield return null; }
        fade = to;
    }

    void Look(Vector3 eye, Vector3 at)
    {
        var p = player ? player.transform : null; if (!p) return;
        p.position = eye;
        var d = at - eye;
        yaw = Quaternion.LookRotation(new Vector3(d.x, 0f, d.z)).eulerAngles.y;
        pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
        baseYaw = yaw; basePitch = pitch;
        Apply();
    }
    float baseYaw, basePitch;
    void Apply()
    {
        var p = player.transform; p.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (player.cam) { player.cam.transform.localPosition = Vector3.zero; player.cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f); }
    }

    void Update()
    {
        if (phase == Phase.Car && player)
        {
            // look around a little from the driver's seat
            var m = Mouse.current;
            if (m != null)
            {
                Vector2 d = m.delta.ReadValue() * 0.11f * GameSettings.Sensitivity;
                yaw = Mathf.Clamp(yaw + d.x, baseYaw - 70f, baseYaw + 70f);
                pitch = Mathf.Clamp(pitch - d.y, basePitch - 30f, basePitch + 30f);
            }
            Apply();
        }
        promptA = Mathf.MoveTowards(promptA, prompt ? 1f : 0f, Time.deltaTime * 2f);
    }

    void Say(string s, float time) { sub = s; subUntil = time <= 0f ? Time.time + 4f : Time.time + time; }
    void SetGoal(string g) { goal = g; goalAt = Time.time; }

    // ------------------------------------------------------------------ drawing
    void Styles()
    {
        if (sBig != null) return;
        sBig = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
        sMid = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
        sSmall = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
        sSub = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
        sOsd = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Bold };
        sLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
    }

    static void Shadow(Rect r, string s, GUIStyle st, Color c)
    {
        s = Loc.T(s);
        GUI.color = new Color(0f, 0f, 0f, c.a * 0.8f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, st);
        GUI.color = c; GUI.Label(r, s, st);
    }

    void OnGUI()
    {
        Styles();
        float w = Screen.width, h = Screen.height, u = h / 720f;
        if (phase == Phase.Psa) { Psa(w, h, u); return; }
        if (phase == Phase.Eas || phase == Phase.Out) { EasScreen(w, h, u); return; }

        if (phase == Phase.House && fade < 0.99f && !noteOpen)
        {
            // crosshair + label
            var hv = player ? player.Hover : null;
            GUI.color = new Color(1f, 1f, 1f, hv ? 0.85f : 0.35f);
            float cs = (hv ? 6f : 4f) * u;
            GUI.DrawTexture(new Rect(w / 2 - cs / 2, h / 2 - cs / 2, cs, cs), Texture2D.whiteTexture);
            if (hv) { sLabel.fontSize = Mathf.RoundToInt(19 * u); Shadow(new Rect(0, h / 2 + 16 * u, w, 30 * u), hv.label, sLabel, new Color(0.95f, 0.93f, 0.85f, 0.9f)); }
            sOsd.fontSize = Mathf.RoundToInt(16 * u);
            if (!string.IsNullOrEmpty(goal)) Shadow(new Rect(0f, 26f * u, w - 36f * u, 26f * u), "►  " + Loc.T(goal), sOsd, new Color(0.9f, 0.9f, 0.85f, 0.55f * Mathf.Clamp01((Time.time - goalAt) * 0.7f)));
        }
        if (phase == Phase.Car && promptA > 0.01f)
        {
            sSmall.fontSize = Mathf.RoundToInt(18 * u);
            Shadow(new Rect(0, h - 70 * u, w, 30 * u), "LMB — get out of the car", sSmall, new Color(1f, 1f, 1f, 0.75f * promptA));
        }

        if (noteOpen && note)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.75f); GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            float nh = h * 0.8f, nw = nh * note.width / note.height;
            GUI.color = Color.white; GUI.DrawTexture(new Rect((w - nw) / 2f, h * 0.06f, nw, nh), note);
            if (GameSettings.Language == 1)
            {
                sSub.fontSize = Mathf.RoundToInt(19 * u);
                Shadow(new Rect(w * 0.1f, h * 0.87f, w * 0.8f, 40 * u), "\"Mark - went to Mom's for the weekend. Back Sunday. Don't wait up. - L.\"", sSub, new Color(1f, 0.97f, 0.88f, 1f));
            }
            sSmall.fontSize = Mathf.RoundToInt(15 * u);
            Shadow(new Rect(0, h - 30 * u, w, 24 * u), "LMB — put it back", sSmall, new Color(1f, 1f, 1f, 0.6f));
        }

        if (!string.IsNullOrEmpty(sub) && Time.time < subUntil && !noteOpen && (GameSettings.Subtitles || !sub.StartsWith("\"")))
        {
            bool clock = sub.StartsWith("FRI");
            float sa = Mathf.Clamp01((subUntil - Time.time) * 2f);
            if (clock) { sOsd.fontSize = Mathf.RoundToInt(28 * u); Shadow(new Rect(0f, 22f * u, w - 34f * u, 44f * u), sub, sOsd, new Color(0.95f, 0.95f, 0.9f, 0.8f * sa)); }
            else { sSub.fontSize = Mathf.RoundToInt(22 * u); Shadow(new Rect(w * 0.1f, h - 120f * u, w * 0.8f, 60f * u), sub, sSub, new Color(1f, 0.97f, 0.88f, sa)); }
        }

        if (fade > 0f) { GUI.color = new Color(fadeCol.r, fadeCol.g, fadeCol.b, fade); GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture); }
        GUI.color = Color.white;
    }

    void Psa(float w, float h, float u)
    {
        GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
        if (slide < 0)
        {
            GUI.color = Color.white; GUI.DrawTexture(new Rect(0, 0, w, h), barsTex);
            sBig.fontSize = Mathf.RoundToInt(26 * u);
            GUI.color = new Color(0f, 0f, 0f, 0.7f); GUI.DrawTexture(new Rect(0, h * 0.42f, w, 70 * u), Texture2D.whiteTexture);
            Shadow(new Rect(0, h * 0.42f, w, 70 * u), "MANDELA COUNTY PUBLIC ACCESS", sBig, Color.white);
        }
        else if (slide < Slides.Length)
        {
            GUI.color = new Color(0.03f, 0.05f, 0.22f); GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            sSmall.fontSize = Mathf.RoundToInt(20 * u);
            Shadow(new Rect(0, h * 0.12f, w, 30 * u), "PUBLIC SERVICE ANNOUNCEMENT", sSmall, new Color(0.95f, 0.8f, 0.2f));
            sBig.fontSize = Mathf.RoundToInt((slide == 0 ? 34 : 30) * u);
            Shadow(new Rect(w * 0.1f, h * 0.25f, w * 0.8f, h * 0.5f), Slides[slide], sBig, Color.white);
            sSmall.fontSize = Mathf.RoundToInt(15 * u);
            Shadow(new Rect(0, h * 0.86f, w, 26 * u), "MANDELA COUNTY DEPARTMENT OF PUBLIC SAFETY", sSmall, new Color(0.7f, 0.75f, 0.9f));
        }
        // tape look
        Grain(w, h, slide < 0 ? 0.08f : 0.12f);
        GUI.color = new Color(0f, 0f, 0f, 0.2f);
        for (float yy = 0; yy < h; yy += 4f * u) GUI.DrawTexture(new Rect(0, yy, w, 1.4f * u), Texture2D.whiteTexture);
        float band = (Time.time * 0.11f % 1.3f) * h - 0.15f * h;
        GUI.color = new Color(1f, 1f, 1f, 0.05f); GUI.DrawTexture(new Rect(0, band, w, 24f * u), Texture2D.whiteTexture);
        sOsd.fontSize = Mathf.RoundToInt(24 * u); sOsd.alignment = TextAnchor.UpperLeft;
        Shadow(new Rect(40 * u, 28 * u, 300 * u, 40 * u), "PLAY  ▶", sOsd, new Color(0.92f, 0.95f, 1f, 0.9f));
        sOsd.alignment = TextAnchor.UpperRight;
        sSmall.fontSize = Mathf.RoundToInt(15 * u);
        float sk = Mathf.Clamp01(skipHold);
        Shadow(new Rect(0, h - 40 * u, w - 40 * u, 24 * u), sk > 0f ? Loc.T("Skipping...") : Loc.T("Hold SPACE — skip"), new GUIStyle(sSmall) { alignment = TextAnchor.MiddleRight }, new Color(1f, 1f, 1f, 0.45f + sk * 0.5f));
        if (slide >= 99) { GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture); }
        GUI.color = Color.white;
    }

    void EasScreen(float w, float h, float u)
    {
        if (phase == Phase.Out)
        {
            GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            sBig.fontSize = Mathf.RoundToInt(40 * u);
            Shadow(new Rect(0, h * 0.4f, w, 60 * u), "12:00 AM", sBig, new Color(0.85f, 0.85f, 0.8f));
            GUI.color = Color.white; return;
        }
        GUI.color = new Color(0.05f, 0.1f, 0.5f); GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
        GUI.color = new Color(0.7f, 0.07f, 0.07f); GUI.DrawTexture(new Rect(0, 0, w, h * 0.16f), Texture2D.whiteTexture);
        sBig.fontSize = Mathf.RoundToInt(40 * u);
        Shadow(new Rect(0, 0, w, h * 0.16f), "EMERGENCY ALERT SYSTEM", sBig, Color.white);
        sMid.fontSize = Mathf.RoundToInt(24 * u);
        Shadow(new Rect(w * 0.08f, h * 0.22f, w * 0.84f, h * 0.5f),
            "THE FOLLOWING MESSAGE IS TRANSMITTED AT THE REQUEST OF THE MANDELA COUNTY CIVIL AUTHORITIES.\n\nA CIVIL DANGER WARNING IS IN EFFECT UNTIL 6:00 AM.\n\nREMAIN INDOORS. LOCK ALL DOORS AND WINDOWS. DO NOT OPEN YOUR DOOR TO ANYONE.",
            sMid, Color.white);
        float x = w - ((Time.time * 160f * u) % (w * 3f));
        sBig.fontSize = Mathf.RoundToInt(26 * u); sBig.alignment = TextAnchor.MiddleLeft; sBig.wordWrap = false;
        GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(new Rect(0, h * 0.8f, w, 50 * u), Texture2D.whiteTexture);
        Shadow(new Rect(x, h * 0.8f, w * 3f, 50 * u), "THEY LOOK LIKE PEOPLE YOU KNOW.   DO NOT LET THEM IN.   THIS IS NOT A TEST.", sBig, new Color(1f, 0.9f, 0.3f));
        sBig.alignment = TextAnchor.MiddleCenter; sBig.wordWrap = true;
        Grain(w, h, 0.1f);
        GUI.color = Color.white;
    }

    void Grain(float w, float h, float a)
    {
        if (Time.unscaledTime > nextNoise)
        {
            nextNoise = Time.unscaledTime + 0.05f;
            var px = noise.GetPixels32();
            for (int i = 0; i < px.Length; i++) { byte b = (byte)Random.Range(0, 255); px[i] = new Color32(b, b, b, 255); }
            noise.SetPixels32(px); noise.Apply();
        }
        GUI.color = new Color(1f, 1f, 1f, a); GUI.DrawTexture(new Rect(0, 0, w, h), noise);
    }

    static Texture2D MakeBars()
    {
        var t = new Texture2D(7, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        Color[] top = { new Color(.75f,.75f,.75f), new Color(.75f,.75f,0), new Color(0,.75f,.75f), new Color(0,.75f,0), new Color(.75f,0,.75f), new Color(.75f,0,0), new Color(0,0,.75f) };
        for (int x = 0; x < 7; x++) { t.SetPixel(x, 1, top[x]); t.SetPixel(x, 0, x % 2 == 0 ? new Color(0.08f, 0.08f, 0.1f) : top[6 - x] * 0.6f); }
        t.Apply(); return t;
    }

    static AudioClip MakeTone()
    {
        int rate = 22050, n = rate; var d = new float[n];
        for (int i = 0; i < n; i++) d[i] = Mathf.Sin(2f * Mathf.PI * 1000f * i / rate) * 0.3f;
        var c = AudioClip.Create("bars_tone", n, 1, rate, false); c.SetData(d, 0); return c;
    }
}
