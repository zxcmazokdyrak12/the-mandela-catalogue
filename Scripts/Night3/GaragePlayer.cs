using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// Night 3 - the garage. Mark wants out of Mandela County before dawn.
// Mouse only: look around, LMB on a highlighted thing (hold for searching / pouring / cranking).
//   drawers, toolbox, shelves, visor -> search for the car keys
//   jerrycan -> pick it up; fuel cap -> hold to pour
//   chain hoist -> one tug per click, the door goes up a bit each time
//   door to the house -> slide the bolt shut
//   car -> get in. In the car: LMB = duck / sit up, Space = get out, hold LMB on the ignition to start it
// RMB (hold) = flashlight.  Space = step back to the middle of the garage.
// Everything you do makes noise. Noise brings them.
[DefaultExecutionOrder(50)]
public class GaragePlayer : MonoBehaviour
{
    public enum State { Stand, Moving, InCar, Ducked, Dead, Finale }

    [System.Serializable] public struct Station { public string name; public Vector3 eye, look; public float yawRange, pitchUp, pitchDown; }

    [Header("Refs")]
    public Camera cam;
    public FPArms arms;
    public Light flashlight;
    public Renderer flashLens;
    public FlashlightBattery battery;
    public GarageDirector director;
    public GarageGate gate;
    public GameObject jerrycan;            // hidden while carried
    public Transform car;                  // car root (front = +Z)
    public Vector2 carHalf = new Vector2(1.05f, 2.7f);   // half width / half length of the car (local)
    public AudioClip carDoorClip;
    public AudioClip chainClip;            // a long recording of a metal door going up; each tug plays a slice of it

    [Header("Stations (world)")]
    public Station[] stations =
    {
        new Station { name = "Middle",  eye = new Vector3(-3.30f, 1.62f, -3.20f), look = new Vector3(-5.0f, 1.0f, -4.6f), yawRange = 170f, pitchUp = 50f, pitchDown = 55f },
        new Station { name = "Bench",   eye = new Vector3(-3.05f, 1.60f, -5.70f), look = new Vector3(-2.3f, 1.0f, -5.9f), yawRange = 80f, pitchUp = 40f, pitchDown = 55f },
        new Station { name = "Side",    eye = new Vector3(-6.75f, 1.60f, -5.00f), look = new Vector3(-7.4f, 1.0f, -5.6f), yawRange = 100f, pitchUp = 40f, pitchDown = 60f },
        new Station { name = "Chain",   eye = new Vector3(-3.10f, 1.62f, -1.95f), look = new Vector3(-2.45f, 1.4f, -1.35f), yawRange = 80f, pitchUp = 45f, pitchDown = 45f },
        new Station { name = "Door",    eye = new Vector3(-3.05f, 1.62f, -4.20f), look = new Vector3(-2.3f, 1.3f, -4.2f), yawRange = 80f, pitchUp = 40f, pitchDown = 45f },
        new Station { name = "Driver",  eye = new Vector3(-5.40f, 1.08f, -4.25f), look = new Vector3(-5.4f, 1.0f, -1.0f), yawRange = 120f, pitchUp = 30f, pitchDown = 40f },
        new Station { name = "Ducked",  eye = new Vector3(-5.10f, 0.74f, -4.35f), look = new Vector3(-4.3f, 0.55f, -4.2f), yawRange = 50f, pitchUp = 15f, pitchDown = 25f },
    };
    public const int Middle = 0, Driver = 5, DuckedSt = 6;

    [Header("Tuning")]
    public float mouseSensitivity = 0.11f;
    public float flashIntensity = 6.5f;
    public float searchTime = 1.4f, pourTime = 11f, crankTime = 1.3f;
    public int chainTugs = 12;

    [Header("Noise per action")]
    public float searchNoise = 0.16f;      // per second
    public float pourNoise = 0.17f;        // per second
    public float tugNoise = 0.09f;
    public float crankNoise = 0.32f;

    public State state = State.Stand;
    public int station;
    public bool hasKeys, carrying;
    public float fuel;                     // 0..1
    public int keySpot;                    // searchId that hides the keys
    public bool FlashOn => flashlight && flashlight.enabled;
    public bool InCar => state == State.InCar || state == State.Ducked;
    public bool Ducked => state == State.Ducked;
    public bool Busy => holding != null;
    public bool GateOpen => gate && gate.open >= 0.98f;

