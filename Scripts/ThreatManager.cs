using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Night director. Runs the four threats from the clock and the night number:
//   door     - a voice outside, then the door creaks open wider and wider -> slam it shut
//   window   - a shadow behind the glass, voices of loved ones -> close the curtains
//                (closed curtains get torn open again after a while)
//   stairs   - footsteps coming down -> run to the stairs and hold the flashlight on them
//   intruder - the Intruder appears on the TV -> yank the plug (it gets plugged back in later)
// Everything is signalled by sound, light and shadow. The wardrobe saves you from a threat that
// would have got you, but you can only stay inside for a short time.
public class ThreatManager : MonoBehaviour
{
    public PanicPlayer player;
    public NightTimer timer;
    public TVSet tv;

    [Header("Places")]
    public Transform doorPoint;
    public Transform windowPoint;
    public Renderer windowShadow;
    public Light windowBacklight;     // lights the yard behind the figure so it reads as a silhouette
    public Vector3 stairsTop = new Vector3(-1.1f, 3.1f, 4.9f);
    public Vector3 stairsBottom = new Vector3(-1.1f, 0.6f, 1.9f);
    public Light stairsLight;

    [Header("Voices")]
    public AudioClip[] doorVoices;
    public AudioClip[] windowVoices;
    public AudioClip[] whispers;
    public AudioClip phoneCall;
    [Tooltip("Footsteps of the thing on the stairs (falls back to generated steps).")]
    public AudioClip[] stairSteps;
    [Tooltip("The Intruder's voice from the TV when his face comes on.")]
    public AudioClip intruderVoice;
    [Tooltip("Emergency alert header bursts + attention tone for the death screen.")]
    public AudioClip alertTone;

    [Header("Bodies")]
    public StairsStalker stalker;     // the hooded one walking down the stairs

    [Header("Difficulty")]
    [Range(0f, 1f)] public float baseAggression = 0.15f;
    public float perNight = 0.12f;
    public float perHour = 0.07f;
    public bool debugNoDeath;

    public bool Over => over;
    public int ActiveThreats => ActiveCount;
    public bool IntruderShowing => intruder.state == State.Active;
    public void MadDeath() { if (!over) StartCoroutine(Death(player.cam.transform.position + player.cam.transform.forward, "mad")); }

    public float Aggression => Mathf.Clamp01(baseAggression + perNight * (timer.night - 1) + perHour * timer.Hour);

    enum State { Idle, Warning, Active }
    class Threat { public State state; public float timer, cooldown, progress, lit; public int stage; public bool voiced; }
    readonly Threat door = new Threat(), window = new Threat(), stairs = new Threat(), intruder = new Threat();

    float curtainsClosedFor, replugTimer, whisperTimer, stepTimer, shadowAlpha, stairsLightBase = 1f;
    bool over;
    Material shadowMat; Vector3 shadowHome; float backlightBase;
    AudioClip[] steps; AudioClip sting, thud, ring;
    float fade; Color fadeColor = Color.black; string endText, endSub; bool endDead; float intruderVoiceTimer;
    GUIStyle endStyle;

    void Start()
    {
        if (!player) player = FindFirstObjectByType<PanicPlayer>();
        if (!timer) timer = FindFirstObjectByType<NightTimer>();
        if (!tv) tv = FindFirstObjectByType<TVSet>();
        if (stairSteps != null && stairSteps.Length > 0) steps = stairSteps;
        else
        {
            steps = new AudioClip[4];
            for (int i = 0; i < steps.Length; i++) steps[i] = SfxBank.Step(300 + i);
        }
        thud = SfxBank.Thud();
        sting = MakeSting();
        ring = MakeRing();
        door.cooldown = Random.Range(25f, 40f);
        window.cooldown = Random.Range(15f, 30f);
        stairs.cooldown = Random.Range(45f, 70f);
        intruder.cooldown = Random.Range(35f, 55f);
        whisperTimer = Random.Range(40f, 80f);
        replugTimer = Random.Range(35f, 60f);
        if (windowShadow) { shadowMat = windowShadow.material; shadowHome = windowShadow.transform.position; SetShadow(0f); }
        if (stairsLight) stairsLightBase = stairsLight.intensity;
        if (windowBacklight) { backlightBase = windowBacklight.intensity; windowBacklight.intensity = 0f; }
        if (timer) timer.OnNightEnd += () => StartCoroutine(Survived());
        if (stalker) stalker.OnStep = (p, loud) => PlayAt(steps[Random.Range(0, steps.Length)], p + Vector3.up * 0.1f, loud, Random.Range(0.72f, 0.8f));
        if (phoneCall && timer && timer.night == 1) StartCoroutine(Phone());
    }

