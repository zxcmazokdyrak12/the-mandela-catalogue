using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Night 2 controller. Mark is in bed in the upstairs bedroom, facing the door, the window behind him.
// Mouse only:
//   look around with the mouse, LMB on a highlighted thing:
//     laptop  -> sit at it: a small desktop with a mouse cursor (files decrypt only while you are at the laptop
//                and DECRYPT.EXE is running); RMB / Space = stand up
//     desk    -> crawl under it and hide; LMB = come out
//     papers  -> read the newspapers; LMB = next page, RMB = put them down
//   hold RMB = flashlight (in bed / under the desk)
[DefaultExecutionOrder(50)]
public class BedroomPlayer : MonoBehaviour
{
    public enum State { Bed, Laptop, Desk, Papers, Moving, Dead }

    [Header("Refs")]
    public Camera cam;
    public FPArms arms;
    public Light flashlight;
    public Renderer flashLens;
    public FlashlightBattery battery;
    public Renderer laptopScreen;          // glows on the nightstand
    public Light laptopGlow;
    public Texture2D[] newspapers;

    [Header("Places (world)")]
    public Vector3 bedEye = new Vector3(-0.35f, 3.98f, -4.05f);
    public Vector3 bedLook = new Vector3(0.55f, 3.6f, -1.1f);
    public Vector3 laptopEye = new Vector3(0.38f, 3.9f, -3.98f);
    public Vector3 laptopLook = new Vector3(0.72f, 3.52f, -4.36f);
    public Vector3 deskEye = new Vector3(1.32f, 3.17f, -2.7f);
    public Vector3 deskLook = new Vector3(-0.6f, 3.05f, -2.0f);
    public Vector3 keyL = new Vector3(0.64f, 3.46f, -4.28f), keyR = new Vector3(0.78f, 3.46f, -4.30f);

    [Header("Tuning")]
    public float mouseSensitivity = 0.11f;
    public float flashIntensity = 6.5f;
    [Tooltip("Seconds at the laptop to decrypt the whole archive.")]
    public float decryptSeconds = 170f;
    public float maxHideTime = 22f;

    public State state = State.Bed;
    public float decrypt;                 // 0..1
    public bool Hidden => state == State.Desk;
    public bool AtLaptop => state == State.Laptop;
    public bool CanLeaveDesk = true;      // the director closes this while something is in the room
    [Tooltip("The laptop starts switched off. Standing up leaves it running; only Start > Shut Down turns it off.")]
    public bool LaptopOn;
    public bool FlashOn => flashlight && flashlight.enabled;
    MaterialPropertyBlock screenBlock;
    public int FilesUnlocked => Mathf.Min(LaptopContent.Files.Length, Mathf.FloorToInt(decrypt * LaptopContent.Files.Length + 0.0001f));

    float yaw, pitch, baseYaw, basePitch, yawRange = 120f, pitchUp = 55f, pitchDown = 55f;
    bool clickArmed, flashOn; float hideTimer;
    BedSpot hover;
    string toast; float toastUntil;
    Material lensMat;
    int paperIdx;
    LaptopDesktop desk;
    NightTimer timer;
    public NightTimer Timer => timer;
    public int ChatShown => shown.Count;
    public LaptopDesktop Desk => desk;
    // chat
    readonly List<int> shown = new List<int>();
    float nextMsgAt, typingUntil;
    AudioSource sfx; AudioClip ping, keyClick;
    GUIStyle sLabel, sSmall;

    void Start()
    {
        if (!cam) cam = GetComponentInChildren<Camera>();
        if (flashLens) { lensMat = flashLens.material; lensMat.EnableKeyword("_EMISSION"); }
        sfx = gameObject.AddComponent<AudioSource>(); sfx.playOnAwake = false; sfx.spatialBlend = 0f;
        ping = MakePing(); keyClick = MakeClick();
        cam.transform.localPosition = Vector3.zero; cam.transform.localRotation = Quaternion.identity;
        Place(bedEye, bedLook, 120f, 55f, 55f);
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        for (int i = 0; i < LaptopContent.Chat.Length; i++) if (LaptopContent.Chat[i].at < 0f) shown.Add(i);
        timer = FindFirstObjectByType<NightTimer>();
        GameHUD.HideClock = false;
        desk = new LaptopDesktop(this);
        desk.Click = v => sfx.PlayOneShot(keyClick, v);
        ScreenPower(LaptopOn);
    }

