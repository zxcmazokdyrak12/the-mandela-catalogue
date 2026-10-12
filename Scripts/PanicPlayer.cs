using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections;

// First-person panic controller. Mouse only:
//   LMB click on a highlighted spot = action (fires on release)
//   hold RMB = flashlight
//   hold LMB+RMB, the wheel button or Space = cover your ears
public class PanicPlayer : MonoBehaviour
{
    [Header("Scene refs")]
    public Camera cam;
    public Transform armR, armL;
    public Light flashlight;
    public Renderer flashLens;
    public Transform door, wardrobeDoor, curtainL, curtainR;
    public TVSet tv;
    public Hotspot sofaHotspot;
    public Volume postVolume;
    public FPBody body;
    public FPArms arms;           // hand-animated first-person arms (replaces the body)
    public FlashlightBattery battery;

    [Header("Sounds (generated if empty)")]
    public AudioSource sfx;
    public AudioClip slamClip, wardrobeClip, curtainClip, plugClip, landClip, creakClip, heartClip;
    public AudioClip[] stepClips;

    [Header("Points (world space)")]
    public Vector3 restPos = new Vector3(0.78f, 1.62f, 0.0f);
    public Vector3 doorStandPos = new Vector3(0.22f, 1.62f, -2.8f);
    public Vector3 doorLook = new Vector3(0.2f, 1.15f, -3.55f);
    public Vector3 windowStandPos = new Vector3(0.7f, 1.62f, 4.25f);
    public Vector3 windowLook = new Vector3(0.7f, 1.55f, 5.0f);
    public Vector3 stairsStandPos = new Vector3(-0.25f, 1.62f, 1.4f);
    public Vector3 stairsLook = new Vector3(-1.1f, 2.9f, 4.6f);
    public Vector3 tvCrouchPos = new Vector3(-0.72f, 0.86f, 0.42f);
    public Vector3 tvLook = new Vector3(-1.13f, 0.78f, -0.07f);
    public Vector3 plugLeanPos = new Vector3(-1.12f, 0.62f, 0.6f);
    public Vector3 plugLook = new Vector3(-1.57f, 0.3f, 0.75f);
    public Vector3 wardrobeWaypoint = new Vector3(0.62f, 1.62f, 1.25f);
    public Vector3 wardrobeFront = new Vector3(-0.95f, 1.62f, 1.3f);
    public Vector3 wardrobeInside = new Vector3(-1.97f, 1.56f, 1.42f);
    public Vector3 wardrobePeek = new Vector3(-1.6f, 1.5f, 1.52f);

    [Header("Tuning")]
    public float mouseSensitivity = 0.11f;
    public float flashIntensity = 6.5f;
    public float maxHideTime = 10f;
    public bool debugForceFlash;   // editor testing: keep the flashlight raised
    [Range(0, 1)] public float madLevel;   // hook for the M.A.D. meter

    enum Station { Rest, TV, Stairs, Hidden, Busy }
    Station station = Station.Rest;
    float yaw, pitch, baseYaw;
    float shakeAmp, shakeTime, shakeDur;
    bool running; float runPhase; int lastStep;
    Vector3 armRTarget, armLTarget, armROff, armLOff;
    static readonly Vector3 ArmRBase = new Vector3(0.2f, -0.3f, -0.1f), ArmLBase = new Vector3(-0.2f, -0.3f, -0.1f);
    static readonly Vector3 Down = new Vector3(70f, 0f, 0f);
    static readonly Vector3 FlashPose = new Vector3(-8f, 8f, -25f);
    static readonly Vector3 EarR = new Vector3(-56f, -25f, -90f), EarL = new Vector3(-56f, 25f, 90f);
    static readonly Vector3 EarROff = new Vector3(0.2f, -0.26f, 0.05f), EarLOff = new Vector3(-0.2f, -0.26f, 0.05f);
    bool earsCovered, flashForced, flashOn, clickArmed, clicked, beamWasOn;
    float hideTimer, nextBeat, doorAngle, wardAngle, doorSign = 1f, wardSign = 1f, earBlend;
    int doorAnimId;
    Quaternion doorClosed, wardClosed;
    Vector3 curtainClosedScale;
    bool curtainsClosed;
    int plugTugs;
    Hotspot hover;
    AudioLowPassFilter lowpass;
    Vignette vignette; float baseVignette;
    Material lensMat;
    float baseFov;
    string toast; float toastUntil;
    string bodyState = ""; float bodyActionUntil;

    public bool EarsCovered => earsCovered;
    public bool IsHidden => station == Station.Hidden;
    public bool AtTV => station == Station.TV;
    public bool CurtainsClosed => curtainsClosed;
    public float DoorAngle => doorAngle;
    public bool AtStairs => station == Station.Stairs;
    public bool FlashShining => flashlight && flashlight.enabled;
    public bool Frozen => frozen;
    bool frozen;