    public void KillBy(Vector3 at, string cause) { if (!over && !debugNoDeath) StartCoroutine(Death(at, cause)); }

    int ActiveCount => (door.state != State.Idle ? 1 : 0) + (window.state != State.Idle ? 1 : 0) + (stairs.state != State.Idle ? 1 : 0) + (intruder.state != State.Idle ? 1 : 0);
    int MaxActive => 1 + Mathf.FloorToInt(Aggression * 2.5f);
    float Cooldown() => Random.Range(Mathf.Lerp(40f, 12f, Aggression), Mathf.Lerp(65f, 25f, Aggression));
    float KillTime(float easy, float hard) => Mathf.Lerp(easy, hard, Aggression);

    void Update()
    {
        if (over || timer == null || !timer.Running || player == null) return;
        float dt = Time.deltaTime;
        DebugKeys();
        UpdateDoor(dt);
        UpdateWindow(dt);
        UpdateStairs(dt);
        UpdateIntruder(dt);
        UpdateAmbient(dt);
    }

    // ---------------------------------------------------------------- door
    void UpdateDoor(float dt)
    {
        var t = door;
        switch (t.state)
        {
            case State.Idle:
                if ((t.cooldown -= dt * (player.DoorAngle > 5f ? 3f : 1f)) > 0f) break;
                if (ActiveCount >= MaxActive) { t.cooldown = 3f; break; }
                t.state = State.Warning; t.timer = Random.Range(3f, 5f);
                Voice(Pick(doorVoices), doorPoint.position, 1f, 1600f);
                StartCoroutine(Knock());
                break;
            case State.Warning:
                if ((t.timer -= dt) > 0f) break;
                t.state = State.Active; t.timer = 0f; t.stage = 0;
                player.CreakDoorTo(14f);
                break;
            case State.Active:
                t.timer += dt;
                if (t.timer > 0.6f && player.DoorAngle < 1f) { Thump(doorPoint.position); Reset(t); break; }
                float kt = KillTime(16f, 8f);
                int stage = t.timer > kt * 0.66f ? 2 : t.timer > kt * 0.33f ? 1 : 0;
                if (stage > t.stage)
                {
                    t.stage = stage;
                    player.CreakDoorTo(stage == 1 ? 32f : 62f, 1.3f);
                }
                if (t.timer > kt) Kill(t, doorPoint.position + Vector3.up * 0.1f, "door");
                break;
        }
    }

