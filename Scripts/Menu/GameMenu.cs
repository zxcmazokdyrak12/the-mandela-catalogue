using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Main menu + pause menu, drawn like a VCR's on-screen display over a live, slowly pushing-in shot of the living room at night.
//   main menu (first launch / "main menu"): NEW TAPE, CONTINUE (pick a tape = a night or a day you reached), SETTINGS, CREDITS, EXIT
//   in game, ESC: RESUME, SETTINGS, MAIN MENU, EXIT
// Nights are tapes: TAPE 1 side A = night 1, side B = day 1, TAPE 2 = night 2 / day 2, TAPE 3 = the last night.
[DefaultExecutionOrder(-500)]
public class GameMenu : MonoBehaviour
{
    public static GameMenu I;
    public static bool Paused { get; private set; }

    [Header("Main menu shot")]
    public Transform player;                 // the Player (camera rig) - moved along the push-in
    public Vector3 camFrom = new Vector3(0.35f, 1.45f, -1.8f);
    public Vector3 camTo = new Vector3(0.45f, 1.42f, 2.4f);
    public Vector3 lookAt = new Vector3(-0.25f, 1.3f, 5.1f);
    public float pushTime = 75f;
    [Header("The figure in the window")]
    public Renderer windowShadow;
    public Light windowBacklight;
    public float backlightIntensity = 10f;
    [Tooltip("Pale mist behind the window so the figure reads as a silhouette (menu only)")]
    public GameObject behindWindow;
    public Transform curtainL, curtainR;
    public Vector2 figureEvery = new Vector2(14f, 26f);
    [Tooltip("Editor: keep the figure in the window (to look at it).")]
    public bool debugFigure;
    [Header("Sound")]
    public AudioClip menuMove, menuPick;
    [Range(0f, 1f)] public float uiVolume = 0.5f;
    [Header("Look")]
    public Texture2D logo;                   // the scratchy chalk title
    public Font fCond, fSerif, fWide, fMono, fBanner;

    enum Page { Main, Tapes, Settings, Credits, ConfirmNew, Pause }
    Page page = Page.Main;
    int sel;
    bool menuMode, leaving;
    float t0, tCred, shotT, figureAt, figureA, figureTarget, staticA, staticUntil;
    Material shadowMat; float backlightBase = 1f;
    AudioSource ui, hum; AudioClip blip, clunk;
    CursorLockMode prevLock; bool prevVisible;
    Texture2D noise; float nextNoise;
    GUIStyle sTitle, sItem, sSmall, sOsd, sCred;

    // ------------------------------------------------------------------ setup
    void Awake()
    {
        I = this; Paused = false; Time.timeScale = 1f; AudioListener.pause = false;
        gameObject.AddComponent<SettingsApplier>();
    }