    void Start()
    {
        transform.position = restPos;
        yaw = 0f; pitch = 4f;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        baseFov = cam.fieldOfView;
        doorClosed = door.rotation;
        wardClosed = wardrobeDoor.rotation;
        doorSign = OpenSign(door, doorClosed, Vector3.forward);
        wardSign = OpenSign(wardrobeDoor, wardClosed, Vector3.right);
        curtainClosedScale = curtainL.localScale;
        SetCurtains(0f);
        armRTarget = armLTarget = Down;
        armR.localPosition = ArmRBase; armL.localPosition = ArmLBase;
        armR.localRotation = armL.localRotation = Quaternion.Euler(Down);
        lowpass = cam.GetComponent<AudioLowPassFilter>();
        if (!lowpass) lowpass = cam.gameObject.AddComponent<AudioLowPassFilter>();
        lowpass.cutoffFrequency = 22000f;
        if (postVolume && postVolume.profile.TryGet(out vignette)) baseVignette = vignette.intensity.value;
        if (flashLens) { lensMat = flashLens.material; lensMat.EnableKeyword("_EMISSION"); }
        if (!sfx) sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false; sfx.spatialBlend = 0f;
        if (!slamClip) slamClip = SfxBank.Slam(1f);
        if (!wardrobeClip) wardrobeClip = SfxBank.Slam(0.55f);
        if (!curtainClip) curtainClip = SfxBank.Curtain();
        if (!plugClip) plugClip = SfxBank.PlugPop();
        if (!landClip) landClip = SfxBank.Thud();
        if (!creakClip) creakClip = SfxBank.Creak(1.6f);
        if (!heartClip) heartClip = SfxBank.Heartbeat();
        if (stepClips == null || stepClips.Length == 0)
        {
            stepClips = new AudioClip[5];
            for (int i = 0; i < stepClips.Length; i++) stepClips[i] = SfxBank.Step(100 + i);
        }
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    // which rotation direction swings a door towards 'into'
    float OpenSign(Transform t, Quaternion closed, Vector3 into)
    {
        var r = t.GetComponent<Renderer>();
        if (!r) r = t.GetComponentInChildren<Renderer>();
        Vector3 c0 = r.bounds.center;
        t.rotation = Quaternion.AngleAxis(25f, Vector3.up) * closed;
        Vector3 c1 = r.bounds.center;
        t.rotation = closed;
        return Vector3.Dot(c1 - c0, into) > 0f ? 1f : -1f;
    }

    void SetDoor(float a) { doorAngle = a; door.rotation = Quaternion.AngleAxis(doorSign * a, Vector3.up) * doorClosed; }
    void SetWard(float a) { wardAngle = a; wardrobeDoor.rotation = Quaternion.AngleAxis(wardSign * a, Vector3.up) * wardClosed; }
    void SetCurtains(float closed01)
    {
        var s = curtainClosedScale; s.x *= Mathf.Lerp(0.28f, 1f, closed01);
        curtainL.localScale = s; curtainR.localScale = s;
    }

    void Update()
    {
        var m = Mouse.current; var kb = Keyboard.current;
        if (m == null || frozen || GameHUD.Blocking || GameMenu.Paused) return;
        if (m.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
        { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; clickArmed = false; return; }

        bool lmb = m.leftButton.isPressed, rmb = m.rightButton.isPressed;
        if (m.leftButton.wasPressedThisFrame) clickArmed = !rmb;
        if (lmb && rmb) clickArmed = false;
        clicked = m.leftButton.wasReleasedThisFrame && clickArmed;
        if (m.leftButton.wasReleasedThisFrame) clickArmed = false;

        bool canAct = station == Station.Rest || station == Station.TV || station == Station.Hidden;
        bool earInput = (lmb && rmb) || m.middleButton.isPressed || (kb != null && kb.spaceKey.isPressed);
        earsCovered = canAct && earInput;
        flashOn = flashForced || debugForceFlash || (rmb && !earsCovered && canAct && station != Station.Hidden);
        float batt = battery ? battery.Tick(flashOn, m.rightButton.wasPressedThisFrame, Time.deltaTime) : 1f;
        flashlight.enabled = flashOn && batt > 0.001f;
        if (flashlight.enabled) flashlight.intensity = flashIntensity * batt * (0.94f + 0.06f * Mathf.PerlinNoise(Time.time * 6f, 1f));
        if (lensMat) lensMat.SetColor("_EmissionColor", flashlight.enabled ? new Color(1f, 0.92f, 0.75f) * 8f * batt : Color.black);
        earBlend = Mathf.MoveTowards(earBlend, earsCovered ? 1f : 0f, Time.deltaTime * 5f);
        lowpass.cutoffFrequency = 22000f * Mathf.Pow(650f / 22000f, earBlend);
        AudioListener.volume = Mathf.Lerp(1f, 0.35f, earBlend) * GameSettings.Volume;
        if (vignette != null) vignette.intensity.value = Mathf.Lerp(baseVignette, 0.6f, earBlend);

        // test triggers until the night director exists
        if (kb != null)
        {
            if (kb.f1Key.wasPressedThisFrame) StartCoroutine(CreakDoor());
            if (kb.f2Key.wasPressedThisFrame && tv && tv.pluggedIn) tv.intruderShowing = !tv.intruderShowing;
        }

        if (canAct)
        {
            Vector2 d = m.delta.ReadValue();
            yaw += d.x * mouseSensitivity * GameSettings.Sensitivity; pitch -= d.y * mouseSensitivity * GameSettings.Sensitivity;
            pitch = Mathf.Clamp(pitch, -60f, 75f);
            if (station == Station.TV) yaw = baseYaw + Mathf.Clamp(Mathf.DeltaAngle(baseYaw, yaw), -165f, 165f);
            if (station == Station.Hidden)
            {
                yaw = baseYaw + Mathf.Clamp(Mathf.DeltaAngle(baseYaw, yaw), -25f, 25f);
                pitch = Mathf.Clamp(pitch, -15f, 25f);
            }
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        hover = null;
        if (station == Station.Rest) hover = Probe(false);
        else if (station == Station.TV)
            hover = Mathf.Abs(Mathf.DeltaAngle(baseYaw, yaw)) > 100f ? sofaHotspot : Probe(true);

        if (station == Station.Hidden)
        {
            hideTimer += Time.deltaTime;
            float stress = Mathf.InverseLerp(maxHideTime * 0.4f, maxHideTime, hideTimer);
            if (Time.time > nextBeat)
            {
                nextBeat = Time.time + Mathf.Lerp(0.9f, 0.42f, stress);
                sfx.PlayOneShot(heartClip, Mathf.Lerp(0.35f, 0.9f, stress));
                if (stress > 0.3f) Shake(0.004f + 0.012f * stress, 0.12f);
            }
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, baseFov - 10f * stress, Time.deltaTime * 3f);
            if (hideTimer > maxHideTime) { Toast("I can't stay in here any longer...", 2.5f); StartCoroutine(ExitWardrobe()); }
            else if (clicked) StartCoroutine(ExitWardrobe());
            return;
        }

        if (clicked && hover != null && (station == Station.Rest || station == Station.TV))
        {
            switch (hover.type)
            {
                case HotspotType.Door: StartCoroutine(DoorAction()); break;
                case HotspotType.Window: StartCoroutine(WindowAction()); break;
                case HotspotType.TV: StartCoroutine(VaultToTV()); break;
                case HotspotType.Stairs: StartCoroutine(StairsAction()); break;
                case HotspotType.Wardrobe: StartCoroutine(HideInWardrobe()); break;
                case HotspotType.Plug: StartCoroutine(PlugAction()); break;
                case HotspotType.SofaReturn: StartCoroutine(VaultBack()); break;
            }
        }
    }

    Hotspot Probe(bool atTV)
    {
        var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        var hits = Physics.RaycastAll(ray, 7f, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            var hs = h.collider.GetComponent<Hotspot>();
            if (hs == null) { if (h.collider.isTrigger) continue; return null; }
            bool ok = atTV ? hs.type == HotspotType.Plug : hs.type != HotspotType.Plug && hs.type != HotspotType.SofaReturn;
            if (ok) return hs;
        }
        return null;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        Vector3 pos = Vector3.zero, rot = Vector3.zero;
        if (running)
        {
            runPhase += dt * 20f;
            int step = (int)(runPhase / (2f * Mathf.PI));
            if (step != lastStep)
            {
                lastStep = step;
                sfx.PlayOneShot(stepClips[Random.Range(0, stepClips.Length)], Random.Range(0.45f, 0.65f));
            }
            float s = Mathf.Sin(runPhase * 0.5f);
            pos += new Vector3(s * 0.025f, Mathf.Abs(s) * 0.055f - 0.03f, 0f);
            rot.z += s * 1.8f;
            rot.x += Mathf.Abs(s) * 1.2f;
        }
        if (station == Station.Rest)   // leaning on the sofa
        {
            rot.z += 3.5f;
            pos += new Vector3(-0.03f, Mathf.Sin(Time.time * 1.6f) * 0.006f, 0f);
        }
        if (station == Station.Hidden) pos.y += Mathf.Sin(Time.time * 2.2f) * 0.008f;
        if (earBlend > 0f)
        {
            pos += new Vector3(0f, -0.06f * earBlend, 0f) + Random.insideUnitSphere * 0.0025f * earBlend;
            rot.x += 7f * earBlend;
        }
        if (shakeTime > 0f)
        {
            shakeTime -= dt;
            float k = shakeAmp * Mathf.Clamp01(shakeTime / shakeDur);
            pos += Random.insideUnitSphere * k;
            rot += Random.insideUnitSphere * k * 120f;
        }
        cam.transform.localPosition = Vector3.Lerp(cam.transform.localPosition, pos, 1f - Mathf.Exp(-25f * dt));
        cam.transform.localRotation = Quaternion.Slerp(cam.transform.localRotation, Quaternion.Euler(rot), 1f - Mathf.Exp(-20f * dt));

        // arms
        Vector3 r = armRTarget, l = armLTarget, ro = armROff, lo = armLOff;
        if (running)
        {
            float s = Mathf.Sin(runPhase * 0.5f);
            r = new Vector3(30f + 40f * s, -6f, -10f); l = new Vector3(30f - 40f * s, 6f, 10f);
        }
        if (flashOn)
        {
            float sway = running ? 6f * Mathf.Sin(runPhase * 0.5f) : 0f;
            l = FlashPose + new Vector3(Mathf.Sin(Time.time * 1.3f) * 0.8f + sway, Mathf.Sin(Time.time * 0.9f) * 0.6f, 0f);
            lo = Vector3.zero;
        }
        if (earsCovered) { r = EarR; l = EarL; ro = EarROff; lo = EarLOff; }
        float k2 = 1f - Mathf.Exp(-(running ? 30f : 16f) * dt);
        armR.localRotation = Quaternion.Slerp(armR.localRotation, Quaternion.Euler(r), k2);
        armL.localRotation = Quaternion.Slerp(armL.localRotation, Quaternion.Euler(l), k2);
        armR.localPosition = Vector3.Lerp(armR.localPosition, ArmRBase + ro, k2);
        armL.localPosition = Vector3.Lerp(armL.localPosition, ArmLBase + lo, k2);

        // the beam lands where you look; the hand just sways around it
        if (flashOn && !body && !arms)
        {
            Vector3 aim = cam.transform.position + cam.transform.forward * 4f;
            Quaternion want = Quaternion.LookRotation(aim - flashlight.transform.position, cam.transform.up);
            flashlight.transform.rotation = beamWasOn ? Quaternion.Slerp(flashlight.transform.rotation, want, 1f - Mathf.Exp(-14f * dt)) : want;
        }
        beamWasOn = flashOn;

        if (arms && !frozen) { arms.Drive(flashOn, earsCovered, running, runPhase, station == Station.Hidden); arms.fear = madLevel; }
        if (body)
        {
            if (Time.time >= bodyActionUntil)
            {
                string want = running ? "Run" : station == Station.Hidden ? "Terrified" : station == Station.TV ? "CrouchHold" : "Idle";
                if (want != bodyState) { bodyState = want; body.Play(want, 0.2f, 0f, want == "CrouchHold" ? 0f : 1f); }
            }
            body.flashIK = flashOn; body.earsIK = earsCovered;
            body.Align();
        }
    }

    void Hands(Vector3? l, Vector3? r = null)
    {
        if (arms) arms.Reach(l, r);
        if (!body) return;
        body.handsIK = l.HasValue;
        if (l.HasValue) { body.handLTarget = l.Value; body.handRTarget = r ?? l.Value; }
    }

    void BodyAction(string s, float dur, float start = 0f, float speed = 1f)
    {
        if (arms) arms.Act(s, dur);
        if (!body) return;
        body.Play(s, 0.08f, start, speed); bodyState = s; bodyActionUntil = Time.time + dur;
    }

    // ---------- hooks for the night director (and tests) ----------
    // editor tests: run one of the station actions directly
    public void DebugAction(string which)
    {
        switch (which)
        {
            case "door": StartCoroutine(DoorAction()); break;
            case "window": StartCoroutine(WindowAction()); break;
            case "tv": StartCoroutine(VaultToTV()); break;
            case "plug": StartCoroutine(PlugAction()); break;
            case "back": StartCoroutine(VaultBack()); break;
            case "stairs": StartCoroutine(StairsAction()); break;
            case "hide": StartCoroutine(HideInWardrobe()); break;
            case "unhide": StartCoroutine(ExitWardrobe()); break;
        }
    }
    public void ResetToRest(float lookYaw = 0f)
    {
        StopAllCoroutines();
        Hands(null);
        station = Station.Rest; flashForced = false; running = false; frozen = false;
        armRTarget = armLTarget = Down; armROff = armLOff = Vector3.zero; if (arms) arms.Clear();
        transform.position = restPos; yaw = lookYaw; pitch = 4f;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        cam.fieldOfView = baseFov;
    }
    public void TriggerDoorCreak() { StartCoroutine(CreakDoor()); }
    public void CreakDoorTo(float angle, float dur = 1.6f) { StartCoroutine(CreakDoor(angle, dur)); }

    // something on the other side yanks the curtains open again
    public WindowGust gust;      // wind bursts the window open instead of the curtains just snapping open
    public void SetCurtainAmount(float closed01) => SetCurtains(closed01);
    public void WindOpenedCurtains() { curtainsClosed = false; }
    public void Jolt(float amp, float dur) => Shake(amp, dur);
    public void ForceOpenCurtains()
    {
        if (!curtainsClosed) return;
        if (gust && !gust.Busy) { gust.Blow(); return; }
        curtainsClosed = false;
        PlayAt(curtainClip, curtainL.position, 1f);
        StartCoroutine(Animate(SetCurtains, 1f, 0f, 0.22f));
    }

    // death: stop everything and whip the head toward whatever got in
    public void Freeze(Vector3 lookAt, float turnTime = 0.12f, bool shake = true)
    {
        frozen = true;
        StopAllCoroutines();
        flashForced = false; running = false; earsCovered = false;
        station = Station.Busy;
        StartCoroutine(Turn(lookAt, turnTime));
        if (shake) Shake(0.12f, 1.2f);
    }
    public void Unfreeze() { frozen = false; }

    // ---------- helpers ----------
    void Shake(float amp, float dur)
    {
        float cur = shakeTime > 0f ? shakeAmp * Mathf.Clamp01(shakeTime / Mathf.Max(shakeDur, 0.01f)) : 0f;
        if (amp >= cur) { shakeAmp = amp; shakeDur = dur; shakeTime = dur; }
    }
    void Play(AudioClip c, float v = 1f) { if (sfx && c) sfx.PlayOneShot(c, v); }
    void PlayAt(AudioClip c, Vector3 p, float v = 1f) { if (c) AudioSource.PlayClipAtPoint(c, p, v); }
    public void Toast(string s, float t) { toast = s; toastUntil = Time.time + t; }
    void SyncAngles()
    {
        var e = transform.eulerAngles; yaw = e.y; pitch = e.x > 180f ? e.x - 360f : e.x;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }
    static IEnumerator Wait(float t) { yield return new WaitForSeconds(t); }

    // move + look in a panicky way (fast start, hard stop)
    IEnumerator Go(Vector3 to, Vector3 lookAt, float dur, bool run, float roll = 0f)
    {
        Vector3 p0 = transform.position; Quaternion r0 = transform.rotation;
        Vector3 dir = lookAt - to; if (dir.sqrMagnitude < 1e-4f) dir = transform.forward;
        Quaternion r1 = Quaternion.LookRotation(dir);
        running = run;
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur)
        {
            float e = run ? 1f - (1f - t) * (1f - t) : 1f - Mathf.Pow(1f - t, 3f);
            float er = 1f - Mathf.Pow(1f - Mathf.Clamp01(t * 1.7f), 3f);
            transform.position = Vector3.Lerp(p0, to, e);
            transform.rotation = Quaternion.Slerp(r0, r1, er) * Quaternion.Euler(0f, 0f, roll * Mathf.Sin(t * Mathf.PI));
            yield return null;
        }
        transform.position = to; transform.rotation = r1;
        running = false;
        SyncAngles();
    }

    // whip the head around (always turns horizontally, never flips through the floor)
    IEnumerator Turn(Vector3 lookAt, float dur)
    {
        SyncAngles();
        Vector3 dir = lookAt - transform.position;
        float y1 = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float p1 = -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg;
        float y0 = yaw, p0 = pitch, dy = Mathf.DeltaAngle(y0, y1);
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur)
        {
            float e = 1f - Mathf.Pow(1f - t, 3f);
            transform.rotation = Quaternion.Euler(Mathf.Lerp(p0, p1, e), y0 + dy * e, 0f);
            yield return null;
        }
        transform.rotation = Quaternion.Euler(p1, y0 + dy, 0f);
        SyncAngles();
    }