    float yaw, pitch, baseYaw, basePitch, yawRange = 120f, pitchUp = 50f, pitchDown = 50f;
    bool clickArmed, flashOn;
    GarageSpot hover, holding; float holdT;
    int cranks, cranksNeeded;
    string toast; float toastUntil;
    Material lensMat;
    AudioSource sfx, loopSrc;
    AudioClip glug, clank0, clank1, crank, rummage, bolt, doorOpen, doorClose;
    GUIStyle sLabel, sSmall;
    float tugCooldown;

    void Start()
    {
        if (!cam) cam = GetComponentInChildren<Camera>();
        if (!director) director = FindFirstObjectByType<GarageDirector>();
        if (flashLens) { lensMat = flashLens.material; lensMat.EnableKeyword("_EMISSION"); }
        sfx = gameObject.AddComponent<AudioSource>(); sfx.playOnAwake = false; sfx.spatialBlend = 0f;
        loopSrc = gameObject.AddComponent<AudioSource>(); loopSrc.playOnAwake = false; loopSrc.loop = true; loopSrc.spatialBlend = 0f;
        glug = GarageSfx.Glug(); clank0 = GarageSfx.Chain(1); clank1 = GarageSfx.Chain(2); crank = GarageSfx.Crank(); bolt = GarageSfx.Bolt();
        rummage = SfxBank.Curtain();
        doorOpen = carDoorClip;
        cam.transform.localPosition = Vector3.zero; cam.transform.localRotation = Quaternion.identity;
        Place(Middle);
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        // the keys are somewhere different every time
        var spots = FindObjectsByType<GarageSpot>(FindObjectsSortMode.None);
        int n = 0; foreach (var s in spots) if (s.type == GarageSpotType.Search) n++;
        keySpot = Random.Range(0, Mathf.Max(1, n));
        cranksNeeded = Random.Range(2, 4);
        GameHUD.HideClock = false;
    }