    // ---------------------------------------------------------------- window
    void UpdateWindow(float dt)
    {
        var t = window;
        bool closed = player.CurtainsClosed;
        curtainsClosedFor = closed ? curtainsClosedFor + dt : 0f;
        if (closed && t.state == State.Idle && curtainsClosedFor > Mathf.Lerp(60f, 25f, Aggression))
        {
            player.ForceOpenCurtains();
            curtainsClosedFor = 0f;
            t.cooldown = Mathf.Min(t.cooldown, Random.Range(3f, 8f));
        }
        switch (t.state)
        {
            case State.Idle:
                if (closed) break;
                if ((t.cooldown -= dt) > 0f) break;
                if (ActiveCount >= MaxActive) { t.cooldown = 3f; break; }
                t.state = State.Active; t.timer = 0f; t.voiced = false;
                Voice(Pick(windowVoices), windowPoint.position, 1f, 2600f);
                break;
            case State.Active:
                t.timer += dt;
                if (closed) { Thump(windowPoint.position); Reset(t); break; }
                float kt = KillTime(15f, 7f);
                if (!t.voiced && t.timer > kt * 0.5f) { t.voiced = true; Voice(Pick(windowVoices), windowPoint.position, 1f, 3200f); }
                if (t.timer > kt) Kill(t, windowPoint.position, "window");
                break;
        }
        bool show = t.state == State.Active && !closed;
        shadowAlpha = Mathf.MoveTowards(shadowAlpha, show ? 0.93f : 0f, dt * (show ? 0.5f : 3f));
        SetShadow(shadowAlpha);
        if (windowShadow && shadowAlpha > 0f)
            windowShadow.transform.position = shadowHome + new Vector3(Mathf.Sin(Time.time * 0.6f) * 0.06f, Mathf.Sin(Time.time * 0.37f) * 0.015f, 0f);
    }

    void SetShadow(float a)
    {
        if (!shadowMat) return;
        shadowMat.SetColor("_BaseColor", new Color(0f, 0f, 0f, a));
        windowShadow.enabled = a > 0.001f;
        if (windowBacklight) { windowBacklight.intensity = backlightBase * a; windowBacklight.enabled = a > 0.001f; }
    }

    // ---------------------------------------------------------------- stairs
    void UpdateStairs(float dt)
    {
        var t = stairs;
        switch (t.state)
        {
            case State.Idle:
                if (stairsLight) stairsLight.intensity = stairsLightBase;
                if ((t.cooldown -= dt) > 0f) break;
                if (ActiveCount >= MaxActive) { t.cooldown = 3f; break; }
                t.state = State.Active; t.progress = 0f; t.lit = 0f; stepTimer = 0.5f;
                Voice(Pick(whispers), stairsTop, 0.7f, 5000f);
                if (stalker) stalker.Begin();
                break;
            case State.Active:
                Vector3 pos = stalker && stalker.Active ? stalker.Chest : Vector3.Lerp(stairsTop, stairsBottom, t.progress);
                bool lit = player.AtStairs && player.FlashShining && Looking(pos, 0.8f);
                if (stalker) stalker.Tick(t.progress, lit);
                if (lit)
                {
                    t.lit += dt;
                    if (t.lit > 1.1f) { StartCoroutine(Retreat(pos)); if (stalker) stalker.Retreat(); Reset(t); break; }
                }
                else
                {
                    t.lit = Mathf.Max(0f, t.lit - dt * 0.5f);
                    t.progress += dt / KillTime(20f, 10f);
                    if (!(stalker && stalker.Active) && (stepTimer -= dt) <= 0f)
                    {
                        stepTimer = Mathf.Lerp(1.5f, 0.75f, t.progress);
                        PlayAt(steps[Random.Range(0, steps.Length)], pos, Mathf.Lerp(0.4f, 1f, t.progress), 0.75f);
                    }
                }
                if (stairsLight)
                    stairsLight.intensity = stairsLightBase * (Mathf.PerlinNoise(Time.time * 9f, 2.7f) < 0.45f ? 0.08f : 1f);
                if (t.progress >= 1f) Kill(t, stairsBottom + Vector3.up * 0.9f, "stairs");
                break;
        }
    }

    IEnumerator Retreat(Vector3 from)
    {
        for (int i = 0; i < 7; i++)
        {
            Vector3 p = Vector3.Lerp(from, stairsTop + Vector3.up * 0.5f, i / 6f);
            PlayAt(steps[Random.Range(0, steps.Length)], p, 0.8f - i * 0.09f, 0.85f);
            yield return new WaitForSeconds(0.38f);
        }
    }