    IEnumerator Animate(System.Action<float> set, float from, float to, float dur, bool easeIn = true)
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur)
        {
            set(Mathf.Lerp(from, to, easeIn ? t * t : 1f - (1f - t) * (1f - t)));
            yield return null;
        }
        set(to);
    }

    IEnumerator RunHome()
    {
        Vector3 here = transform.position;
        yield return Turn(new Vector3(restPos.x, here.y, restPos.z), 0.18f);
        Vector3 fwd = restPos - here; fwd.y = 0f;
        yield return Go(restPos, restPos + fwd.normalized, 0.62f, true);
        yield return Turn(restPos + new Vector3(0f, -0.05f, 3f), 0.28f);
        Play(landClip, 0.3f); Shake(0.012f, 0.25f);   // back against the sofa
        station = Station.Rest;
    }

    // ---------- actions ----------
    IEnumerator DoorAction()
    {
        station = Station.Busy;
        yield return Turn(new Vector3(doorStandPos.x, transform.position.y, doorStandPos.z), 0.2f);   // whip around
        Shake(0.02f, 0.15f);
        yield return Go(doorStandPos, doorLook, 0.6f, true);
        armRTarget = new Vector3(-6f, -10f, -20f); armROff = new Vector3(0f, 0.06f, 0.16f);   // shove
        BodyAction("Push", 0.55f, 0.35f, 2.2f);
        yield return Wait(0.05f);
        doorAnimId++;                                   // cancels a creak in progress
        bool wasOpen = doorAngle > 1f;
        yield return Animate(SetDoor, doorAngle, 0f, 0.09f);
        if (wasOpen) { PlayAt(slamClip, door.position + Vector3.up, 1f); Shake(0.07f, 0.4f); }
        else { Play(landClip, 0.5f); Shake(0.03f, 0.2f); }
        yield return Wait(0.3f);
        armRTarget = Down; armROff = Vector3.zero; if (arms) arms.Clear();
        yield return RunHome();
    }

    IEnumerator CreakDoor() { return CreakDoor(14f, 1.6f); }
    IEnumerator CreakDoor(float to, float dur)
    {
        int id = ++doorAnimId;
        PlayAt(creakClip, door.position + Vector3.up, 0.9f);
        float from = doorAngle;
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur)
        {
            if (id != doorAnimId) yield break;
            SetDoor(Mathf.Lerp(from, to, 1f - (1f - t) * (1f - t)));
            yield return null;
        }
    }

    IEnumerator WindowAction()
    {
        station = Station.Busy;
        yield return Turn(new Vector3(windowStandPos.x, transform.position.y, windowStandPos.z), 0.15f);
        yield return Go(windowStandPos, windowLook, 0.65f, true);
        armRTarget = new Vector3(-22f, 22f, -30f); armLTarget = new Vector3(-22f, -22f, 30f); if (arms) arms.Act("CurtainGrab", 30f);   // grab both curtains
        armROff = new Vector3(0.05f, 0.08f, 0.1f); armLOff = new Vector3(-0.05f, 0.08f, 0.1f);
        yield return Wait(0.1f);
        bool closing = !curtainsClosed;
        armRTarget = new Vector3(-12f, closing ? -14f : 25f, -20f); armLTarget = new Vector3(-12f, closing ? 14f : -25f, 20f); if (arms) arms.Act(closing ? "CurtainClose" : "CurtainOpen", 30f);
        Play(curtainClip, 0.9f);
        if (!arms) BodyAction("PullRope", 0.6f, 0.4f, 2.5f);
        yield return Animate(SetCurtains, closing ? 0f : 1f, closing ? 1f : 0f, 0.13f);
        curtainsClosed = closing;
        Shake(0.035f, 0.3f);
        yield return Wait(0.3f);
        armRTarget = armLTarget = Down; armROff = armLOff = Vector3.zero; if (arms) arms.Clear();
        yield return RunHome();
    }

    IEnumerator VaultToTV()
    {
        station = Station.Busy;
        yield return Turn(new Vector3(-0.6f, 0.9f, 0.05f), 0.18f);
        armRTarget = new Vector3(42f, -4f, 0f); armLTarget = new Vector3(42f, 4f, 0f);     // palms on the sofa back
        BodyAction("Vault", 0.5f, 0.2f, 2.4f);
        armROff = armLOff = new Vector3(0f, 0.02f, 0.08f);
        Hands(new Vector3(0.24f, 0.97f, 0.35f), new Vector3(0.24f, 0.97f, -0.3f));
        yield return Wait(0.06f);
        yield return Go(new Vector3(0.22f, 1.8f, 0.06f), new Vector3(-1.3f, 0.55f, 0.05f), 0.22f, false, -10f);
        Shake(0.015f, 0.15f);
        yield return Go(new Vector3(-0.68f, 1.0f, 0.22f), new Vector3(-1.4f, 0.45f, 0.1f), 0.2f, false, 22f);   // clumsy tumble
        armRTarget = new Vector3(18f, -14f, -25f); armLTarget = new Vector3(18f, 14f, 25f);      // hands catch the fall
        armROff = armLOff = new Vector3(0f, -0.04f, 0.12f);
        Play(landClip, 0.9f); Shake(0.08f, 0.35f);
        BodyAction("Fall", 0.5f, 0f, 1.6f);
        Hands(new Vector3(-0.98f, 0.03f, 0.5f), new Vector3(-0.95f, 0.03f, -0.05f));
        yield return Wait(0.15f);
        armRTarget = armLTarget = Down; armROff = armLOff = Vector3.zero; if (arms) arms.Clear();
        yield return Go(tvCrouchPos, tvLook, 0.35f, false);
        Hands(null);
        baseYaw = yaw;
        station = Station.TV;
    }

    IEnumerator PlugAction()
    {
        station = Station.Busy;
        yield return Go(plugLeanPos, plugLook, 0.2f, false);
        armRTarget = new Vector3(28f, -12f, -15f); armROff = new Vector3(-0.04f, -0.02f, 0.16f); if (arms) arms.Act("PlugReach", 30f);
        yield return Wait(0.08f);
        if (tv.pluggedIn)
        {
            plugTugs++;
            if (plugTugs < 2)
            {
                armROff = new Vector3(-0.04f, -0.02f, 0.1f); if (arms) arms.Act("PlugTug", 30f);           // tug - it's stuck
                yield return tv.TugPlug();
                armROff = new Vector3(-0.04f, -0.02f, 0.16f); if (arms) arms.Act("PlugReach", 30f);
                Shake(0.02f, 0.2f);
                Toast("It won't budge!", 1.2f);
            }
            else
            {
                armRTarget = new Vector3(-8f, -4f, -30f); armROff = new Vector3(0.03f, 0.06f, 0.02f);   // rip it out
                Play(plugClip, 0.9f);
                BodyAction("Lever", 0.55f, 0.6f, 2.2f);
                yield return tv.YankPlug();
                Shake(0.05f, 0.3f);
                plugTugs = 0;
            }
        }
        else
        {
            yield return tv.InsertPlug();
            Shake(0.015f, 0.15f);
        }
        yield return Wait(0.15f);
        armRTarget = Down; armROff = Vector3.zero; if (arms) arms.Clear();
        yield return Go(tvCrouchPos, tvLook, 0.25f, false);
        baseYaw = yaw;
        station = Station.TV;
    }

    IEnumerator VaultBack()
    {
        station = Station.Busy;
        yield return Turn(new Vector3(0.6f, 1.0f, 0.05f), 0.18f);
        armRTarget = new Vector3(40f, -4f, 0f); armLTarget = new Vector3(40f, 4f, 0f);
        BodyAction("Vault", 0.5f, 0.2f, 2.4f);
        armROff = armLOff = new Vector3(0f, 0.02f, 0.08f);
        Hands(new Vector3(-0.1f, 0.6f, 0.4f), new Vector3(0.2f, 0.97f, -0.3f));
        yield return Go(new Vector3(-0.35f, 1.55f, 0.1f), new Vector3(1.4f, 0.8f, 0.05f), 0.28f, false, 10f);
        yield return Go(new Vector3(0.62f, 1.3f, 0.05f), new Vector3(1.5f, 1.1f, 0.4f), 0.2f, false, -16f);
        Play(landClip, 0.8f); Shake(0.06f, 0.3f);
        Hands(null);
        armRTarget = armLTarget = Down; armROff = armLOff = Vector3.zero; if (arms) arms.Clear();
        yield return Go(restPos, restPos + new Vector3(0f, -0.05f, 3f), 0.4f, false);
        station = Station.Rest;
    }

    IEnumerator StairsAction()
    {
        station = Station.Busy;
        yield return Turn(new Vector3(stairsStandPos.x, transform.position.y, stairsStandPos.z), 0.16f);
        flashForced = true;
        yield return Go(stairsStandPos, stairsLook, 0.6f, true);
        station = Station.Stairs;
        float t = 0f;
        while (true)
        {
            t += Time.deltaTime;
            Vector2 d = Mouse.current.delta.ReadValue() * mouseSensitivity * GameSettings.Sensitivity * 0.6f;
            yaw += d.x; pitch = Mathf.Clamp(pitch - d.y, -70f, 35f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            if (t > 0.3f && clicked) break;
            yield return null;
        }
        flashForced = false;
        station = Station.Busy;
        yield return RunHome();
    }

    IEnumerator HideInWardrobe()
    {
        station = Station.Busy;
        yield return Turn(new Vector3(wardrobeWaypoint.x, transform.position.y, wardrobeWaypoint.z), 0.18f);
        yield return Go(wardrobeWaypoint, wardrobeFront, 0.3f, true);
        yield return Go(wardrobeFront, new Vector3(-1.64f, 1.35f, wardrobeFront.z), 0.3f, true);
        armRTarget = new Vector3(4f, -16f, -20f); armROff = new Vector3(-0.04f, 0f, 0.14f); if (arms) arms.Act("WardGrab", 30f);
        yield return Wait(0.05f);
        PlayAt(wardrobeClip, wardrobeDoor.position + Vector3.up, 0.5f);
        if (arms) arms.Act("WardPull", 30f); else BodyAction("PullRope", 0.3f, 0.4f, 3f);
        yield return Animate(SetWard, wardAngle, 105f, 0.11f, false);    // rip it open
        Shake(0.03f, 0.2f);
        armRTarget = Down; armROff = Vector3.zero; if (arms) arms.Clear();
        yield return Go(wardrobeInside, wardrobeInside + Vector3.left, 0.26f, true);
        yield return Turn(wardrobePeek, 0.16f);
        armRTarget = new Vector3(0f, -30f, -40f); armROff = new Vector3(-0.08f, 0f, 0.1f); if (arms) arms.Act("WardHold", 30f);   // grab the door edge
        yield return Wait(0.04f);
        yield return Animate(SetWard, 105f, 9f, 0.09f);                // slam it, leave a gap
        PlayAt(wardrobeClip, wardrobeDoor.position + Vector3.up, 1f); Shake(0.06f, 0.35f);
        yield return Wait(0.1f);
        armRTarget = Down; armROff = Vector3.zero; if (arms) arms.Clear();
        baseYaw = yaw; hideTimer = 0f; nextBeat = Time.time + 0.5f;
        station = Station.Hidden;
    }

    IEnumerator ExitWardrobe()
    {
        station = Station.Busy;
        armRTarget = new Vector3(5f, -10f, -10f); armROff = new Vector3(0f, 0f, 0.18f); if (arms) arms.Act("WardPush", 30f);
        PlayAt(creakClip, wardrobeDoor.position + Vector3.up, 0.35f);
        yield return Animate(SetWard, wardAngle, 80f, 0.18f, false);
        armRTarget = Down; armROff = Vector3.zero; if (arms) arms.Clear();
        cam.fieldOfView = baseFov;
        yield return Go(wardrobeFront, wardrobeFront + Vector3.right, 0.35f, false);
        StartCoroutine(Animate(SetWard, 80f, 20f, 1.4f, false));        // the door swings back lazily
        yield return Turn(new Vector3(wardrobeWaypoint.x, transform.position.y, wardrobeWaypoint.z), 0.15f);
        yield return Go(wardrobeWaypoint, wardrobeWaypoint + (wardrobeWaypoint - wardrobeFront), 0.35f, true);
        yield return Go(restPos, restPos + (restPos - wardrobeWaypoint), 0.3f, true);
        yield return Turn(restPos + new Vector3(0f, -0.05f, 3f), 0.3f);
        Play(landClip, 0.3f); Shake(0.012f, 0.25f);
        station = Station.Rest;
    }

    // ---------- HUD ----------
    GUIStyle style;
    void OnGUI()
    {
        if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18, wordWrap = true };
        style.fontSize = Mathf.RoundToInt(20f * Screen.height / 720f);
        float cx = Screen.width / 2f, cy = Screen.height / 2f;
        if (station == Station.Rest || station == Station.TV)
        {
            float s = hover ? 8f : 3f;
            GUI.color = new Color(1f, 1f, 1f, hover ? 0.85f : 0.35f);
            GUI.DrawTexture(new Rect(cx - s / 2f, cy - s / 2f, s, s), Texture2D.whiteTexture);
            if (hover) { float k = Screen.height / 720f; GUI.color = new Color(0f, 0f, 0f, 0.8f); GUI.Label(new Rect(cx - 400f * k + 2f, cy + 16f * k + 2f, 800f * k, 34f * k), Loc.T(hover.label), style); GUI.color = new Color(1f, 1f, 1f, 0.9f); GUI.Label(new Rect(cx - 400f * k, cy + 16f * k, 800f * k, 34f * k), Loc.T(hover.label), style); }
        }
        if (station == Station.Hidden || station == Station.Stairs)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            GUI.Label(new Rect(cx - 400f, Screen.height - 70f * Screen.height / 720f, 800f, 34f * Screen.height / 720f), Loc.T(station == Station.Hidden ? "LMB — get out" : "LMB — back to the couch"), style);
        }
        if (Time.time < toastUntil)
        {
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(toastUntil - Time.time));
            GUI.Label(new Rect(0f, Screen.height * 0.16f, Screen.width, 80f * Screen.height / 720f), Loc.T(toast), style);
        }
        GUI.color = Color.white;
    }
}