    void Place(int st)
    {
        station = st;
        var s = stations[st];
        transform.position = s.eye;
        Vector3 d = s.look - s.eye;
        baseYaw = yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        basePitch = pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
        yawRange = s.yawRange; pitchUp = s.pitchUp; pitchDown = s.pitchDown;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void Update()
    {
        var m = Mouse.current; var kb = Keyboard.current;
        if (GameMenu.Paused) return;
        if (m == null || state == State.Dead || state == State.Moving || GameHUD.Blocking) { Lights(false); return; }
        if (state == State.Finale) { Look(m); Lights(false); return; }
        if (m.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; clickArmed = false; return; }

        Look(m);
        bool lmbDown = m.leftButton.wasPressedThisFrame, lmb = m.leftButton.isPressed;
        if (lmbDown) clickArmed = true;
        bool click = clickArmed && m.leftButton.wasReleasedThisFrame;
        if (click) clickArmed = false;
        bool back = kb != null && kb.spaceKey.wasPressedThisFrame;
        flashOn = m.rightButton.isPressed;
        tugCooldown -= Time.deltaTime;

        if (holding != null) { Hold(lmb); Lights(flashOn); return; }

        Hover();
        if (state == State.Stand)
        {
            if (lmbDown && hover) Use(hover);
            else if (back && station != Middle) StartCoroutine(MoveTo(Middle, 0.45f));
        }
        else if (state == State.InCar)
        {
            if (back) StartCoroutine(GetOut());
            else if (lmbDown && hover && hover.type == GarageSpotType.Ignition) BeginHold(hover);
            else if (lmbDown && hover && hover.type == GarageSpotType.Search) BeginHold(hover);
            else if (click && !hover) StartCoroutine(Duck(true));
        }
        else if (state == State.Ducked)
        {
            if (click || back) StartCoroutine(Duck(false));
        }
        Lights(flashOn);
    }

    void Look(Mouse m)
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;
        Vector2 md = m.delta.ReadValue() * mouseSensitivity * GameSettings.Sensitivity;
        yaw = Mathf.Clamp(yaw + md.x, baseYaw - yawRange, baseYaw + yawRange);
        pitch = Mathf.Clamp(pitch - md.y, basePitch - pitchUp, basePitch + pitchDown);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void Hover()
    {
        hover = null;
        // a fat ray: you do not have to hit small things pixel-perfect
        var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        if (!Physics.Raycast(ray, out var hit, 4.5f, ~0, QueryTriggerInteraction.Collide) &&
            !Physics.SphereCast(ray, 0.09f, out hit, 4.5f, ~0, QueryTriggerInteraction.Collide)) return;
        var s = hit.collider.GetComponent<GarageSpot>(); if (!s) return;
        bool inside = s.station == Driver;
        if (InCar != inside && s.type != GarageSpotType.Car) return;
        if (InCar && s.type == GarageSpotType.Car) return;
        hover = s;
    }

    // ---------------------------------------------------------------- actions
    void Use(GarageSpot s)
    {
        if (s.type == GarageSpotType.Car) { StartCoroutine(GetIn()); return; }
        if (station != s.station) { StartCoroutine(MoveThenUse(s)); return; }
        switch (s.type)
        {
            case GarageSpotType.Search:
            case GarageSpotType.FuelCap:
                BeginHold(s); break;
            case GarageSpotType.Jerrycan:
                if (fuel >= 1f) { Toast("The tank is already full.", 1.5f); break; }
                carrying = true; if (jerrycan) jerrycan.SetActive(false);
                sfx.PlayOneShot(rummage, 0.3f); director.AddNoise(0.05f);
                Toast("Gas can. Heavy, nearly full.", 2f); break;
            case GarageSpotType.Chain:
                if (GateOpen) { Toast("The door is all the way up.", 1.5f); break; }
                if (tugCooldown > 0f) break;
                tugCooldown = 0.45f;
                gate.open = Mathf.Min(1f, gate.open + 1f / chainTugs);
                if (chainClip) ChainSlice(); else sfx.PlayOneShot(Random.value < 0.5f ? clank0 : clank1, 0.9f);
                director.AddNoise(tugNoise);
                if (arms) arms.Act("Push", 0.35f);
                if (GateOpen) Toast("The door is open.", 1.5f);
                break;
            case GarageSpotType.HouseDoor:
                if (director.BoltLocked) { Toast("It's locked.", 1.2f); break; }
                director.LockBolt();
                sfx.PlayOneShot(bolt, 0.8f);
                break;
        }
    }

    AudioSource chainSrc;
    void ChainSlice()
    {
        if (!chainSrc) { chainSrc = gameObject.AddComponent<AudioSource>(); chainSrc.playOnAwake = false; chainSrc.spatialBlend = 0f; }
        chainSrc.Stop();
        chainSrc.clip = chainClip; chainSrc.volume = 0.8f; chainSrc.pitch = Random.Range(0.95f, 1.05f);
        chainSrc.time = Mathf.Clamp(gate.open * (chainClip.length - 0.6f), 0f, Mathf.Max(0f, chainClip.length - 0.6f));
        chainSrc.Play();
        chainSrc.SetScheduledEndTime(AudioSettings.dspTime + 0.42f);
    }

    IEnumerator MoveThenUse(GarageSpot s)
    {
        yield return MoveTo(s.station, 0.55f);
        if (state == State.Stand) Use(s);
    }

    void BeginHold(GarageSpot s)
    {
        if (s.type == GarageSpotType.Search && s.searched) { Toast("Already looked there.", 1.2f); return; }
        if (s.type == GarageSpotType.FuelCap)
        {
            if (fuel >= 1f) { Toast("The tank is full.", 1.2f); return; }
            if (!carrying) { Toast("I need the gas can.", 1.5f); return; }
            loopSrc.clip = glug; loopSrc.volume = 0.7f; loopSrc.Play();
        }
        if (s.type == GarageSpotType.Ignition)
        {
            if (!hasKeys) { Toast("No keys.", 1.2f); return; }
            if (fuel < 1f) { Toast("The tank is empty.", 1.5f); return; }
            if (!GateOpen) { Toast("The garage door is still closed.", 1.8f); return; }
            sfx.PlayOneShot(crank, 0.9f);
            director.AddNoise(crankNoise);
        }
        if (s.type == GarageSpotType.Search) { sfx.clip = rummage; sfx.loop = true; sfx.volume = 0.35f; sfx.Play(); }
        holding = s; holdT = 0f;
        if (arms && !InCar) arms.Reach(s.transform.position, s.transform.position + Vector3.right * 0.1f);
    }

    void Hold(bool lmb)
    {
        var s = holding;
        if (!lmb && s.type != GarageSpotType.Ignition) { EndHold(); return; }
        holdT += Time.deltaTime;
        switch (s.type)
        {
            case GarageSpotType.Search:
                director.AddNoise(searchNoise * Time.deltaTime);
                if (holdT >= searchTime)
                {
                    s.searched = true;
                    if (s.searchId == keySpot) { hasKeys = true; Toast("The car keys!", 2.5f); }
                    else Toast("Not here.", 1.2f);
                    EndHold();
                }
                break;
            case GarageSpotType.FuelCap:
                director.AddNoise(pourNoise * Time.deltaTime);
                fuel = Mathf.Min(1f, fuel + Time.deltaTime / pourTime);
                if (fuel >= 1f) { carrying = false; Toast("The tank is full.", 2f); EndHold(); }
                break;
            case GarageSpotType.Ignition:
                if (holdT >= crankTime)
                {
                    cranks++;
                    EndHold();
                    if (cranks >= cranksNeeded) director.StartFinale();
                    else Toast("It won't catch...", 1.5f);
                }
                break;
        }
    }

    void EndHold()
    {
        holding = null;
        if (loopSrc.isPlaying) loopSrc.Stop();
        if (sfx.loop) { sfx.loop = false; sfx.Stop(); }
        if (arms && !InCar) arms.Reach(null, null);
    }

    // ---------------------------------------------------------------- moving
    // the car is in the middle of the garage: walking from one side to the other goes round the back of it
    bool InsideCar(Vector3 l) => Mathf.Abs(l.x) < carHalf.x && Mathf.Abs(l.z) < carHalf.y;
    bool CrossesCar(Vector3 a, Vector3 b)
    {
        if (!car) return false;
        Vector3 la = car.InverseTransformPoint(a), lb = car.InverseTransformPoint(b);
        if (InsideCar(la) || InsideCar(lb)) return false;
        for (int i = 1; i < 24; i++) if (InsideCar(Vector3.Lerp(la, lb, i / 24f))) return true;
        return false;
    }

    IEnumerator MoveTo(int st, float dur)
    {
        state = State.Moving;
        var s = stations[st];
        var pts = new System.Collections.Generic.List<Vector3> { transform.position };
        if (CrossesCar(transform.position, s.eye))
        {
            float sa = Mathf.Sign(car.InverseTransformPoint(transform.position).x), sb = Mathf.Sign(car.InverseTransformPoint(s.eye).x);
            Vector3 c1 = car.TransformPoint(new Vector3(sa * (carHalf.x + 0.3f), 0f, -carHalf.y - 0.45f)); c1.y = 1.62f;
            Vector3 c2 = car.TransformPoint(new Vector3(sb * (carHalf.x + 0.3f), 0f, -carHalf.y - 0.45f)); c2.y = 1.62f;
            pts.Add(c1); pts.Add(c2);
        }
        pts.Add(s.eye);
        float total = 0f; for (int i = 1; i < pts.Count; i++) total += Vector3.Distance(pts[i - 1], pts[i]);
        if (pts.Count > 2) dur *= Mathf.Clamp(total / 2.5f, 1f, 3f);
        Quaternion r0 = transform.rotation;
        Quaternion r1 = Quaternion.LookRotation(s.look - s.eye);
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur)
        {
            float e = t * t * (3f - 2f * t);
            float d = e * total; Vector3 pos = pts[pts.Count - 1];
            for (int i = 1; i < pts.Count; i++)
            {
                float seg = Vector3.Distance(pts[i - 1], pts[i]);
                if (d <= seg || i == pts.Count - 1) { pos = Vector3.Lerp(pts[i - 1], pts[i], seg > 0f ? Mathf.Clamp01(d / seg) : 1f); break; }
                d -= seg;
            }
            transform.position = pos + Vector3.up * Mathf.Sin(t * Mathf.PI * 2f * (pts.Count - 1)) * 0.015f;
            Quaternion along = pts.Count > 2 && e < 0.85f ? Quaternion.LookRotation(Vector3.ProjectOnPlane(pts[Mathf.Min(pts.Count - 1, 1 + Mathf.FloorToInt(e * (pts.Count - 1)))] - pos, Vector3.up).normalized + Vector3.down * 0.15f) : r1;
            transform.rotation = Quaternion.Slerp(transform.rotation, pts.Count > 2 ? along : Quaternion.Slerp(r0, r1, e), pts.Count > 2 ? Time.deltaTime * 8f : 1f);
            yield return null;
        }
        Place(st);
        state = st == Driver ? State.InCar : st == DuckedSt ? State.Ducked : State.Stand;
    }