    void Place(Vector3 eye, Vector3 look, float yr, float pu, float pd)
    {
        transform.position = eye;
        Vector3 d = look - eye;
        baseYaw = yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        basePitch = pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
        yawRange = yr; pitchUp = pu; pitchDown = pd;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void Update()
    {
        var m = Mouse.current; var kb = Keyboard.current;
        if (GameMenu.Paused) return;
        if (m == null || state == State.Dead || state == State.Moving || GameHUD.Blocking) { Lights(false); return; }
        if (state != State.Laptop)
        {
            if (m.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; clickArmed = false; return; }
        }

        GameHUD.HideClock = state == State.Laptop || state == State.Papers;
        bool rmb = m.rightButton.isPressed;
        if (m.leftButton.wasPressedThisFrame) clickArmed = true;
        bool click = clickArmed && m.leftButton.wasReleasedThisFrame;
        if (click) clickArmed = false;
        bool back = m.rightButton.wasPressedThisFrame || (kb != null && kb.spaceKey.wasPressedThisFrame);

        // look
        if (state != State.Papers && state != State.Laptop && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 md = m.delta.ReadValue() * mouseSensitivity * GameSettings.Sensitivity;
            float lim = state == State.Laptop ? 0.25f : 1f;
            yaw = Mathf.Clamp(yaw + md.x * lim, baseYaw - yawRange * lim, baseYaw + yawRange * lim);
            pitch = Mathf.Clamp(pitch - md.y * lim, basePitch - pitchUp * lim, basePitch + pitchDown * lim);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        switch (state)
        {
            case State.Bed:
                Hover();
                flashOn = rmb;
                if (click && hover)
                {
                    if (hover.type == BedSpotType.Laptop) StartCoroutine(GoLaptop());
                    else if (hover.type == BedSpotType.Desk) StartCoroutine(GoDesk(bedEye));
                    else if (hover.type == BedSpotType.Papers) { state = State.Papers; paperIdx = 0; Cursor.visible = false; }
                }
                break;
            case State.Laptop:
                flashOn = false; hover = null;
                if (desk.Ready && desk.DecryptRunning) decrypt = Mathf.Min(1f, decrypt + Time.deltaTime / Mathf.Max(1f, decryptSeconds));
                desk.Tick(m);
                if (back && !desk.ShuttingDown) CloseLaptop();
                break;
            case State.Desk:
                flashOn = rmb; hover = null;
                hideTimer += Time.deltaTime;
                if (hideTimer > maxHideTime && CanLeaveDesk) { Toast("My legs went numb...", 2f); StartCoroutine(LeaveDesk()); }
                else if (click) StartCoroutine(LeaveDesk());
                break;
            case State.Papers:
                flashOn = false; hover = null;
                if (click) paperIdx = (paperIdx + 1) % Mathf.Max(1, newspapers.Length);
                if (back) state = State.Bed;
                break;
        }
        UpdateChat();
        Lights(flashOn);
    }

    void Lights(bool on)
    {
        float batt = battery ? battery.Tick(on, Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame, Time.deltaTime) : 1f;
        if (flashlight)
        {
            flashlight.enabled = on && batt > 0.001f && state != State.Dead;
            if (flashlight.enabled) flashlight.intensity = flashIntensity * batt * (0.94f + 0.06f * Mathf.PerlinNoise(Time.time * 6f, 1f));
        }
        if (lensMat) lensMat.SetColor("_EmissionColor", flashlight && flashlight.enabled ? new Color(1f, 0.92f, 0.75f) * 8f * batt : Color.black);
        if (laptopGlow) { laptopGlow.enabled = LaptopOn; laptopGlow.intensity = (state == State.Laptop ? 1.1f : 0.65f) * (0.92f + 0.08f * Mathf.PerlinNoise(Time.time * 3f, 0.2f)); }
    }

    void LateUpdate()
    {
        if (!arms || state == State.Dead) return;
        arms.Drive(flashOn, false, false, 0f, false);   // no 'terrified' hands under the desk - they block the view of the room
        arms.fear = state == State.Desk ? 0.6f : 0.15f;
    }

    void Hover()
    {
        hover = null;
        if (Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f)), out var hit, 4f, ~0, QueryTriggerInteraction.Collide))
            hover = hit.collider.GetComponent<BedSpot>();
    }

    // ---------------------------------------------------------------- moves
    IEnumerator Move(Vector3 eye, Vector3 look, float dur, float dip = 0f)
    {
        state = State.Moving;
        Vector3 p0 = transform.position; Quaternion r0 = transform.rotation;
        Quaternion r1 = Quaternion.LookRotation(look - eye);
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur)
        {
            float e = t * t * (3f - 2f * t);
            transform.position = Vector3.Lerp(p0, eye, e) + Vector3.down * Mathf.Sin(t * Mathf.PI) * dip;
            transform.rotation = Quaternion.Slerp(r0, r1, e);
            yield return null;
        }
    }

    IEnumerator GoLaptop()
    {
        yield return Move(laptopEye, laptopLook, 0.55f);
        Place(laptopEye, laptopLook, 25f, 15f, 15f);
        if (arms) arms.Reach(keyL, keyR);
        if (!LaptopOn) { LaptopOn = true; ScreenPower(true); desk.BeginBoot(); }
        // free the mouse for the desktop; the cursor itself is drawn by the laptop
        Cursor.lockState = Application.isEditor ? CursorLockMode.None : CursorLockMode.Confined; Cursor.visible = false;
        if (Mouse.current != null) Mouse.current.WarpCursorPosition(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        desk.OnEnter();
        state = State.Laptop;
    }

    public void CloseLaptop() { if (state == State.Laptop) StartCoroutine(LeaveLaptop()); }

    // Start > Shut Down: the screen goes black and Mark gets up
    public void ShutDownLaptop() { if (state == State.Laptop && !desk.ShuttingDown) StartCoroutine(ShutDown()); }
    IEnumerator ShutDown()
    {
        desk.BeginShutdown();
        for (float t = 0f; t < 0.85f; t += Time.deltaTime) yield return null;
        LaptopOn = false; ScreenPower(false);
        sfx.PlayOneShot(keyClick, 0.6f);
        yield return LeaveLaptop();
    }

    // the screen on the nightstand: lit or dead black
    void ScreenPower(bool on)
    {
        if (laptopGlow) laptopGlow.enabled = on;
        if (!laptopScreen) return;
        if (screenBlock == null) screenBlock = new MaterialPropertyBlock();
        if (on) laptopScreen.SetPropertyBlock(null);
        else
        {
            screenBlock.Clear();
            screenBlock.SetColor("_BaseColor", new Color(0.015f, 0.015f, 0.02f));
            screenBlock.SetColor("_Color", new Color(0.015f, 0.015f, 0.02f));
            screenBlock.SetColor("_EmissionColor", Color.black);
            laptopScreen.SetPropertyBlock(screenBlock);
        }
    }

    IEnumerator LeaveLaptop()
    {
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        if (arms) arms.Reach(null, null);
        yield return Move(bedEye, bedLook, 0.4f);
        Place(bedEye, bedLook, 120f, 55f, 55f);
        state = State.Bed;
    }

    IEnumerator GoDesk(Vector3 from)
    {
        // roll off the bed and crawl under the desk
        yield return Move(new Vector3(0.75f, 3.45f, -3.0f), deskEye + Vector3.left * 0.4f, 0.45f, 0.1f);
        yield return Move(deskEye, deskLook, 0.5f, 0.05f);
        Place(deskEye, deskLook, 75f, 20f, 25f);
        hideTimer = 0f;
        state = State.Desk;
    }

    IEnumerator LeaveDesk()
    {
        yield return Move(new Vector3(0.75f, 3.45f, -3.0f), bedLook, 0.45f, 0.05f);
        yield return Move(bedEye, bedLook, 0.5f);
        Place(bedEye, bedLook, 120f, 55f, 55f);
        state = State.Bed;
    }

    // death: stop everything and look at it
    public void Freeze(Vector3 look)
    {
        StopAllCoroutines();
        if (arms) { arms.Reach(null, null); arms.Act("Defend", 5f); }
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        state = State.Dead;
        StartCoroutine(Turn(look, 0.08f));
    }
    IEnumerator Turn(Vector3 look, float dur)
    {
        Quaternion r0 = transform.rotation, r1 = Quaternion.LookRotation(look - transform.position);
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur) { transform.rotation = Quaternion.Slerp(r0, r1, t); yield return null; }
        transform.rotation = r1;
    }

    public void Toast(string s, float t) { toast = s; toastUntil = Time.time + t; }

    // test hooks
    public void DebugGo(string where)
    {
        StopAllCoroutines();
        if (where != "laptop") { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        if (where == "laptop") StartCoroutine(GoLaptop());
        else if (where == "desk") StartCoroutine(GoDesk(bedEye));
        else if (where == "papers") state = State.Papers;
        else if (where == "bed") { Place(bedEye, bedLook, 120f, 55f, 55f); state = State.Bed; if (arms) arms.Reach(null, null); }
    }

    // ---------------------------------------------------------------- chat
    void UpdateChat()
    {
        if (Time.time < nextMsgAt) return;
        for (int i = 0; i < LaptopContent.Chat.Length; i++)
        {
            if (shown.Contains(i)) continue;
            var c = LaptopContent.Chat[i];
            if (decrypt + 0.0001f < Mathf.Min(1f, c.at)) return;            // in order
            if (typingUntil <= 0f) { typingUntil = Time.time + (c.mine ? 0.6f : 1.8f + c.text.Length * 0.02f); return; }
            if (Time.time < typingUntil) return;
            shown.Add(i); typingUntil = 0f; nextMsgAt = Time.time + 1.2f;
            if (!c.mine) sfx.PlayOneShot(ping, state == State.Laptop ? 0.5f : 0.25f);
            desk.NotifyMessage();
            return;
        }
    }
    public bool Typing => typingUntil > 0f && Time.time < typingUntil;

    // ---------------------------------------------------------------- GUI
    void Styles()
    {
        if (sLabel != null) return;
        sLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
        sSmall = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
    }

    void OnGUI()
    {
        if (GameMenu.Paused && Event.current.type != EventType.Repaint) return;   // the pause menu gets the clicks
        Styles();
        float w = Screen.width, h = Screen.height, k = h / 720f, cx = w / 2f, cy = h / 2f;
        sLabel.fontSize = Mathf.RoundToInt(20 * k); sSmall.fontSize = Mathf.RoundToInt(17 * k);
        if (state == State.Laptop)
        {
            desk.Draw();
            Shadow(new Rect(0, h - 34 * k, w, 28 * k), "RMB / Space — stand up (the laptop stays ON)        Start → Shut Down — turn it off", sSmall, new Color(1f, 1f, 1f, 0.45f));
            GUI.color = Color.white;
            return;
        }
        if (state == State.Papers) { Papers(w, h, k); return; }
        if (state == State.Bed)
        {
            float s = hover ? 8f : 3f;
            GUI.color = new Color(1f, 1f, 1f, hover ? 0.85f : 0.35f);
            GUI.DrawTexture(new Rect(cx - s / 2f, cy - s / 2f, s, s), Texture2D.whiteTexture);
            if (hover)
            {
                string lbl = hover.type == BedSpotType.Laptop ? (LaptopOn ? "Laptop — it's ON (the screen is glowing)" : "Laptop — turn it on") : hover.label;
                Shadow(new Rect(cx - 400 * k, cy + 16 * k, 800 * k, 34 * k), lbl, sLabel, new Color(1f, 1f, 1f, 0.9f));
            }
        }
        if (state == State.Desk) Shadow(new Rect(0, h - 70 * k, w, 34 * k), "LMB — come out", sSmall, new Color(1f, 1f, 1f, 0.5f));
        if (Time.time < toastUntil) Shadow(new Rect(0, h * 0.16f, w, 80 * k), toast, sLabel, new Color(1f, 1f, 1f, Mathf.Clamp01(toastUntil - Time.time)));
        GUI.color = Color.white;
    }

    void Papers(float w, float h, float k)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.82f); GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
        if (newspapers != null && newspapers.Length > 0)
        {
            var t = newspapers[paperIdx % newspapers.Length];
            float ph = h * 0.9f, pw = ph * t.width / t.height;
            if (pw > w * 0.9f) { pw = w * 0.9f; ph = pw * t.height / t.width; }
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect((w - pw) / 2f, (h - ph) / 2f - 14 * k, pw, ph), t);
        }
        GUI.color = new Color(1f, 1f, 1f, 0.6f);
        GUI.Label(new Rect(0, h - 34 * k, w, 28 * k), Loc.T("LMB — next page (" + (paperIdx + 1) + "/" + Mathf.Max(1, newspapers.Length) + ")      RMB — put them down"), sSmall);
        GUI.color = Color.white;
    }

    static void Shadow(Rect r, string s, GUIStyle st, Color c)
    {
        s = Loc.T(s);
        GUI.color = new Color(0f, 0f, 0f, c.a * 0.8f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, st);
        GUI.color = c; GUI.Label(r, s, st);
    }

    static AudioClip MakePing()
    {
        const int sr = 44100; var d = new float[(int)(sr * 0.35f)];
        for (int i = 0; i < d.Length; i++) { float t = i / (float)sr; float f = t < 0.09f ? 880f : 1320f; d[i] = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 9f) * 0.5f; }
        var c = AudioClip.Create("Ping", d.Length, 1, sr, false); c.SetData(d, 0); return c;
    }
    static AudioClip MakeClick()
    {
        const int sr = 44100; var d = new float[(int)(sr * 0.03f)]; var r = new System.Random(5);
        for (int i = 0; i < d.Length; i++) { float t = i / (float)sr; d[i] = ((float)r.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 300f) * 0.6f; }
        var c = AudioClip.Create("Key", d.Length, 1, sr, false); c.SetData(d, 0); return c;
    }
}