    void Start()
    {
        menuMode = NightRouter.InMenu;
        ui = gameObject.AddComponent<AudioSource>(); ui.spatialBlend = 0f; ui.playOnAwake = false; ui.ignoreListenerPause = true;
        blip = menuMove ? menuMove : MakeBlip(); clunk = menuPick ? menuPick : MakeClunk();
        noise = new Texture2D(96, 54, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        if (behindWindow) behindWindow.SetActive(menuMode);
        if (!menuMode) return;

        t0 = Time.unscaledTime;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        if (curtainL && curtainR) { var s = curtainL.localScale; s.x *= 0.28f; curtainL.localScale = s; curtainR.localScale = s; }
        if (windowShadow) { shadowMat = windowShadow.material; windowShadow.enabled = false; }
        if (windowBacklight) { backlightBase = backlightIntensity; windowBacklight.enabled = false; }
        figureAt = Time.time + Random.Range(6f, 10f);
        hum = gameObject.AddComponent<AudioSource>(); hum.clip = MakeHum(); hum.loop = true; hum.volume = 0.05f; hum.spatialBlend = 0f; hum.Play();
        staticUntil = Time.unscaledTime + 0.6f;
        sel = GameSettings.Reached >= 0 ? 1 : 0;
        Place(0f);
    }

    // ------------------------------------------------------------------ main menu shot
    void Place(float k)
    {
        if (!player) return;
        float e = k * k * (3f - 2f * k);
        Vector3 p = Vector3.Lerp(camFrom, camTo, e);
        float tt = Time.time;
        p += new Vector3(Mathf.PerlinNoise(tt * 0.21f, 1.3f) - 0.5f, Mathf.PerlinNoise(tt * 0.17f, 7.9f) - 0.5f, 0f) * 0.05f;   // handheld drift
        player.position = p;
        var dir = lookAt - p;
        player.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(Mathf.Sin(tt * 0.4f) * 0.6f, Mathf.Sin(tt * 0.27f) * 0.8f, Mathf.Sin(tt * 0.33f) * 0.5f);
        var cam = player.GetComponentInChildren<Camera>();
        if (cam) { cam.transform.localPosition = Vector3.zero; cam.transform.localRotation = Quaternion.identity; }
    }

    void Update()
    {
        var kb = Keyboard.current; var m = Mouse.current;
        if (menuMode)
        {
            shotT += Time.deltaTime;
            float k = shotT / pushTime;
            if (k >= 1f) { shotT = 0f; k = 0f; staticUntil = Time.unscaledTime + 0.5f; }
            Place(k);
            // the figure: fades in at the window for a few seconds, then is gone
            if (Time.time > figureAt)
            {
                if (figureTarget == 0f) { figureTarget = 1f; figureAt = Time.time + Random.Range(3f, 6f); }
                else { figureTarget = 0f; figureAt = Time.time + Random.Range(figureEvery.x, figureEvery.y); }
            }
            if (debugFigure) figureTarget = 1f;
            figureA = Mathf.MoveTowards(figureA, figureTarget, Time.deltaTime * (figureTarget > 0f ? 0.35f : 1.5f));
            if (shadowMat)
            {
                shadowMat.SetColor("_BaseColor", new Color(0f, 0f, 0f, figureA * 0.9f));
                windowShadow.enabled = figureA > 0.001f;
                if (windowBacklight) { windowBacklight.enabled = figureA > 0.001f; windowBacklight.intensity = backlightBase * figureA; }
            }
            if (Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
        else if (!leaving && kb != null && kb.escapeKey.wasPressedThisFrame && CanPause())
        {
            if (!Paused) PauseGame();
            else if (page == Page.Settings) { page = Page.Pause; sel = 1; Blip(); }
            else ResumeGame();
            return;
        }
        if (!menuMode && !Paused) return;
        if (leaving) return;
        Keys(kb);
    }

    bool CanPause()
    {
        // not on the death / end screens
        var tm = FindFirstObjectByType<ThreatManager>(); if (tm && tm.isActiveAndEnabled && tm.Over) return false;
        var bd = FindFirstObjectByType<BedroomDirector>(); if (bd && bd.isActiveAndEnabled && bd.Over) return false;
        var gd = FindFirstObjectByType<GarageDirector>(); if (gd && gd.isActiveAndEnabled && gd.Over) return false;
        return true;
    }

    public void PauseGame()
    {
        if (menuMode || Paused) return;
        Paused = true; page = Page.Pause; sel = 0;
        prevLock = Cursor.lockState; prevVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        Time.timeScale = 0f; AudioListener.pause = true;
        Blip();
    }

    public void ResumeGame()
    {
        Paused = false; Time.timeScale = 1f; AudioListener.pause = false;
        Cursor.lockState = prevLock == CursorLockMode.None ? CursorLockMode.Locked : prevLock; Cursor.visible = prevVisible;
        GameSettings.Save();
    }

    // ------------------------------------------------------------------ items
    struct Item { public string text, value; public bool enabled; }

    Item[] Items()
    {
        switch (page)
        {
            case Page.Main:
                return new[] {
                    It("New tape."), It("continue", GameSettings.Reached >= 0), It("settings"), It("credits"), It("goodbye") };
            case Page.Tapes:
            {
                var a = new Item[GameSettings.StageCount + 1];
                for (int i = 0; i < GameSettings.StageCount; i++) a[i] = new Item { text = TapeName(i, i <= GameSettings.Reached), enabled = i <= GameSettings.Reached };
                a[GameSettings.StageCount] = It("BACK");
                return a;
            }
            case Page.Settings:
                return new[] {
                    Val("VOLUME", Mathf.RoundToInt(GameSettings.Volume * 100f) + "%"),
                    Val("MOUSE SENSITIVITY", GameSettings.Sensitivity.ToString("0.00")),
                    Val("BRIGHTNESS", Mathf.RoundToInt(GameSettings.Brightness * 100f) + "%"),
                    Val("SUBTITLES", Loc.T(GameSettings.Subtitles ? "ON" : "OFF")),
                    Val("RETRO FILTER", Loc.T(GameSettings.Retro ? "ON" : "OFF")),
                    Val("LANGUAGE", GameSettings.Language == 1 ? "РУССКИЙ" : "ENGLISH"),
                    It("BACK") };
            case Page.Credits: return new[] { It("BACK") };
            case Page.ConfirmNew: return new[] { It("NO"), It("YES") };
            case Page.Pause: return new[] { It("Resume."), It("settings"), It("main menu"), It("goodbye") };
        }
        return new Item[0];
    }
    static Item It(string s, bool en = true) => new Item { text = Loc.T(s), enabled = en };
    static Item Val(string s, string v) => new Item { text = Loc.T(s), value = v, enabled = true };

    public static string TapeName(int stage, bool known)
    {
        int tape = stage / 2 + 1;
        string head = Loc.T("TAPE {0}").Replace("{0}", tape.ToString());
        if (stage == 4) return head + "  —  " + (known ? Loc.T("THE LAST NIGHT") : "▒▒▒▒▒▒▒▒");
        string side = Loc.T(stage % 2 == 0 ? "SIDE A" : "SIDE B");
        string what = stage % 2 == 0 ? Loc.T("NIGHT") + " " + (stage / 2 + 1) : Loc.T("DAY") + " " + (stage / 2 + 1);
        return head + " · " + side + "  —  " + (known ? what : "▒▒▒▒▒▒▒▒");
    }

    void Keys(Keyboard kb)
    {
        if (kb == null) return;
        var items = Items();
        if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) Move(items, 1);
        if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) Move(items, -1);
        if (page == Page.Settings && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)) Change(sel, -1);
        if (page == Page.Settings && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)) Change(sel, 1);
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Pick(sel);
        if (menuMode && kb.escapeKey.wasPressedThisFrame && page != Page.Main) Back();
    }

    void Move(Item[] items, int d)
    {
        for (int n = 0; n < items.Length; n++)
        {
            sel = (sel + d + items.Length) % items.Length;
            if (items[sel].enabled) break;
        }
        Blip();
    }

    void Back()
    {
        Blip();
        if (page == Page.Settings && Paused) { page = Page.Pause; sel = 1; return; }
        int from = (int)page;
        page = Page.Main;
        sel = from == (int)Page.Tapes ? 1 : from == (int)Page.Settings ? 2 : from == (int)Page.Credits ? 3 : 0;
        GameSettings.Save();
    }

    void Change(int row, int d)
    {
        switch (row)
        {
            case 0: GameSettings.Volume = Mathf.Round((GameSettings.Volume + d * 0.05f) * 20f) / 20f; AudioListener.volume = GameSettings.Volume; break;
            case 1: GameSettings.Sensitivity = Mathf.Round((GameSettings.Sensitivity + d * 0.05f) * 20f) / 20f; break;
            case 2: GameSettings.Brightness = Mathf.Round((GameSettings.Brightness + d * 0.05f) * 20f) / 20f; break;
            case 3: GameSettings.Subtitles = !GameSettings.Subtitles; break;
            case 4: GameSettings.Retro = !GameSettings.Retro; break;
            case 5: GameSettings.Language = GameSettings.Language == 1 ? 0 : 1; break;
            default: return;
        }
        Blip();
    }

    void Pick(int i)
    {
        var items = Items();
        if (i < 0 || i >= items.Length || !items[i].enabled) return;
        switch (page)
        {
            case Page.Main:
                if (i == 0) { if (GameSettings.Reached >= 0) { page = Page.ConfirmNew; sel = 0; Blip(); } else Begin(0, true); }
                else if (i == 1) { page = Page.Tapes; sel = Mathf.Clamp(GameSettings.Last, 0, GameSettings.Reached); Blip(); }
                else if (i == 2) { page = Page.Settings; sel = 0; Blip(); }
                else if (i == 3) { page = Page.Credits; sel = 0; tCred = Time.unscaledTime; Blip(); }
                else Quit();
                break;
            case Page.Tapes:
                if (i == GameSettings.StageCount) Back(); else Begin(i, false);
                break;
            case Page.Settings:
                if (i == 6) Back(); else Change(i, 1);
                break;
            case Page.Credits: Back(); break;
            case Page.ConfirmNew:
                if (i == 1) Begin(0, true); else { page = Page.Main; sel = 0; Blip(); }
                break;
            case Page.Pause:
                if (i == 0) ResumeGame();
                else if (i == 1) { page = Page.Settings; sel = 0; Blip(); }
                else if (i == 2) StartCoroutine(ToMenu());
                else Quit();
                break;
        }
    }

    void Quit()
    {
        GameSettings.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void Begin(int stage, bool fresh)
    {
        if (leaving) return;
        if (fresh) { GameSettings.ResetProgress(); IntroDirector.Pending = true; }
        StartCoroutine(Load(stage));
    }

    IEnumerator Load(int stage)
    {
        leaving = true; Play(clunk, 0.8f);
        staticUntil = Time.unscaledTime + 10f;
        GameSettings.Save();
        yield return new WaitForSecondsRealtime(1.3f);
        NightRouter.InMenu = false;
        GameSettings.Queue(stage);
        Time.timeScale = 1f; AudioListener.pause = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    IEnumerator ToMenu()
    {
        leaving = true; Play(clunk, 0.8f);
        staticUntil = Time.unscaledTime + 10f;
        GameSettings.Save();
        yield return new WaitForSecondsRealtime(0.8f);
        Paused = false; Time.timeScale = 1f; AudioListener.pause = false;
        NightRouter.InMenu = true;
        GameHUD.HideClock = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// called by the ending: back to the main menu
    public static void BackToMenu()
    {
        NightRouter.InMenu = true;
        GameHUD.HideClock = false;
        Time.timeScale = 1f; AudioListener.pause = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ------------------------------------------------------------------ drawing
    // Style: black, grainy, a scratchy chalk logo that twitches and drops out, the menu on the left in mismatched fonts,
    // settings / tapes / credits as a typewritten list on the right.
    void Styles()
    {
        if (sTitle != null) return;
        sTitle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
        sItem = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow };
        sSmall = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, wordWrap = true, font = fMono };
        sOsd = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, wordWrap = false, font = fMono, clipping = TextClipping.Overflow };
        sCred = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = true, font = fMono };
    }

    static readonly Color Ink = new Color(0.86f, 0.86f, 0.84f);
    static readonly Color Ash = new Color(0.42f, 0.42f, 0.42f);
    static readonly Color Blood = new Color(0.88f, 0.05f, 0.04f);
    float logoNext, logoDropUntil, glitchNext; Vector2 logoOff; int glitchChar = -1; float glitchUntil2;

    void OnGUI()
    {
        if (!menuMode && !Paused && !leaving) return;
        GUI.depth = -100;
        Styles();
        float w = Screen.width, h = Screen.height, u = h / 720f, now = Time.unscaledTime;

        GUI.color = menuMode ? Color.black : new Color(0f, 0f, 0f, 0.86f);
        GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
        Grain(w, h, menuMode ? 0.045f : 0.04f);

        if (menuMode && page == Page.Main) Logo(w, h, u, now);

        // the menu on the left - always there
        DrawLeft(w, h, u, now);

        // the right side: whatever page is open
        if (page == Page.Settings || page == Page.Tapes || page == Page.ConfirmNew) DrawList(w, h, u);
        else if (page == Page.Credits) Credits(w, h, u);

        sOsd.fontSize = Mathf.RoundToInt(12 * u);
        string help = Loc.T("↑↓ / mouse — select      ENTER / LMB — confirm      ESC — back") + (page == Page.Settings ? "      " + Loc.T("←→ — change") : "");
        float hw = sOsd.CalcSize(new GUIContent(help)).x;
        GUI.color = new Color(1f, 1f, 1f, 0.22f); GUI.Label(new Rect((w - hw) / 2f, h - 22 * u, hw + 10, 18 * u), help, sOsd);

        if (leaving) Leaving(w, h, u, now);
        GUI.color = Color.white;
    }

    // ---- the scratchy title: twitches every few frames, sometimes ghosts, sometimes is just gone
    void Logo(float w, float h, float u, float now)
    {
        if (!logo) return;
        if (now > logoNext)
        {
            logoNext = now + Random.Range(0.05f, 0.11f);
            logoOff = new Vector2(Random.Range(-3f, 3f), Random.Range(-2f, 2f)) * u;
            if (Random.value < 0.035f) logoDropUntil = now + Random.Range(0.06f, 0.25f);
        }
        if (now < logoDropUntil) return;
        float lw = w * 0.64f, lh = lw * logo.height / logo.width;
        var r = new Rect(w * 0.34f + logoOff.x, h * 0.14f + logoOff.y, lw, lh);
        GUI.color = Color.black; GUI.DrawTexture(new Rect(w * 0.34f - 18 * u, h * 0.14f - 14 * u, lw + 36 * u, lh + 28 * u), Texture2D.whiteTexture);
        if ((now % 2.9f) < 0.09f) { GUI.color = new Color(1f, 1f, 1f, 0.25f); GUI.DrawTexture(new Rect(r.x + 14 * u, r.y - 4 * u, r.width, r.height), logo); }
        GUI.color = new Color(1f, 1f, 1f, 0.92f + 0.08f * Mathf.PerlinNoise(now * 9f, 0.5f));
        GUI.DrawTexture(r, logo);
        // a black band eats a slice of it now and then
        if ((now % 1.7f) < 0.06f) { GUI.color = Color.black; GUI.DrawTexture(new Rect(r.x, r.y + Random.Range(0f, lh * 0.8f), lw, Random.Range(6f, 30f) * u), Texture2D.whiteTexture); }
    }

    // ---- the left column: main menu (or the pause menu), copied from the Graveyard Shift look -
    // every item in its own typeface, some pulled wide, some crushed thin and tall, the last one cut by the box edge
    struct Look { public Font f; public int size; public Vector2 scale; public Color col; public bool spaced, bold; public float clip; }
    Look LookFor(int i, bool last)
    {
        var cream = new Color(0.94f, 0.92f, 0.83f);
        if (last) return new Look { f = fBanner, size = 34, scale = new Vector2(0.74f, 1.75f), col = Color.white, clip = 0.8f };            // "oodbye"
        switch (i)
        {
            case 0: return new Look { f = fSerif, size = 46, col = cream, spaced = true, scale = new Vector2(1.08f, 1.0f) };                    // "Endure."
            case 1: return new Look { f = fCond, size = 34, scale = new Vector2(0.78f, 1.75f), col = new Color(0.97f, 0.96f, 0.9f), bold = true }; // "make your selections"
            case 2: return new Look { f = fSerif, size = 34, scale = new Vector2(0.94f, 1.32f), col = new Color(0.5f, 0.5f, 0.48f) };           // "honor and praise"
            default: return new Look { f = fWide, size = 30, scale = new Vector2(1.3f, 0.8f), col = new Color(0.72f, 0.71f, 0.66f), spaced = true };
        }
    }

    void DrawLeft(float w, float h, float u, float now)
    {
        bool pauseList = Paused && !menuMode;
        var items = pauseList ? new[] { It("Resume."), It("settings"), It("main menu"), It("goodbye") }
                              : new[] { It("New tape."), It("continue", GameSettings.Reached >= 0), It("settings"), It("credits"), It("goodbye") };
        bool active = page == Page.Main || page == Page.Pause;
        int current = active ? sel : (page == Page.Settings ? 2 : page == Page.Tapes ? 1 : page == Page.Credits ? 3 : page == Page.ConfirmNew ? 0 : -1);
        if (pauseList && page == Page.Settings) current = 1;
        var e = Event.current;
        float row = 74 * u;
        var box = new Rect(46 * u, h * 0.27f, 350 * u, items.Length * row + 46 * u);
        GUI.color = Color.black; GUI.DrawTexture(box, Texture2D.whiteTexture);
        if (pauseList)
        {
            sItem.font = fWide; sItem.fontSize = Mathf.RoundToInt(26 * u); sItem.fontStyle = FontStyle.Normal;
            Shadow(new Rect(box.x + 24 * u, box.y - 44 * u, w, 40 * u), Spaced(Loc.T("PAUSE")), sItem, Ash);
        }
        if (now > glitchNext) { glitchNext = now + Random.Range(0.4f, 1.6f); glitchChar = Random.Range(0, 12); glitchUntil2 = now + 0.08f; }

        GUI.BeginGroup(box);
        var mp = e.mousePosition;
        for (int i = 0; i < items.Length; i++)
        {
            bool last = i == items.Length - 1;
            var lk = LookFor(i, last);
            string text = items[i].text;
            var hit = new Rect(0, 22 * u + i * row, box.width, row);
            bool over = hit.Contains(mp);
            if (over && items[i].enabled && e.type == EventType.MouseMove && (!active || sel != i)) { if (active) { sel = i; Blip(); } }
            if (over && items[i].enabled && e.type == EventType.MouseDown && e.button == 0)
            {
                if (!active) page = pauseList ? Page.Pause : Page.Main;
                sel = i; Pick(i); e.Use();
            }
            bool on = i == current;
            if (last && on && menuMode && active) { text = Loc.T("you can't."); lk = new Look { f = fSerif, size = 40, scale = new Vector2(1.1f, 1.15f), col = new Color(0.94f, 0.92f, 0.83f) }; }
            if (lk.spaced) text = Spaced(text);
            if (on && now < glitchUntil2 && glitchChar < text.Length && glitchChar >= 0 && text[glitchChar] != ' ')
                text = text.Substring(0, glitchChar) + "▒" + text.Substring(glitchChar + 1);

            sItem.font = lk.f; sItem.fontSize = Mathf.RoundToInt(lk.size * u); sItem.fontStyle = lk.bold ? FontStyle.Bold : FontStyle.Normal;
            float x0 = 24 * u;
            if (lk.clip > 0f) x0 = -sItem.CalcSize(new GUIContent(text.Substring(0, 1))).x * lk.scale.x * lk.clip;   // first letter eaten by the box edge
            // long labels (Russian) get squeezed to fit the box instead of being cut off
            float tw = sItem.CalcSize(new GUIContent(text)).x * lk.scale.x, room = box.width - x0 - 16 * u;
            if (lk.clip <= 0f && tw > room * Mathf.Min(1f, lk.scale.x)) sItem.fontSize = Mathf.FloorToInt(sItem.fontSize * room * Mathf.Min(1f, lk.scale.x) / tw);
            var r = new Rect(x0, hit.y, 900 * u, row);
            Color c = !items[i].enabled ? new Color(0.2f, 0.2f, 0.2f) : on ? lk.col : lk.col * (last ? 0.85f : 0.68f);
            c.a = 1f;
            Vector2 j = on ? new Vector2(Random.Range(-1.2f, 1.2f), Random.Range(-1f, 1f)) * u : Vector2.zero;
            var m = GUI.matrix;
            GUIUtility.ScaleAroundPivot(lk.scale, new Vector2(r.x, r.center.y));
            if (last && !(on && menuMode && active)) { GUI.color = new Color(0.25f, 0.35f, 1f, 0.55f); GUI.Label(new Rect(r.x - 2 * u, r.y, r.width, r.height), text, sItem); }
            GUI.color = c; GUI.Label(new Rect(r.x + j.x, r.y + j.y, r.width, r.height), text, sItem);
            GUI.matrix = m;
        }
        GUI.EndGroup();
        sItem.fontStyle = FontStyle.Normal;
    }

    static string Spaced(string s)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++) { sb.Append(s[i]); if (i < s.Length - 1 && s[i] != ' ') sb.Append(' '); }
        return sb.ToString();
    }

    // ---- the typewritten list on the right: settings, tapes, the "are you sure"
    void DrawList(float w, float h, float u)
    {
        var items = Items(); var e = Event.current;
        float x = w * 0.46f, y = h * 0.30f, row = 36 * u, wr = w * 0.48f;
        sOsd.fontSize = Mathf.RoundToInt(22 * u);
        if (page == Page.ConfirmNew)
        {
            GUI.color = Ink; sSmall.fontSize = Mathf.RoundToInt(20 * u);
            GUI.Label(new Rect(x, y - 70 * u, wr, 60 * u), Loc.T("Start a new game? The saved progress will be lost.").ToLower(), sSmall);
        }
        if (page == Page.Tapes) { GUI.color = Ash; GUI.Label(new Rect(x, y - 44 * u, wr, row), "-- " + Loc.T("SELECT TAPE").ToLower() + " --", sOsd); }
        for (int i = 0; i < items.Length; i++)
        {
            var r = new Rect(x, y + i * row, wr, row);
            string line = ListLine(i, items[i]);
            bool over = r.Contains(e.mousePosition);
            if (over && items[i].enabled && e.type == EventType.MouseMove && sel != i) { sel = i; Blip(); }
            if (over && items[i].enabled && e.type == EventType.MouseDown && (e.button == 0 || e.button == 1))
            {
                sel = i;
                if (page == Page.Settings && i <= 2) SetFromBar(i, e.mousePosition.x, r, e.button == 1);
                else Pick(i);
                e.Use();
            }
            bool on = sel == i;
            Color c = !items[i].enabled ? new Color(0.3f, 0.3f, 0.3f) : on ? Ink : new Color(0.62f, 0.62f, 0.6f);
            if (page == Page.Settings && i == 4 && false) c = Blood;
            GUI.color = c;
            GUI.Label(r, (on ? "> " : "  ") + line, sOsd);
        }
    }

    const int BarLen = 10;
    string ListLine(int i, Item it)
    {
        if (page != Page.Settings) return it.text.ToLower();
        switch (i)
        {
            case 0: return Pad(Loc.T("VOLUME").ToLower()) + Bar(GameSettings.Volume) + "  " + Mathf.RoundToInt(GameSettings.Volume * 100f) + "%";
            case 1: return Pad(Loc.T("MOUSE SENSITIVITY").ToLower()) + Bar((GameSettings.Sensitivity - 0.25f) / 2.25f) + "  " + GameSettings.Sensitivity.ToString("0.00");
            case 2: return Pad(Loc.T("BRIGHTNESS").ToLower()) + Bar(GameSettings.Brightness) + "  " + Mathf.RoundToInt(GameSettings.Brightness * 100f) + "%";
            case 3: return (GameSettings.Subtitles ? "[x] " : "[ ] ") + Loc.T("SUBTITLES").ToLower();
            case 4: return (GameSettings.Retro ? "[x] " : "[ ] ") + Loc.T("RETRO FILTER").ToLower();
            case 5: return Loc.T("LANGUAGE").ToLower() + ":  " + (GameSettings.Language == 1 ? "< русский >" : "< english >");
            default: return it.text.ToLower();
        }
    }
    static string Pad(string s) { const int n = 22; return s.Length >= n ? s + " " : s + new string(' ', n - s.Length); }
    static string Bar(float v)
    {
        int k = Mathf.Clamp(Mathf.RoundToInt(v * BarLen), 0, BarLen);
        return "[" + new string('#', k) + new string('-', BarLen - k) + "]";
    }
    void SetFromBar(int row, float mx, Rect r, bool right)
    {
        if (right) { Change(row, -1); return; }
        // where on the bar did we click? the bar starts after the padded label
        float cw = sOsd.CalcSize(new GUIContent("M")).x;
        float bx = r.x + cw * (2 + 22 + 1), bwid = cw * BarLen;
        if (mx < bx - cw || mx > bx + bwid + cw) { Change(row, 1); return; }
        float v = Mathf.Clamp01((mx - bx) / bwid);
        if (row == 0) { GameSettings.Volume = v; AudioListener.volume = v; }
        else if (row == 1) GameSettings.Sensitivity = 0.25f + v * 2.25f;
        else GameSettings.Brightness = v;
        Blip();
    }

    void Credits(float w, float h, float u)
    {
        float x = w * 0.46f, wr = w * 0.48f;
        float y = h * 0.86f - (Time.unscaledTime - tCred) * 34f * u;
        var lines = GarageDirector.CreditLines;
        float total = 0f; for (int i = 0; i < lines.Length; i++) total += (i == 0 ? 44 : 28) * u;
        if (y + total < h * 0.1f) { tCred = Time.unscaledTime; y = h * 0.86f; }
        for (int i = 0; i < lines.Length; i++)
        {
            sCred.fontSize = Mathf.RoundToInt((i == 0 ? 26 : 16) * u);
            if (y > h * 0.1f && y < h * 0.86f)
            {
                GUI.color = i == 0 ? Blood : new Color(0.8f, 0.8f, 0.78f);
                GUI.Label(new Rect(x, y, wr, 60 * u), Loc.T(lines[i]), sCred);
            }
            y += (i == 0 ? 44 : 28) * u;
        }
        // fade the top and bottom of the roll into the black
        for (int k = 0; k < 8; k++)
        {
            GUI.color = new Color(0f, 0f, 0f, 1f - k / 8f);
            GUI.DrawTexture(new Rect(x, h * 0.1f + k * 6 * u, wr, 6 * u), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x, h * 0.86f - (k + 1) * 6 * u, wr, 6 * u), Texture2D.whiteTexture);
        }
        sOsd.fontSize = Mathf.RoundToInt(20 * u);
        var r = new Rect(x, h * 0.9f, 300 * u, 30 * u);
        var e = Event.current;
        if (r.Contains(e.mousePosition) && e.type == EventType.MouseDown && e.button == 0) { Back(); e.Use(); }
        GUI.color = Ink; GUI.Label(r, "> " + Loc.T("BACK").ToLower(), sOsd);
    }

    void Leaving(float w, float h, float u, float now)
    {
        float st = Mathf.Clamp01((now - (staticUntil - 10f)) / 0.6f);
        GUI.color = new Color(0f, 0f, 0f, st * 0.9f); GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
        Grain(w, h, 0.05f + st * 0.5f);
        sOsd.fontSize = Mathf.RoundToInt(22 * u);
        string s = Loc.T("LOADING TAPE").ToLower() + ((int)(now * 3f) % 2 == 0 ? " _" : "");
        float sw = sOsd.CalcSize(new GUIContent(s)).x;
        GUI.color = Ink; GUI.Label(new Rect((w - sw) / 2f, h * 0.47f, sw + 20, 40 * u), s, sOsd);
    }

    void Grain(float w, float h, float a)
    {
        if (Time.unscaledTime > nextNoise)
        {
            nextNoise = Time.unscaledTime + 0.05f;
            var px = noise.GetPixels32();
            for (int i = 0; i < px.Length; i++) { byte v = (byte)(Random.value < 0.5f ? 0 : Random.Range(60, 255)); px[i] = new Color32(v, v, v, 255); }
            noise.SetPixels32(px); noise.Apply();
        }
        GUI.color = new Color(1f, 1f, 1f, a);
        GUI.DrawTexture(new Rect(0, 0, w, h), noise);
    }

    static void Shadow(Rect r, string s, GUIStyle st, Color c)
    {
        GUI.color = c; GUI.Label(r, s, st);
    }

    // ------------------------------------------------------------------ sounds
    void Blip() => Play(blip, 0.5f);
    void Play(AudioClip c, float v) { if (c && ui) ui.PlayOneShot(c, v * uiVolume * 2f); }

    static AudioClip MakeBlip()
    {
        int rate = 22050, n = rate / 25; var d = new float[n];
        for (int i = 0; i < n; i++) d[i] = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 1320f * i / rate)) * 0.15f * (1f - (float)i / n);
        var c = AudioClip.Create("menu_blip", n, 1, rate, false); c.SetData(d, 0); return c;
    }

    static AudioClip MakeClunk()
    {
        int rate = 22050, n = (int)(rate * 0.9f); var d = new float[n]; var rng = new System.Random(3); float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float thump = Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 18f) * 0.8f;
            float clack = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 40f) * 0.5f;
            float motor = (float)(rng.NextDouble() * 2 - 1); lp += (motor - lp) * 0.05f;
            float whirr = t > 0.15f ? lp * 0.5f * Mathf.Clamp01((t - 0.15f) * 4f) * Mathf.Clamp01((0.9f - t) * 4f) : 0f;
            d[i] = thump + clack + whirr;
        }
        var c = AudioClip.Create("tape_clunk", n, 1, rate, false); c.SetData(d, 0); return c;
    }

    static AudioClip MakeHum()
    {
        int rate = 22050, n = rate * 2; var d = new float[n];
        for (int i = 0; i < n; i++) { float t = (float)i / rate; d[i] = Mathf.Sin(2f * Mathf.PI * 60f * t) * 0.4f + Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.2f + Mathf.Sin(2f * Mathf.PI * 15734f * t) * 0.03f; }
        var c = AudioClip.Create("vcr_hum", n, 1, rate, false); c.SetData(d, 0); return c;
    }
}