    // ---------------------------------------------------------------- TV intruder
    void UpdateIntruder(float dt)
    {
        var t = intruder;
        if (!tv) return;
        switch (t.state)
        {
            case State.Idle:
                if (!tv.pluggedIn)
                {
                    // something plugs it back in when you are not looking
                    if ((replugTimer -= dt) <= 0f && !player.AtTV)
                    {
                        StartCoroutine(tv.InsertPlug());
                        replugTimer = Random.Range(35f, 60f);
                        t.cooldown = Mathf.Max(t.cooldown, Random.Range(8f, 15f));
                    }
                    break;
                }
                if ((t.cooldown -= dt) > 0f) break;
                if (ActiveCount >= MaxActive) { t.cooldown = 3f; break; }
                t.state = State.Warning; t.timer = Random.Range(2.5f, 4f);
                tv.staticVolume = 0.6f;
                break;
            case State.Warning:
                if (!tv.pluggedIn) { Reset(t); break; }
                if ((t.timer -= dt) > 0f) break;
                tv.intruderShowing = true;
                t.state = State.Active; t.timer = 0f;
                intruderVoiceTimer = 0.4f;
                break;
            case State.Active:
                t.timer += dt;
                if (!tv.pluggedIn || !tv.intruderShowing) { Reset(t); replugTimer = Random.Range(35f, 60f); break; }
                if (intruderVoice && (intruderVoiceTimer -= dt) <= 0f)
                {
                    intruderVoiceTimer = intruderVoice.length + Random.Range(5f, 9f);
                    var v = Voice(intruderVoice, tv.transform.position + Vector3.up * 0.3f, 1f, 6500f);
                    if (v) { v.pitch = Random.Range(0.94f, 1f); v.minDistance = 2.5f; }
                }
                // no direct kill: the Intruder eats sanity instead (SanitySystem)
                break;
        }
        if (t.state == State.Idle && tv.staticVolume > 0.3f) tv.staticVolume = Mathf.MoveTowards(tv.staticVolume, 0.3f, dt * 0.2f);
    }

    // ---------------------------------------------------------------- ambience
    void UpdateAmbient(float dt)
    {
        if (Aggression < 0.3f || whispers == null || whispers.Length == 0) return;
        if ((whisperTimer -= dt) > 0f) return;
        whisperTimer = Random.Range(Mathf.Lerp(90f, 35f, Aggression), Mathf.Lerp(140f, 60f, Aggression));
        Vector3 around = player.transform.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(1.5f, 3f);
        Voice(Pick(whispers), around, 0.3f, 7000f);
    }

    IEnumerator Phone()
    {
        yield return new WaitForSeconds(6f);
        for (int i = 0; i < 2 && !over; i++) { PlayAt(ring, doorPoint.position + new Vector3(1.2f, 0f, 1.6f), 0.45f, 1f); yield return new WaitForSeconds(3f); }
        if (over) yield break;
        var a = Voice(phoneCall, player.transform.position, 0.55f, 3200f);
        if (a) { a.spatialBlend = 0f; a.gameObject.AddComponent<AudioHighPassFilter>().cutoffFrequency = 450f; }
    }

    IEnumerator Knock()
    {
        for (int i = 0; i < 3; i++) { PlayAt(thud, doorPoint.position, 0.6f, 1.3f); yield return new WaitForSeconds(0.32f); }
    }

    // ---------------------------------------------------------------- outcomes
    void Reset(Threat t) { if (t == stairs && stalker && stalker.Active) stalker.Retreat(); t.state = State.Idle; t.cooldown = Cooldown(); t.timer = 0f; t.stage = 0; t.progress = 0f; t.lit = 0f; t.voiced = false; }

    void Kill(Threat t, Vector3 at, string cause)
    {
        if (debugNoDeath) { Reset(t); return; }
        if (stalker && t == stairs) { if (player.IsHidden) stalker.Retreat(); else stalker.Release(); }
        if (player.IsHidden)
        {
            // it came in, looked around, and left
            Voice(Pick(whispers), player.transform.position + player.transform.right * 0.6f, 0.7f, 6000f);
            Reset(t);
            return;
        }
        StartCoroutine(Death(at, cause));
    }