    IEnumerator GetIn()
    {
        if (carrying) { carrying = false; if (jerrycan) jerrycan.SetActive(true); Toast("I put the gas can down.", 1.5f); }
        if (doorOpen) sfx.PlayOneShot(doorOpen, 0.6f);
        director.AddNoise(0.05f);
        yield return MoveTo(Driver, 0.7f);
        if (arms) arms.Reach(stations[Driver].eye + transform.forward * 0.45f + Vector3.down * 0.15f + transform.right * -0.17f,
                             stations[Driver].eye + transform.forward * 0.45f + Vector3.down * 0.15f + transform.right * 0.17f);
    }

    IEnumerator GetOut()
    {
        if (arms) arms.Reach(null, null);
        if (doorOpen) sfx.PlayOneShot(doorOpen, 0.6f);
        director.AddNoise(0.05f);
        yield return MoveTo(Middle, 0.7f);
    }

    IEnumerator Duck(bool down)
    {
        if (arms) arms.Reach(null, null);
        yield return MoveTo(down ? DuckedSt : Driver, down ? 0.3f : 0.4f);
    }

    // ---------------------------------------------------------------- lights / arms
    void Lights(bool on)
    {
        float batt = battery ? battery.Tick(on, Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame, Time.deltaTime) : 1f;
        if (flashlight)
        {
            flashlight.enabled = on && batt > 0.001f && state != State.Dead && state != State.Finale;
            if (flashlight.enabled) flashlight.intensity = flashIntensity * batt * (0.94f + 0.06f * Mathf.PerlinNoise(Time.time * 6f, 1f));
        }
        if (lensMat) lensMat.SetColor("_EmissionColor", flashlight && flashlight.enabled ? new Color(1f, 0.92f, 0.75f) * 8f * batt : Color.black);
    }