    IEnumerator Death(Vector3 at, string cause)
    {
        over = true;
        timer.Pause();
        var seq = GetComponent<DeathSequence>();
        if (seq) yield return seq.Run(at, cause, sting);
        else
        {
            player.Freeze(at);
            var src = gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 0f; src.PlayOneShot(sting, 1f);
            yield return new WaitForSeconds(0.85f);
        }
        fadeColor = Color.black;
        for (float f = 0f; f < 1f; f += Time.deltaTime / 0.6f) { fade = f; yield return null; }
        fade = 1f;
        AudioListener.volume = 0.6f * GameSettings.Volume;
        endDead = true;
        endText = cause == "door" ? "AN ALTERNATE HAS ENTERED YOUR HOME" : cause == "window" ? "YOU HAVE BEEN SEEN" : cause == "stairs" ? "IT WAS ALREADY INSIDE" : cause == "mad" ? "THANK YOU FOR YOUR COOPERATION" : "HE IS INSIDE YOUR HOME";
        endSub = cause == "door" ? "You let them in." : cause == "window" ? "It saw you through the window." : cause == "stairs" ? "It came down the stairs." : cause == "mad" ? "You did what the signal told you." : "He is already inside your home.";
        GameHUD.EndInfo = "NIGHT " + timer.night + "   ·   " + timer.TimeText;
        if (alertTone && !GameHUD.BurstOn) { var a = gameObject.AddComponent<AudioSource>(); a.spatialBlend = 0f; a.ignoreListenerVolume = true; a.volume = 0.5f; a.clip = alertTone; a.PlayDelayed(0.3f); }
        for (float w = 0f; w < GameHUD.DeathHold; w += Time.unscaledDeltaTime)
        {
            var m = Mouse.current;
            if (w > 5f && m != null && m.leftButton.wasPressedThisFrame) break;
            yield return null;
        }
        AudioListener.volume = GameSettings.Volume;
        NightTimer.carryNight = timer.night;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    IEnumerator Survived()
    {
        over = true;
        Reset(door); Reset(window); Reset(stairs); Reset(intruder);
        if (tv) tv.intruderShowing = false;
        SetShadow(0f);
        player.Toast("6 AM", 6f);
        yield return new WaitForSeconds(2.5f);
        fadeColor = new Color(0.85f, 0.82f, 0.75f);
        for (float f = 0f; f < 1f; f += Time.deltaTime / 2.5f) { fade = f; yield return null; }
        fade = 1f;
        endDead = false;
        endText = "Night " + timer.night + " survived.";
        endSub = "It will get worse.";
        yield return new WaitForSeconds(4f);
        NightTimer.carryNight = timer.night + 1; NightTimer.carryDay = timer.night;   // the day comes first
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void OnGUI()
    {
        if (fade <= 0f) return;
        GUI.depth = -10;
        GameHUD.DrawEnd(fade, fadeColor, endText, endSub, endDead);
    }

    // ---------------------------------------------------------------- helpers
    bool Looking(Vector3 p, float minDot)
    {
        var c = player.cam.transform;
        return Vector3.Dot(c.forward, (p - c.position).normalized) > minDot;
    }

    static AudioClip Pick(AudioClip[] list) => list != null && list.Length > 0 ? list[Random.Range(0, list.Length)] : null;

    AudioSource Voice(AudioClip c, Vector3 pos, float vol, float cutoff)
    {
        if (!c) return null;
        var go = new GameObject("Voice_" + c.name);
        go.transform.position = pos;
        var a = go.AddComponent<AudioSource>();
        a.clip = c; a.spatialBlend = 1f; a.minDistance = 1.5f; a.maxDistance = 25f; a.dopplerLevel = 0f;
        a.volume = vol; a.pitch = Random.Range(0.92f, 1f);
        go.AddComponent<AudioLowPassFilter>().cutoffFrequency = cutoff;
        a.Play();
        Destroy(go, c.length / a.pitch + 0.3f);
        return a;
    }

    void PlayAt(AudioClip c, Vector3 pos, float vol, float pitch)
    {
        if (!c) return;
        var go = new GameObject("Sfx_" + c.name);
        go.transform.position = pos;
        var a = go.AddComponent<AudioSource>();
        a.clip = c; a.spatialBlend = 1f; a.minDistance = 1.2f; a.maxDistance = 25f; a.volume = vol; a.pitch = pitch; a.dopplerLevel = 0f;
        a.Play();
        Destroy(go, c.length / pitch + 0.2f);
    }

    void Thump(Vector3 p) => PlayAt(thud, p, 0.9f, 0.8f);

    // test hooks (also used by the F3-F7 debug keys)
    public void Trigger(string which)
    {
        if (which == "door") door.cooldown = 0f;
        else if (which == "window") window.cooldown = 0f;
        else if (which == "stairs") stairs.cooldown = 0f;
        else if (which == "tv") intruder.cooldown = 0f;
    }
    public string Status() =>
        $"door {door.state} {door.timer:0.0}s st{door.stage} | window {window.state} {window.timer:0.0}s | stairs {stairs.state} {stairs.progress:0.00} lit {stairs.lit:0.0} | tv {intruder.state} {intruder.timer:0.0}s | aggr {Aggression:0.00} max {MaxActive} | {timer.ClockText} over {over}";

    void DebugKeys()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.f3Key.wasPressedThisFrame) door.cooldown = 0f;
        if (kb.f4Key.wasPressedThisFrame) window.cooldown = 0f;
        if (kb.f5Key.wasPressedThisFrame) stairs.cooldown = 0f;
        if (kb.f6Key.wasPressedThisFrame) intruder.cooldown = 0f;
        if (kb.f7Key.wasPressedThisFrame) timer.SkipHour();
    }