    void LateUpdate()
    {
        if (!arms || state == State.Dead) return;
        arms.Drive(flashOn && flashlight && flashlight.enabled, false, false, 0f, false);
        arms.fear = Ducked ? 0.7f : 0.25f;
    }

    // ---------------------------------------------------------------- director hooks
    public void Freeze(Vector3 look)
    {
        StopAllCoroutines(); EndHold();
        if (arms) { arms.Reach(null, null); arms.Act("Defend", 5f); }
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        state = State.Dead;
        StartCoroutine(Turn(look, 0.08f));
    }

    public IEnumerator Turn(Vector3 look, float dur)
    {
        Quaternion r0 = transform.rotation, r1 = Quaternion.LookRotation(look - transform.position);
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur) { transform.rotation = Quaternion.Slerp(r0, r1, t * t * (3f - 2f * t)); yield return null; }
        transform.rotation = r1;
        Vector3 e = r1.eulerAngles; yaw = e.y; pitch = e.x > 180f ? e.x - 360f : e.x;
    }

    // the ending: sit up in the driver's seat, keep looking around a little
    public void BeginFinale()
    {
        StopAllCoroutines(); EndHold();
        Place(Driver);
        yawRange = 150f; pitchUp = 25f; pitchDown = 30f;
        state = State.Finale;
        if (arms) arms.Reach(stations[Driver].eye + Vector3.forward * 0.45f + Vector3.down * 0.15f + Vector3.left * 0.17f,
                             stations[Driver].eye + Vector3.forward * 0.45f + Vector3.down * 0.15f + Vector3.right * 0.17f);
    }

    public void SetBaseLook(Vector3 dir, float range)
    {
        baseYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; yawRange = range;
        yaw = Mathf.Clamp(yaw, baseYaw - yawRange, baseYaw + yawRange);
    }

    public void Toast(string s, float t) { toast = s; toastUntil = Time.time + t; }

    // test hooks
    public void DebugSet(bool keys, float fuelAmount, float gateOpen) { hasKeys = keys; fuel = fuelAmount; if (gate) gate.open = gateOpen; }
    public void DebugGo(int st) { StopAllCoroutines(); EndHold(); Place(st); state = st == Driver ? State.InCar : st == DuckedSt ? State.Ducked : State.Stand; }

    // ---------------------------------------------------------------- GUI
    void OnGUI()
    {
        if (sLabel == null)
        {
            sLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            sSmall = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
        }
        if (state == State.Dead || GameHUD.Blocking) return;
        float w = Screen.width, h = Screen.height, k = h / 720f, cx = w / 2f, cy = h / 2f;
        sLabel.fontSize = Mathf.RoundToInt(20 * k); sSmall.fontSize = Mathf.RoundToInt(16 * k);
        if (state == State.Finale) return;

        // crosshair + what you are looking at
        float sz = hover ? 8f : 3f;
        GUI.color = new Color(1f, 1f, 1f, hover ? 0.85f : 0.35f);
        GUI.DrawTexture(new Rect(cx - sz / 2f, cy - sz / 2f, sz, sz), Texture2D.whiteTexture);
        if (hover && holding == null) Shadow(new Rect(cx - 400 * k, cy + 16 * k, 800 * k, 34 * k), Label(hover), sLabel, new Color(1f, 1f, 1f, 0.9f));

        // hold progress
        if (holding != null)
        {
            float need = holding.type == GarageSpotType.Search ? searchTime : holding.type == GarageSpotType.Ignition ? crankTime : pourTime;
            float p = holding.type == GarageSpotType.FuelCap ? fuel : Mathf.Clamp01(holdT / need);
            Rect bar = new Rect(cx - 90 * k, cy + 26 * k, 180 * k, 6 * k);
            GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(0.9f, 0.9f, 0.85f, 0.85f); GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * p, bar.height), Texture2D.whiteTexture);
        }

        // noise
        float noise = director ? director.Noise : 0f;
        if (noise > 0.03f)
        {
            Rect nb = new Rect(cx - 70 * k, h - 92 * k, 140 * k, 5 * k);
            GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(nb, Texture2D.whiteTexture);
            GUI.color = Color.Lerp(new Color(0.85f, 0.85f, 0.8f, 0.7f), new Color(0.9f, 0.15f, 0.1f, 0.9f), noise);
            GUI.DrawTexture(new Rect(nb.x, nb.y, nb.width * noise, nb.height), Texture2D.whiteTexture);
            sSmall.fontSize = Mathf.RoundToInt(12 * k);
            Shadow(new Rect(0, nb.y - 18 * k, w, 16 * k), "NOISE", sSmall, new Color(1f, 1f, 1f, 0.45f));
            sSmall.fontSize = Mathf.RoundToInt(16 * k);
        }

        string hint = state == State.InCar ? "LMB — duck        Space — get out        hold LMB on the key — start the engine"
                    : state == State.Ducked ? "LMB — sit up"
                    : station != Middle ? "Space — step back" : "";
        if (hint != "") Shadow(new Rect(0, h - 60 * k, w, 30 * k), hint, sSmall, new Color(1f, 1f, 1f, 0.45f));

        // what you have
        string inv = (hasKeys ? Loc.T("KEYS") + "   " : "") + (carrying ? Loc.T("GAS CAN") + "   " : "") + (fuel > 0f ? Loc.T("FUEL") + " " + Mathf.FloorToInt(fuel * 100f) + "%   " : "") + (gate && gate.open > 0f ? Loc.T("DOOR") + " " + Mathf.FloorToInt(gate.open * 100f) + "%" : "");
        if (inv != "") { sSmall.alignment = TextAnchor.MiddleLeft; Shadow(new Rect(26 * k, h - 40 * k, w, 26 * k), inv, sSmall, new Color(1f, 1f, 1f, 0.5f)); sSmall.alignment = TextAnchor.MiddleCenter; }

        if (Time.time < toastUntil) Shadow(new Rect(0, h * 0.18f, w, 60 * k), toast, sLabel, new Color(1f, 1f, 1f, Mathf.Clamp01(toastUntil - Time.time)));
        GUI.color = Color.white;
    }

    string Label(GarageSpot s)
    {
        switch (s.type)
        {
            case GarageSpotType.Search: return Loc.T(s.label) + Loc.T(s.searched ? " (searched)" : " — hold LMB to search");
            case GarageSpotType.FuelCap: return carrying ? "Fuel cap — hold LMB to pour" : "Fuel cap (bring the gas can)";
            case GarageSpotType.Jerrycan: return "Gas can — LMB to pick it up";
            case GarageSpotType.Chain: return "Chain hoist — LMB to pull";
            case GarageSpotType.HouseDoor: return director && director.BoltLocked ? "Door to the house (locked)" : "Door to the house — LMB to lock it!";
            case GarageSpotType.Ignition: return hasKeys ? "Ignition — hold LMB to start" : "Ignition (no keys)";
            case GarageSpotType.Car: return "Car — get in";
        }
        return s.label;
    }

    static void Shadow(Rect r, string s, GUIStyle st, Color c)
    {
        s = Loc.T(s);
        GUI.color = new Color(0f, 0f, 0f, c.a * 0.8f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, st);
        GUI.color = c; GUI.Label(r, s, st);
    }
}