    // jumpscare: detuned saw screech over a noise burst, hard-clipped
    static AudioClip MakeSting()
    {
        const int sr = 44100; float len = 1.4f;
        var d = new float[(int)(sr * len)];
        float ph1 = 0f, ph2 = 0f, ph3 = 0f;
        var rnd = new System.Random(66);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)sr;
            float f = Mathf.Lerp(900f, 420f, t / len) * (1f + 0.05f * Mathf.Sin(t * 70f));
            ph1 += f / sr; ph2 += f * 1.507f / sr; ph3 += f * 0.497f / sr;
            float saw = (ph1 % 1f) * 2f - 1f + ((ph2 % 1f) * 2f - 1f) * 0.7f + ((ph3 % 1f) * 2f - 1f) * 0.8f;
            float noise = (float)rnd.NextDouble() * 2f - 1f;
            float env = Mathf.Min(1f, t * 60f) * Mathf.Exp(-t * 1.6f);
            float v = (saw * 0.6f + noise * 0.5f) * env * 1.8f;
            d[i] = Mathf.Clamp(v, -0.9f, 0.9f);
        }
        var c = AudioClip.Create("Sting", d.Length, 1, sr, false);
        c.SetData(d, 0);
        return c;
    }

    // old landline ring burst
    static AudioClip MakeRing()
    {
        const int sr = 44100; float len = 2f;
        var d = new float[(int)(sr * len)];
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)sr;
            float gate = (t % 0.1f) < 0.05f ? 1f : 0.3f;
            float v = (Mathf.Sin(2f * Mathf.PI * 440f * t) + Mathf.Sin(2f * Mathf.PI * 480f * t)) * 0.3f * gate;
            d[i] = v * Mathf.Min(1f, t * 50f) * Mathf.Min(1f, (len - t) * 20f);
        }
        var c = AudioClip.Create("Ring", d.Length, 1, sr, false);
        c.SetData(d, 0);
        return c;
    }
}
