using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Mark's laptop in night 2: a small late-90s style desktop drawn with IMGUI.
// Start menu, taskbar, desktop icons and draggable windows (minimize / maximize / close):
//   DECRYPT.EXE, Messages, My Computer (explorer), Internet browser, Notepad, error boxes.
// Input is read in Tick() (from Update) against the hotspots registered by the last Draw().
public class LaptopDesktop
{
    class Win
    {
        public string id, title; public string icon;
        public Rect n;                       // normalised rect inside the desktop area
        public bool open, min, max, fixedSize;
        public float scroll, maxScroll;
        public Rect content;                 // last drawn content rect (screen)
        public string text;                  // error box message
    }

    struct Hot
    {
        public Rect r, clip; public Win win; public Action act; public string dbl; public bool menu;
    }

    readonly BedroomPlayer p;
    readonly List<Win> order = new List<Win>();
    readonly Dictionary<string, Win> wins = new Dictionary<string, Win>();
    readonly List<Hot> hots = new List<Hot>(), building = new List<Hot>();

    public bool DecryptRunning => wins["decrypt"].open;

    // power: boot screen when it is switched on, a short shutdown screen when it is switched off
    enum Mode { Desktop, Booting, ShuttingDown }
    Mode mode = Mode.Desktop; float modeT;
    public bool Ready => mode == Mode.Desktop;
    public bool ShuttingDown => mode == Mode.ShuttingDown;
    public void BeginBoot() { mode = Mode.Booting; modeT = 0f; startOpen = false; drag = null; }
    public void BeginShutdown() { mode = Mode.ShuttingDown; modeT = 0f; startOpen = false; drag = null; }
    public Action<float> Click;              // plays a click sound

    bool startOpen, unread;
    string selected, lastKey; float lastT;
    Win drag; Vector2 dragOff;
    Vector2 mouse;
    string folder = "archive";
    readonly Stack<string> folderBack = new Stack<string>();
    int noteFile;
    int page; readonly Stack<int> pageBack = new Stack<int>();
    float hexTimer; readonly string[] hex = new string[18];

    // layout (recomputed every frame)
    Rect scr, desk; float k, px;
    float ox, oy; Rect clip; bool inGroup;

    GUIStyle sText, sBold, sMono, sCenter, sTitle, sHead, sLink, sIcon;
    Font uiFont, monoFont;
    Texture2D cursorTex, circleTex;

    public LaptopDesktop(BedroomPlayer owner)
    {
        p = owner;
        Add("decrypt", "DECRYPT.EXE - mc_archive.enc", "decrypt", new Rect(0.085f, 0.04f, 0.49f, 0.82f), true);
        Add("msgs", "Messages - Unknown number", "chat", new Rect(0.59f, 0.04f, 0.385f, 0.82f), true);
        Add("explorer", "mc_archive", "folder", new Rect(0.12f, 0.10f, 0.62f, 0.74f), false);
        Add("browser", "Mandela County Gazette - Internet", "web", new Rect(0.08f, 0.03f, 0.80f, 0.90f), false);
        Add("notepad", "Notepad", "doc", new Rect(0.22f, 0.12f, 0.58f, 0.76f), false);
        var e = Add("error", "Error", "error", new Rect(0.30f, 0.32f, 0.40f, 0.30f), false); e.fixedSize = true;
        for (int i = 0; i < hex.Length; i++) hex[i] = HexLine();
    }

    Win Add(string id, string title, string icon, Rect n, bool open)
    {
        var w = new Win { id = id, title = title, icon = icon, n = n, open = open };
        wins[id] = w; order.Add(w); return w;
    }

    // ------------------------------------------------------------------ layout
    void Layout()
    {
        float w = Screen.width, h = Screen.height;
        k = h / 720f; px = Mathf.Max(1f, Mathf.Round(k));
        float sw = Mathf.Min(w * 0.88f, h * 1.6f), sh = sw / 1.6f;
        if (sh > h * 0.9f) { sh = h * 0.9f; sw = sh * 1.6f; }
        scr = new Rect((w - sw) / 2f, (h - sh) / 2f - 8f * k, sw, sh);
        desk = new Rect(scr.x, scr.y, scr.width, scr.height - 30f * k);
    }

    Rect WinRect(Win w)
    {
        if (w.max && !w.fixedSize) return desk;
        return new Rect(desk.x + w.n.x * desk.width, desk.y + w.n.y * desk.height, w.n.width * desk.width, w.n.height * desk.height);
    }

    Win Active
    {
        get { for (int i = order.Count - 1; i >= 0; i--) if (order[i].open && !order[i].min) return order[i]; return null; }
    }

    // ------------------------------------------------------------------ app actions
    public void OnEnter()
    {
        startOpen = false; drag = null;
        if (wins["msgs"].open && !wins["msgs"].min && Active == wins["msgs"]) unread = false;
    }

    public void NotifyMessage()
    {
        var m = wins["msgs"];
        if (Active != m) unread = true;
        m.scroll = 0f;          // jump back to the newest message
    }

    void Focus(Win w)
    {
        if (w == null) return;
        w.open = true; w.min = false;
        order.Remove(w); order.Add(w);
        if (w.id == "msgs") unread = false;
    }

    void Open(string id) { var w = wins[id]; if (!w.open) w.scroll = 0f; Focus(w); startOpen = false; }
    void Close(Win w) { w.open = false; w.min = false; if (drag == w) drag = null; }
    void Minimize(Win w) { w.min = true; }

    void OpenFolder(string f, bool remember = true)
    {
        if (remember && wins["explorer"].open && folder != f) folderBack.Push(folder);
        folder = f; wins["explorer"].scroll = 0f; Open("explorer");
    }

    void OpenFile(int i) { noteFile = i; wins["notepad"].title = LaptopContent.Files[i].name + " - Notepad"; wins["notepad"].scroll = 0f; Open("notepad"); }

    void Error(string title, string msg) { var e = wins["error"]; e.title = title; e.text = msg; Open("error"); }

    void Go(int pg, bool remember = true)
    {
        if (remember && pg != page) pageBack.Push(page);
        page = pg; var b = wins["browser"]; b.scroll = 0f; b.title = LaptopContent.Pages[page].title + " - Internet";
    }

    void Launch(string what)
    {
        startOpen = false;
        switch (what)
        {
            case "decrypt": Open("decrypt"); break;
            case "msgs": Open("msgs"); break;
            case "browser": if (!wins["browser"].open) Go(0, false); Open("browser"); break;
            case "computer": OpenFolder("computer"); break;
            case "archive": OpenFolder("archive"); break;
            case "bin": OpenFolder("bin"); break;
            case "shutdown": p.ShutDownLaptop(); break;
        }
    }

    // test hook: "decrypt", "msgs", "browser", "computer", "archive", "bin", "file0".."file4", "page:id", "error", "start", "min:id", "max:id"
    public void DebugOpen(string what)
    {
        if (what.StartsWith("file")) { OpenFile(int.Parse(what.Substring(4))); return; }
        if (what.StartsWith("page:")) { Launch("browser"); Go(LaptopContent.PageIndex(what.Substring(5))); return; }
        if (what.StartsWith("min:")) { Minimize(wins[what.Substring(4)]); return; }
        if (what.StartsWith("max:")) { wins[what.Substring(4)].max = true; return; }
        if (what == "error") { Error("Access denied", "Cannot open this file.\n\nIt is still encrypted."); return; }
        if (what == "start") { startOpen = true; return; }
        Launch(what);
    }

    // ------------------------------------------------------------------ input (Update)
    public void Tick(Mouse m)
    {
        Layout();
        Vector2 mp = m.position.ReadValue();
        mouse = new Vector2(Mathf.Clamp(mp.x, scr.x, scr.xMax - 2f), Mathf.Clamp(Screen.height - mp.y, scr.y, scr.yMax - 2f));
        modeT += Time.deltaTime;
        if (mode == Mode.Booting && modeT > 2.8f) mode = Mode.Desktop;
        if (mode != Mode.Desktop) return;

        if (drag != null)
        {
            if (!m.leftButton.isPressed) drag = null;
            else
            {
                Rect r = WinRect(drag);
                Vector2 tl = mouse - dragOff;
                tl.x = Mathf.Clamp(tl.x, desk.x - r.width + 60f * k, desk.xMax - 60f * k);
                tl.y = Mathf.Clamp(tl.y, desk.y, desk.yMax - 24f * k);
                drag.n.x = (tl.x - desk.x) / desk.width; drag.n.y = (tl.y - desk.y) / desk.height;
            }
        }

        float wheel = m.scroll.ReadValue().y;
        if (Mathf.Abs(wheel) > 0.01f)
        {
            for (int i = order.Count - 1; i >= 0; i--)
            {
                var w = order[i]; if (!w.open || w.min) continue;
                if (!WinRect(w).Contains(mouse)) continue;
                float step = Mathf.Sign(wheel) * 48f * k;
                if (w.id == "msgs") w.scroll = Mathf.Clamp(w.scroll + step, 0f, w.maxScroll);
                else w.scroll = Mathf.Clamp(w.scroll - step, 0f, w.maxScroll);
                break;
            }
        }

        if (m.leftButton.wasPressedThisFrame) Press(mouse);

        if (DecryptRunning && p.decrypt < 1f && (hexTimer -= Time.deltaTime) <= 0f)
        {
            hexTimer = 0.06f;
            for (int i = 0; i < hex.Length - 1; i++) hex[i] = hex[i + 1];
            hex[hex.Length - 1] = HexLine();
        }
    }

    void Press(Vector2 pt)
    {
        Click?.Invoke(0.35f);
        for (int i = hots.Count - 1; i >= 0; i--)
        {
            var h = hots[i];
            if (!h.r.Contains(pt) || !h.clip.Contains(pt)) continue;
            if (startOpen && !h.menu) startOpen = false;
            if (h.win != null) Focus(h.win);
            if (h.dbl != null)
            {
                bool dbl = lastKey == h.dbl && Time.unscaledTime - lastT < 0.45f;
                selected = h.dbl; lastKey = h.dbl; lastT = Time.unscaledTime;
                if (dbl) { lastKey = null; h.act?.Invoke(); }
            }
            else h.act?.Invoke();
            return;
        }
        startOpen = false; selected = null;
    }

    void HotR(Rect r, Win w, Action a, string dbl = null, bool menu = false)
    {
        Rect s = new Rect(r.x, r.y, r.width, r.height);
        building.Add(new Hot { r = s, clip = inGroup ? clip : new Rect(-1e5f, -1e5f, 2e5f, 2e5f), win = w, act = a, dbl = dbl, menu = menu });
    }

    // ------------------------------------------------------------------ drawing helpers (all rects in screen space)
    void D(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(new Rect(r.x - ox, r.y - oy, r.width, r.height), Texture2D.whiteTexture); }
    void T(Rect r, Texture t, Color c) { GUI.color = c; GUI.DrawTexture(new Rect(r.x - ox, r.y - oy, r.width, r.height), t, ScaleMode.StretchToFill); }
    void L(Rect r, string s, GUIStyle st, Color c) { GUI.color = c; GUI.Label(new Rect(r.x - ox, r.y - oy, r.width, r.height), Loc.T(s), st); }

    static readonly Color Face = new Color(0.75f, 0.75f, 0.75f), Dark = new Color(0.5f, 0.5f, 0.5f), Navy = new Color(0f, 0f, 0.5f);

    void Bevel(Rect r, bool raised, Color face)
    {
        D(r, face);
        Color lt = raised ? Color.white : Dark, dk = raised ? new Color(0.25f, 0.25f, 0.25f) : Color.white;
        D(new Rect(r.x, r.y, r.width, px), lt); D(new Rect(r.x, r.y, px, r.height), lt);
        D(new Rect(r.x, r.yMax - px, r.width, px), dk); D(new Rect(r.xMax - px, r.y, px, r.height), dk);
    }

    void Sunken(Rect r, Color face)
    {
        D(r, face);
        D(new Rect(r.x, r.y, r.width, px), Dark); D(new Rect(r.x, r.y, px, r.height), Dark);
        D(new Rect(r.x, r.yMax - px, r.width, px), Color.white); D(new Rect(r.xMax - px, r.y, px, r.height), Color.white);
    }

    bool Button(Rect r, string label, Win w, Action a, bool enabled = true)
    {
        bool down = enabled && drag == null && r.Contains(mouse) && Mouse.current != null && Mouse.current.leftButton.isPressed;
        Bevel(r, !down, Face);
        sCenter.fontSize = Mathf.RoundToInt(12 * k);
        L(new Rect(r.x + (down ? px : 0), r.y + (down ? px : 0), r.width, r.height), label, sCenter, enabled ? Color.black : Dark);
        if (enabled) HotR(r, w, a);
        return down;
    }

    void Styles()
    {
        if (sText != null) return;
        try { uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Tahoma", "Verdana", "Arial" }, 12); } catch { uiFont = null; }
        try { monoFont = Font.CreateDynamicFontFromOSFont(new[] { "Courier New", "Lucida Console", "Consolas" }, 12); } catch { monoFont = null; }
        GUIStyle Mk(TextAnchor a, bool wrap, FontStyle fs, Font f)
        {
            var s = new GUIStyle(GUI.skin.label) { alignment = a, wordWrap = wrap, fontStyle = fs, richText = false, clipping = TextClipping.Clip };
            if (f) s.font = f; s.normal.textColor = Color.white; s.padding = new RectOffset(0, 0, 0, 0); s.margin = new RectOffset(0, 0, 0, 0);
            return s;
        }
        sText = Mk(TextAnchor.UpperLeft, true, FontStyle.Normal, uiFont);
        sBold = Mk(TextAnchor.MiddleLeft, false, FontStyle.Bold, uiFont);
        sMono = Mk(TextAnchor.UpperLeft, true, FontStyle.Normal, monoFont);
        sCenter = Mk(TextAnchor.MiddleCenter, false, FontStyle.Normal, uiFont);
        sTitle = Mk(TextAnchor.MiddleLeft, false, FontStyle.Bold, uiFont);
        sHead = Mk(TextAnchor.UpperLeft, true, FontStyle.Bold, null);
        sLink = Mk(TextAnchor.UpperLeft, true, FontStyle.Normal, uiFont);
        sIcon = Mk(TextAnchor.UpperCenter, true, FontStyle.Normal, uiFont);
        cursorTex = MakeCursor(); circleTex = MakeCircle();
    }

    // ------------------------------------------------------------------ icons (drawn from boxes)
    void Icon(string kind, Rect r)
    {
        float u = r.width / 16f;
        Rect R(float x, float y, float w, float h) => new Rect(r.x + x * u, r.y + y * u, w * u, h * u);
        switch (kind)
        {
            case "pc":
                D(R(1, 1, 14, 10), new Color(0.2f, 0.2f, 0.2f)); D(R(2, 2, 12, 8), Face); D(R(3, 3, 10, 6), new Color(0f, 0.5f, 0.5f));
                D(R(5, 11, 6, 2), Dark); D(R(2, 13, 12, 2), Face); D(R(2, 14.5f, 12, 0.5f), Dark); break;
            case "folder":
            case "drive":
                if (kind == "drive") { D(R(1, 5, 14, 7), Dark); D(R(1, 5, 14, 6), Face); D(R(11, 8, 2, 1), new Color(0f, 0.8f, 0f)); break; }
                D(R(1, 3, 6, 2), new Color(0.85f, 0.7f, 0.2f)); D(R(1, 4, 14, 10), new Color(0.6f, 0.5f, 0.1f)); D(R(1.5f, 5, 13, 8.5f), new Color(1f, 0.85f, 0.35f)); break;
            case "doc":
            case "lock":
            case "img":
                D(R(3, 1, 10, 14), new Color(0.3f, 0.3f, 0.3f)); D(R(3.5f, 1.5f, 9, 13), Color.white);
                if (kind == "img") { D(R(4.5f, 3, 7, 6), new Color(0.3f, 0.45f, 0.6f)); D(R(4.5f, 7, 7, 2), new Color(0.2f, 0.35f, 0.2f)); }
                else for (int i = 0; i < 5; i++) D(R(5, 3.5f + i * 2f, 6, 0.8f), new Color(0.55f, 0.55f, 0.6f));
                if (kind == "lock") { D(R(8, 8, 6, 6), new Color(0.4f, 0.3f, 0f)); D(R(8.5f, 8.5f, 5, 5), new Color(0.95f, 0.75f, 0.15f)); D(R(9.5f, 6, 3, 2.5f), new Color(0.4f, 0.3f, 0f)); D(R(10.5f, 10, 1, 2), new Color(0.3f, 0.2f, 0f)); }
                break;
            case "web":
                T(R(1, 1, 14, 14), circleTex, new Color(0.1f, 0.3f, 0.75f));
                T(R(2, 2, 12, 12), circleTex, new Color(0.35f, 0.6f, 0.95f));
                D(R(4, 4, 4, 3), new Color(0.2f, 0.65f, 0.25f)); D(R(8, 8, 4, 4), new Color(0.2f, 0.65f, 0.25f)); D(R(1, 7.5f, 14, 0.7f), new Color(1f, 1f, 1f, 0.6f)); break;
            case "chat":
                D(R(1, 2, 14, 9), new Color(0.2f, 0.2f, 0.25f)); D(R(1.5f, 2.5f, 13, 8), new Color(1f, 1f, 0.85f)); D(R(3, 11, 3, 3), new Color(0.2f, 0.2f, 0.25f));
                for (int i = 0; i < 3; i++) D(R(3.5f + i * 3.5f, 6, 2, 2), new Color(0.2f, 0.4f, 0.8f)); break;
            case "bin":
                D(R(3, 3, 10, 2), Dark); D(R(4, 5, 8, 10), new Color(0.35f, 0.35f, 0.35f)); D(R(4.5f, 5.5f, 7, 9), new Color(0.82f, 0.82f, 0.85f));
                for (int i = 0; i < 3; i++) D(R(5.5f + i * 2f, 6.5f, 0.8f, 7), Dark); break;
            case "decrypt":
                D(R(1, 2, 14, 12), new Color(0.2f, 0.2f, 0.2f)); D(R(2, 4, 12, 9), Color.black); D(R(2, 2.5f, 12, 1.2f), Navy);
                D(R(3, 6, 2, 1), new Color(0.3f, 1f, 0.4f)); D(R(5, 7, 2, 1), new Color(0.3f, 1f, 0.4f)); D(R(3, 8, 2, 1), new Color(0.3f, 1f, 0.4f)); D(R(8, 8, 4, 1), new Color(0.3f, 1f, 0.4f)); break;
            case "error":
                T(R(1, 1, 14, 14), circleTex, new Color(0.5f, 0f, 0f)); T(R(2, 2, 12, 12), circleTex, new Color(0.9f, 0.1f, 0.1f));
                sCenter.fontSize = Mathf.RoundToInt(r.width * 0.55f); L(R(0, 0, 16, 16), "X", sCenter, Color.white); break;
            case "disc":
                T(R(1, 1, 14, 14), circleTex, new Color(0.7f, 0.7f, 0.75f)); T(R(6, 6, 4, 4), circleTex, Face); break;
            case "start":
                D(R(1, 3, 6, 5), new Color(0.85f, 0.15f, 0.1f)); D(R(1, 9, 6, 5), new Color(0.1f, 0.3f, 0.85f));
                D(R(8, 2, 7, 5), new Color(0.1f, 0.6f, 0.15f)); D(R(8, 8, 7, 5), new Color(0.95f, 0.75f, 0.1f)); break;
            case "shutdown":
                D(R(2, 4, 12, 9), new Color(0.2f, 0.2f, 0.2f)); D(R(3, 5, 10, 7), new Color(0.05f, 0.05f, 0.1f)); D(R(6, 12, 4, 2), Dark); break;
        }
    }

    // ------------------------------------------------------------------ drawing (OnGUI)
    public void Draw()
    {
        Styles(); Layout();
        bool repaint = Event.current.type == EventType.Repaint;
        building.Clear();
        ox = oy = 0f; inGroup = false;

        // bezel + wallpaper
        D(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f));
        D(new Rect(scr.x - 18f * k, scr.y - 18f * k, scr.width + 36f * k, scr.height + 36f * k), new Color(0.06f, 0.06f, 0.07f));
        D(new Rect(scr.x - 3f * k, scr.y - 3f * k, scr.width + 6f * k, scr.height + 6f * k), new Color(0.12f, 0.12f, 0.13f));
        if (mode == Mode.Booting) { BootScreen(); if (repaint) hots.Clear(); GUI.color = Color.white; return; }
        D(scr, new Color(0f, 0.36f, 0.38f));
        HotR(desk, null, () => { selected = null; });

        DesktopIcons();
        foreach (var w in order) if (w.open && !w.min) DrawWin(w);
        Taskbar();
        if (startOpen) StartMenu();

        // scanline-ish dimming so it reads as a screen in a dark room
        GUI.color = new Color(0f, 0f, 0f, 0.06f);
        for (float y = scr.y; y < scr.yMax; y += 3f * k) GUI.DrawTexture(new Rect(scr.x, y, scr.width, px), Texture2D.whiteTexture);

        if (mode == Mode.ShuttingDown) ShutdownScreen();
        // cursor
        if (mode == Mode.Desktop) T(new Rect(mouse.x, mouse.y, 12f * k * 1.1f, 19f * k * 1.1f), cursorTex, Color.white);
        GUI.color = Color.white;

        if (repaint) { hots.Clear(); hots.AddRange(building); }
    }

    void BootScreen()
    {
        D(scr, Color.black);
        sMono.fontSize = Mathf.RoundToInt(13 * k); sMono.wordWrap = false;
        string[] post = { "BIOS v4.51  (C) 1996", "Memory test: 65536K OK", "Detecting drives... C: 4.3GB", "Booting from C:" };
        int lines = Mathf.Min(post.Length, Mathf.FloorToInt(modeT / 0.25f));
        if (modeT < 1.2f)
        {
            for (int i = 0; i < lines; i++) L(new Rect(scr.x + 20f * k, scr.y + 20f * k + i * 18f * k, scr.width, 18f * k), post[i], sMono, new Color(0.75f, 0.75f, 0.75f));
            return;
        }
        // splash
        float a = Mathf.Clamp01((modeT - 1.2f) / 0.3f);
        Rect logo = new Rect(scr.center.x - 22f * k, scr.center.y - 60f * k, 44f * k, 44f * k);
        Icon("start", logo);
        sHead.fontSize = Mathf.RoundToInt(24 * k); sHead.alignment = TextAnchor.UpperCenter;
        L(new Rect(scr.x, scr.center.y - 6f * k, scr.width, 34f * k), "Workstation 98", sHead, new Color(1f, 1f, 1f, a));
        sHead.alignment = TextAnchor.UpperLeft;
        Rect bar = new Rect(scr.center.x - 120f * k, scr.center.y + 40f * k, 240f * k, 10f * k);
        D(bar, new Color(0.15f, 0.15f, 0.2f));
        float x = Mathf.Repeat(modeT * 1.4f, 1.3f) - 0.3f;
        D(new Rect(bar.x + bar.width * Mathf.Clamp01(x), bar.y, bar.width * 0.3f * Mathf.Clamp01(1.3f - x), bar.height), new Color(0.1f, 0.35f, 0.85f));
        sCenter.fontSize = Mathf.RoundToInt(12 * k);
        L(new Rect(scr.x, bar.yMax + 14f * k, scr.width, 18f * k), "Starting...", sCenter, new Color(0.7f, 0.7f, 0.75f, a));
    }

    void ShutdownScreen()
    {
        float a = Mathf.Clamp01(modeT / 0.25f);
        D(scr, new Color(0f, 0f, 0f, 0.55f * a));
        if (modeT > 0.6f) { D(scr, Color.black); return; }
        Rect box = new Rect(scr.center.x - 150f * k, scr.center.y - 36f * k, 300f * k, 72f * k);
        Bevel(box, true, Face);
        sBold.fontSize = Mathf.RoundToInt(13 * k);
        L(new Rect(box.x + 18f * k, box.y, box.width, box.height), "Shutting down...", sBold, Color.black);
    }

    void DesktopIcons()
    {
        string[,] items = { { "pc", "My Computer", "computer" }, { "bin", "Recycle Bin", "bin" }, { "web", "Internet", "browser" },
                            { "chat", "Messages", "msgs" }, { "decrypt", "decrypt.exe", "decrypt" }, { "folder", "mc_archive", "archive" } };
        float cell = 74f * k, isz = 32f * k;
        sIcon.fontSize = Mathf.RoundToInt(11 * k);
        for (int i = 0; i < items.GetLength(0); i++)
        {
            Rect c = new Rect(desk.x + 8f * k, desk.y + 8f * k + i * cell, 76f * k, cell - 4f * k);
            if (c.yMax > desk.yMax) break;
            string key = "icon:" + items[i, 2]; string what = items[i, 2];
            bool sel = selected == key;
            Icon(items[i, 0], new Rect(c.x + (c.width - isz) / 2f, c.y + 2f * k, isz, isz));
            if (sel) D(new Rect(c.x + (c.width - isz) / 2f, c.y + 2f * k, isz, isz), new Color(0f, 0f, 0.5f, 0.45f));
            Rect lr = new Rect(c.x, c.y + isz + 6f * k, c.width, 30f * k);
            if (sel) { float tw = Mathf.Min(c.width, sIcon.CalcSize(new GUIContent(items[i, 1])).x + 6f * k); D(new Rect(c.x + (c.width - tw) / 2f, lr.y, tw, 15f * k), Navy); }
            else L(new Rect(lr.x + px, lr.y + px, lr.width, lr.height), items[i, 1], sIcon, new Color(0f, 0f, 0f, 0.8f));
            L(lr, items[i, 1], sIcon, Color.white);
            HotR(c, null, () => Launch(what), key);
        }
    }

    void DrawWin(Win w)
    {
        Rect R = WinRect(w);
        bool active = Active == w;
        Bevel(R, true, Face);
        float th = 20f * k;
        Rect tb = new Rect(R.x + 3f * px, R.y + 3f * px, R.width - 6f * px, th);
        HotR(R, w, null);
        // gradient title bar
        Color a = active ? Navy : Dark, b = active ? new Color(0.06f, 0.52f, 0.82f) : new Color(0.75f, 0.75f, 0.75f);
        int strips = 24;
        for (int i = 0; i < strips; i++) D(new Rect(tb.x + tb.width * i / strips, tb.y, tb.width / strips + 1f, tb.height), Color.Lerp(a, b, i / (float)(strips - 1)));
        Icon(w.icon, new Rect(tb.x + 3f * k, tb.y + 2f * k, th - 4f * k, th - 4f * k));
        sTitle.fontSize = Mathf.RoundToInt(12 * k);
        L(new Rect(tb.x + th + 2f * k, tb.y, tb.width - th - 70f * k, th), w.title, sTitle, active ? Color.white : new Color(0.85f, 0.85f, 0.85f));
        HotR(tb, w, () => { if (!w.max) { drag = w; dragOff = mouse - new Vector2(R.x, R.y); } }, w.fixedSize ? null : null);
        // title buttons
        float bw = th - 4f * k, bh = th - 5f * k;
        Rect bClose = new Rect(tb.xMax - bw - 2f * k, tb.y + 2.5f * k, bw, bh);
        CapButton(bClose, "close", w, () => Close(w));
        if (!w.fixedSize)
        {
            Rect bMax = new Rect(bClose.x - bw - 2f * k, bClose.y, bw, bh);
            Rect bMin = new Rect(bMax.x - bw, bClose.y, bw, bh);
            CapButton(bMax, w.max ? "restore" : "max", w, () => { w.max = !w.max; drag = null; });
            CapButton(bMin, "min", w, () => Minimize(w));
        }
        Rect c = new Rect(R.x + 4f * px, tb.yMax + 2f * px, R.width - 8f * px, R.yMax - tb.yMax - 6f * px);
        w.content = c;
        switch (w.id)
        {
            case "decrypt": DecryptApp(w, c); break;
            case "msgs": MsgsApp(w, c); break;
            case "explorer": ExplorerApp(w, c); break;
            case "browser": BrowserApp(w, c); break;
            case "notepad": NotepadApp(w, c); break;
            case "error": ErrorApp(w, c); break;
        }
    }

    void CapButton(Rect r, string kind, Win w, Action a)
    {
        bool down = drag == null && r.Contains(mouse) && Mouse.current != null && Mouse.current.leftButton.isPressed;
        Bevel(r, !down, Face);
        float o = down ? px : 0f;
        Rect g = new Rect(r.x + r.width * 0.25f + o, r.y + r.height * 0.22f + o, r.width * 0.5f, r.height * 0.56f);
        Color ink = Color.black;
        switch (kind)
        {
            case "min": D(new Rect(g.x, g.yMax - 2f * px, g.width * 0.75f, 2f * px), ink); break;
            case "max":
                D(new Rect(g.x, g.y, g.width, 2f * px), ink); D(new Rect(g.x, g.y, px, g.height), ink);
                D(new Rect(g.xMax - px, g.y, px, g.height), ink); D(new Rect(g.x, g.yMax - px, g.width, px), ink); break;
            case "restore":
                Rect b1 = new Rect(g.x + g.width * 0.3f, g.y, g.width * 0.7f, g.height * 0.65f), b2 = new Rect(g.x, g.y + g.height * 0.35f, g.width * 0.7f, g.height * 0.65f);
                foreach (var bb in new[] { b1, b2 }) { D(new Rect(bb.x, bb.y, bb.width, 2f * px), ink); D(new Rect(bb.x, bb.y, px, bb.height), ink); D(new Rect(bb.xMax - px, bb.y, px, bb.height), ink); D(new Rect(bb.x, bb.yMax - px, bb.width, px), ink); }
                break;
            case "close":
                sCenter.fontSize = Mathf.RoundToInt(r.height * 0.85f); sCenter.fontStyle = FontStyle.Bold;
                L(new Rect(r.x + o, r.y + o - px, r.width, r.height), "x", sCenter, ink); sCenter.fontStyle = FontStyle.Normal; break;
        }
        HotR(r, w, a);
    }

    void BeginClip(Rect c) { GUI.BeginGroup(c); ox = c.x; oy = c.y; clip = c; inGroup = true; }
    void EndClip() { GUI.EndGroup(); ox = oy = 0f; inGroup = false; }

    // ---------------------------------------------------------------- DECRYPT.EXE
    void DecryptApp(Win w, Rect c)
    {
        Sunken(c, Color.black);
        Rect i = new Rect(c.x + 8f * k, c.y + 6f * k, c.width - 16f * k, c.height - 12f * k);
        sMono.fontSize = Mathf.RoundToInt(12 * k); sMono.wordWrap = false;
        L(new Rect(i.x, i.y, i.width, 16f * k), "SOURCE  C:\\Users\\Mark\\Downloads\\mc_archive.enc", sMono, new Color(0.6f, 0.8f, 0.6f));
        L(new Rect(i.x, i.y + 16f * k, i.width, 16f * k), "OUTPUT  C:\\Users\\Mark\\Downloads\\mc_archive\\", sMono, new Color(0.6f, 0.8f, 0.6f));
        float y = i.y + 40f * k, bottom = i.yMax - 62f * k;
        BeginClip(new Rect(i.x, y, i.width, bottom - y));
        if (p.decrypt < 1f)
        {
            for (int j = 0; j < hex.Length; j++) L(new Rect(i.x, y + j * 15f * k, i.width, 15f * k), hex[j], sMono, new Color(0.25f, 0.9f, 0.35f, 0.5f + 0.5f * j / hex.Length));
        }
        else
        {
            int n = LaptopContent.Files.Length;
            for (int j = 0; j < n; j++) L(new Rect(i.x, y + j * 18f * k, i.width, 16f * k), "[ OK ]  " + LaptopContent.Files[j].name, sMono, new Color(0.4f, 1f, 0.5f));
            L(new Rect(i.x, y + (n + 1) * 18f * k, i.width, 16f * k), "Decryption complete. " + n + " files written.", sMono, Color.white);
        }
        EndClip();
        // progress bar with blocks
        Rect bar = new Rect(i.x, bottom + 6f * k, i.width, 18f * k);
        Sunken(bar, Face);
        float bwid = 9f * k, gap = 2f * k; int total = Mathf.FloorToInt((bar.width - 4f * px) / (bwid + gap));
        int lit = Mathf.FloorToInt(total * p.decrypt);
        for (int j = 0; j < lit; j++) D(new Rect(bar.x + 2f * px + j * (bwid + gap), bar.y + 2f * px, bwid, bar.height - 4f * px), Navy);
        sBold.fontSize = Mathf.RoundToInt(12 * k);
        int got = p.FilesUnlocked;
        string status = p.decrypt < 1f ? (p.AtLaptop ? "Decrypting... " : "Paused - ") + Mathf.FloorToInt(p.decrypt * 100f) + "%" : "Done";
        L(new Rect(i.x, bar.yMax + 6f * k, i.width, 18f * k), status + "      Files: " + got + " / " + LaptopContent.Files.Length, sBold, new Color(0.5f, 1f, 0.55f));
        Button(new Rect(i.xMax - 110f * k, bar.yMax + 4f * k, 110f * k, 22f * k), "Open folder", w, () => OpenFolder("archive"));
    }

    // ---------------------------------------------------------------- Messages
    void MsgsApp(Win w, Rect c)
    {
        sBold.fontSize = Mathf.RoundToInt(12 * k); sText.fontSize = Mathf.RoundToInt(13 * k);
        Rect head = new Rect(c.x, c.y, c.width, 34f * k);
        D(head, Face);
        Icon("chat", new Rect(head.x + 6f * k, head.y + 5f * k, 24f * k, 24f * k));
        L(new Rect(head.x + 38f * k, head.y + 2f * k, head.width, 16f * k), "Unknown number", sBold, Color.black);
        bool gone = p.decrypt >= 1f && p.ChatShown == LaptopContent.Chat.Length;
        string st = gone ? "offline" : p.Typing ? "typing" + new string('.', 1 + (int)(Time.time * 3f) % 3) : "online";
        sText.wordWrap = false;
        L(new Rect(head.x + 38f * k, head.y + 17f * k, head.width, 16f * k), st, sText, gone ? new Color(0.5f, 0f, 0f) : new Color(0.1f, 0.4f, 0.1f));
        sText.wordWrap = true;

        Rect input = new Rect(c.x, c.yMax - 30f * k, c.width, 30f * k);
        Rect area = new Rect(c.x, head.yMax + 2f * k, c.width, input.y - head.yMax - 4f * k);
        Sunken(area, new Color(0.97f, 0.97f, 0.95f));
        Rect inner = new Rect(area.x + 2f * px, area.y + 2f * px, area.width - 4f * px, area.height - 4f * px);
        BeginClip(inner);
        float bw = inner.width * 0.8f, by = inner.yMax - 6f * k + w.scroll, contentH = 0f;
        for (int j = p.ChatShown - 1; j >= 0; j--)
        {
            var m = LaptopContent.Chat[j];
            float tH = sText.CalcHeight(new GUIContent(Loc.T(m.text)), bw - 14f * k);
            Rect b = new Rect(m.mine ? inner.xMax - bw - 6f * k : inner.x + 6f * k, by - tH - 10f * k, bw, tH + 8f * k);
            D(new Rect(b.x + px, b.y + px, b.width, b.height), new Color(0f, 0f, 0f, 0.15f));
            D(b, m.mine ? new Color(0.78f, 0.9f, 0.78f) : new Color(0.86f, 0.88f, 0.96f));
            L(new Rect(b.x + 7f * k, b.y + 4f * k, bw - 14f * k, tH + 2f * k), m.text, sText, Color.black);
            by = b.y - 6f * k; contentH += b.height + 6f * k;
        }
        EndClip();
        w.maxScroll = Mathf.Max(0f, contentH - inner.height + 10f * k);
        w.scroll = Mathf.Clamp(w.scroll, 0f, w.maxScroll);
        // reply box that cannot send
        Rect box = new Rect(input.x, input.y + 3f * k, input.width - 64f * k, 24f * k);
        Sunken(box, Color.white);
        sText.wordWrap = false;
        L(new Rect(box.x + 6f * k, box.y + 4f * k, box.width, 18f * k), gone ? "This user is no longer available." : "Type a message...", sText, Dark);
        sText.wordWrap = true;
        Button(new Rect(box.xMax + 4f * k, box.y, 60f * k, 24f * k), "Send", w, null, false);
    }

    // ---------------------------------------------------------------- My Computer / folders
    struct Item { public string icon, name, size, type; public Action open; }

    List<Item> Items()
    {
        var l = new List<Item>();
        switch (folder)
        {
            case "computer":
                l.Add(new Item { icon = "drive", name = "Local Disk (C:)", type = "Local Disk", open = () => OpenFolder("downloads") });
                l.Add(new Item { icon = "disc", name = "Compact Disc (D:)", type = "CD Drive", open = () => Error("D:\\", "D:\\ is not accessible.\n\nThe device is not ready.") });
                l.Add(new Item { icon = "folder", name = "Desktop", type = "System Folder", open = () => OpenFolder("desktop") });
                break;
            case "desktop":
                l.Add(new Item { icon = "pc", name = "My Computer", type = "Shortcut", open = () => OpenFolder("computer") });
                l.Add(new Item { icon = "web", name = "Internet", type = "Shortcut", open = () => Launch("browser") });
                l.Add(new Item { icon = "chat", name = "Messages", type = "Shortcut", open = () => Launch("msgs") });
                l.Add(new Item { icon = "decrypt", name = "decrypt.exe", size = "88 KB", type = "Application", open = () => Launch("decrypt") });
                l.Add(new Item { icon = "folder", name = "mc_archive", type = "File Folder", open = () => OpenFolder("archive") });
                break;
            case "downloads":
                l.Add(new Item { icon = "folder", name = "mc_archive", type = "File Folder", open = () => OpenFolder("archive") });
                l.Add(new Item { icon = "lock", name = "mc_archive.enc", size = "21,604 KB", type = "ENC File", open = () => Error("mc_archive.enc", "This file is encrypted.\n\nUse DECRYPT.EXE to open it.") });
                l.Add(new Item { icon = "decrypt", name = "decrypt.exe", size = "88 KB", type = "Application", open = () => Launch("decrypt") });
                break;
            case "archive":
                for (int i = 0; i < LaptopContent.Files.Length; i++)
                {
                    int idx = i;
                    if (i < p.FilesUnlocked) l.Add(new Item { icon = "doc", name = LaptopContent.Files[i].name, size = LaptopContent.Files[i].size, type = "Text Document", open = () => OpenFile(idx) });
                    else l.Add(new Item { icon = "lock", name = "file_" + (i + 1).ToString("000") + ".enc", size = "? KB", type = "ENC File", open = () => Error("Access denied", "Cannot open this file.\n\nIt is still encrypted.") });
                }
                break;
            case "bin":
                l.Add(new Item { icon = "img", name = "IMG_0312.jpg", size = "640 KB", type = "JPEG Image", open = () => Error("IMG_0312.jpg", "This item was deleted tonight at 3:12 AM.\n\nYou did not delete it.") });
                break;
        }
        return l;
    }

    string FolderPath()
    {
        switch (folder)
        {
            case "computer": return "My Computer";
            case "desktop": return "C:\\Users\\Mark\\Desktop";
            case "downloads": return "C:\\Users\\Mark\\Downloads";
            case "archive": return "C:\\Users\\Mark\\Downloads\\mc_archive";
            case "bin": return "Recycle Bin";
        }
        return "";
    }

    void ExplorerApp(Win w, Rect c)
    {
        w.title = folder == "computer" ? "My Computer" : folder == "bin" ? "Recycle Bin" : folder == "archive" ? "mc_archive" : folder == "downloads" ? "Downloads" : "Desktop";
        w.icon = folder == "computer" ? "pc" : folder == "bin" ? "bin" : "folder";
        // menu + toolbar
        sText.fontSize = Mathf.RoundToInt(12 * k); sText.wordWrap = false;
        L(new Rect(c.x + 6f * k, c.y + 2f * k, c.width, 16f * k), "File    Edit    View    Go    Help", sText, Color.black);
        float ty = c.y + 20f * k;
        Button(new Rect(c.x + 2f * k, ty, 54f * k, 22f * k), "Back", w, () => { if (folderBack.Count > 0) OpenFolder(folderBack.Pop(), false); }, folderBack.Count > 0);
        Button(new Rect(c.x + 58f * k, ty, 40f * k, 22f * k), "Up", w, () => OpenFolder(folder == "archive" ? "downloads" : "computer"), folder != "computer");
        Rect addr = new Rect(c.x + 104f * k, ty, c.width - 106f * k, 22f * k);
        Sunken(addr, Color.white);
        L(new Rect(addr.x + 6f * k, addr.y + 4f * k, addr.width, 16f * k), FolderPath(), sText, Color.black);

        Rect body = new Rect(c.x, ty + 26f * k, c.width, c.yMax - ty - 26f * k - 20f * k);
        // left pane
        float lw = Mathf.Min(150f * k, body.width * 0.3f);
        Rect left = new Rect(body.x, body.y, lw, body.height);
        Sunken(left, Color.white);
        string[,] places = { { "pc", "My Computer", "computer" }, { "folder", "Desktop", "desktop" }, { "folder", "Downloads", "downloads" }, { "folder", "mc_archive", "archive" }, { "bin", "Recycle Bin", "bin" } };
        for (int i = 0; i < places.GetLength(0); i++)
        {
            Rect r = new Rect(left.x + 4f * k, left.y + 4f * k + i * 22f * k, left.width - 8f * k, 20f * k);
            string f = places[i, 2];
            if (folder == f) D(r, Navy);
            Icon(places[i, 0], new Rect(r.x + 2f * k + (i == 3 ? 10f * k : i == 2 ? 6f * k : 0f), r.y + 2f * k, 16f * k, 16f * k));
            L(new Rect(r.x + 22f * k + (i == 3 ? 10f * k : i == 2 ? 6f * k : 0f), r.y + 2f * k, r.width, 16f * k), places[i, 1], sText, folder == f ? Color.white : Color.black);
            HotR(r, w, () => OpenFolder(f));
        }
        // right pane: details view
        Rect right = new Rect(left.xMax + 3f * k, body.y, body.xMax - left.xMax - 3f * k, body.height);
        Sunken(right, Color.white);
        float cw = right.width;
        float[] col = { 0f, cw * 0.55f, cw * 0.72f };
        Rect hdr = new Rect(right.x + 2f * px, right.y + 2f * px, right.width - 4f * px, 20f * k);
        string[] hn = { "Name", "Size", "Type" };
        for (int i = 0; i < 3; i++)
        {
            float x0 = hdr.x + col[i], x1 = i < 2 ? hdr.x + col[i + 1] : hdr.xMax;
            Bevel(new Rect(x0, hdr.y, x1 - x0, hdr.height), true, Face);
            L(new Rect(x0 + 6f * k, hdr.y + 3f * k, x1 - x0, 16f * k), hn[i], sText, Color.black);
        }
        var items = Items();
        Rect list = new Rect(right.x + 2f * px, hdr.yMax + 2f * k, right.width - 4f * px, right.yMax - hdr.yMax - 4f * k);
        BeginClip(list);
        float rh = 21f * k;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            Rect r = new Rect(list.x, list.y + i * rh - w.scroll, list.width, rh);
            string key = "item:" + folder + ":" + it.name;
            bool sel = selected == key;
            Icon(it.icon, new Rect(r.x + 4f * k, r.y + 2f * k, 17f * k, 17f * k));
            Rect nr = new Rect(r.x + 26f * k, r.y + 3f * k, col[1] - 30f * k, 16f * k);
            if (sel) D(new Rect(nr.x - 2f * k, nr.y - 1f * k, Mathf.Min(nr.width, sText.CalcSize(new GUIContent(it.name)).x + 4f * k), 17f * k), Navy);
            L(nr, it.name, sText, sel ? Color.white : Color.black);
            L(new Rect(r.x + col[1] + 6f * k, r.y + 3f * k, col[2] - col[1], 16f * k), it.size ?? "", sText, Color.black);
            L(new Rect(r.x + col[2] + 6f * k, r.y + 3f * k, cw - col[2], 16f * k), it.type ?? "", sText, Color.black);
            var open = it.open;
            HotR(r, w, open, key);
        }
        EndClip();
        w.maxScroll = Mathf.Max(0f, items.Count * rh - list.height);
        // status bar
        Rect sb = new Rect(c.x, c.yMax - 18f * k, c.width, 18f * k);
        Sunken(sb, Face);
        L(new Rect(sb.x + 6f * k, sb.y + 2f * k, sb.width, 16f * k), items.Count + " object(s)", sText, Color.black);
        sText.wordWrap = true;
    }

    // ---------------------------------------------------------------- Notepad
    void NotepadApp(Win w, Rect c)
    {
        sText.fontSize = Mathf.RoundToInt(12 * k); sText.wordWrap = false;
        L(new Rect(c.x + 6f * k, c.y + 2f * k, c.width, 16f * k), "File    Edit    Search    Help", sText, Color.black);
        sText.wordWrap = true;
        Rect area = new Rect(c.x, c.y + 20f * k, c.width, c.height - 20f * k);
        Sunken(area, Color.white);
        Rect inner = new Rect(area.x + 6f * k, area.y + 4f * k, area.width - 12f * k, area.height - 8f * k);
        sMono.fontSize = Mathf.RoundToInt(13 * k); sMono.wordWrap = true;
        string body = noteFile >= 0 && noteFile < LaptopContent.Files.Length ? LaptopContent.Files[noteFile].body : "";
        float th = sMono.CalcHeight(new GUIContent(Loc.T(body)), inner.width);
        BeginClip(inner);
        L(new Rect(inner.x, inner.y - w.scroll, inner.width, th + 4f), body, sMono, Color.black);
        EndClip();
        w.maxScroll = Mathf.Max(0f, th - inner.height + 10f * k);
    }

    // ---------------------------------------------------------------- Internet
    void BrowserApp(Win w, Rect c)
    {
        var pg = LaptopContent.Pages[page];
        sText.fontSize = Mathf.RoundToInt(12 * k); sText.wordWrap = false;
        L(new Rect(c.x + 6f * k, c.y + 2f * k, c.width, 16f * k), "File    Edit    View    Favorites    Help", sText, Color.black);
        float ty = c.y + 20f * k;
        Button(new Rect(c.x + 2f * k, ty, 54f * k, 22f * k), "Back", w, () => { if (pageBack.Count > 0) Go(pageBack.Pop(), false); }, pageBack.Count > 0);
        Button(new Rect(c.x + 58f * k, ty, 54f * k, 22f * k), "Home", w, () => Go(0));
        Button(new Rect(c.x + 114f * k, ty, 64f * k, 22f * k), "Refresh", w, () => { w.scroll = 0f; });
        L(new Rect(c.x + 184f * k, ty + 4f * k, 60f * k, 16f * k), "Address", sText, Color.black);
        Rect addr = new Rect(c.x + 236f * k, ty, c.width - 238f * k, 22f * k);
        Sunken(addr, Color.white);
        Icon("web", new Rect(addr.x + 3f * k, addr.y + 3f * k, 16f * k, 16f * k));
        L(new Rect(addr.x + 24f * k, addr.y + 4f * k, addr.width, 16f * k), pg.url, sText, Color.black);
        sText.wordWrap = true;

        Rect view = new Rect(c.x, ty + 26f * k, c.width, c.yMax - ty - 26f * k - 20f * k);
        Sunken(view, Color.white);
        Rect inner = new Rect(view.x + 14f * k, view.y + 6f * k, view.width - 28f * k, view.height - 10f * k);
        BeginClip(inner);
        float y = inner.y - w.scroll;
        // masthead
        sHead.fontSize = Mathf.RoundToInt(26 * k);
        float hh = sHead.CalcHeight(new GUIContent(Loc.T(pg.heading)), inner.width);
        L(new Rect(inner.x, y, inner.width, hh), pg.heading, sHead, new Color(0.08f, 0.08f, 0.1f));
        y += hh + 4f * k;
        D(new Rect(inner.x, y, inner.width, 2f * px), new Color(0.6f, 0.05f, 0.05f)); y += 10f * k;
        sText.fontSize = Mathf.RoundToInt(14 * k);
        float bh = sText.CalcHeight(new GUIContent(Loc.T(pg.body)), inner.width);
        L(new Rect(inner.x, y, inner.width, bh), pg.body, sText, new Color(0.1f, 0.1f, 0.1f));
        y += bh + 10f * k;
        sLink.fontSize = Mathf.RoundToInt(14 * k);
        foreach (var lk in pg.links)
        {
            Vector2 sz = sLink.CalcSize(new GUIContent(Loc.T(lk.text)));
            float lwid = Mathf.Min(sz.x, inner.width);
            Rect lr = new Rect(inner.x + 10f * k, y, lwid, sz.y);
            bool hov = lr.Contains(mouse) && inner.Contains(mouse);
            L(lr, lk.text, sLink, hov ? new Color(0.8f, 0f, 0f) : new Color(0f, 0f, 0.8f));
            D(new Rect(lr.x, lr.yMax - px, lwid, px), hov ? new Color(0.8f, 0f, 0f) : new Color(0f, 0f, 0.8f));
            string to = lk.to;
            HotR(lr, w, () => Go(LaptopContent.PageIndex(to)));
            y += sz.y + 8f * k;
        }
        if (pg.ads && p.newspapers != null && p.newspapers.Length > 0)
        {
            y += 10f * k;
            sBold.fontSize = Mathf.RoundToInt(11 * k);
            L(new Rect(inner.x, y, inner.width, 14f * k), "ADVERTISEMENTS", sBold, Dark); y += 18f * k;
            int cols = 4; float gapA = 12f * k, cwid = (inner.width - gapA * (cols - 1)) / cols;
            float rowH = 0f;
            for (int i = 0; i < p.newspapers.Length; i++)
            {
                var t = p.newspapers[i]; if (!t) continue;
                int cI = i % cols;
                if (cI == 0 && i > 0) { y += rowH + gapA; rowH = 0f; }
                float hgt = cwid * t.height / t.width;
                if (hgt > cwid * 1.3f) { hgt = cwid * 1.3f; }
                float wid = hgt * t.width / t.height;
                T(new Rect(inner.x + cI * (cwid + gapA) + (cwid - wid) / 2f, y, wid, hgt), t, Color.white);
                rowH = Mathf.Max(rowH, hgt);
            }
            y += rowH + 10f * k;
        }
        EndClip();
        w.maxScroll = Mathf.Max(0f, (y + w.scroll) - inner.yMax + 10f * k);
        w.scroll = Mathf.Clamp(w.scroll, 0f, w.maxScroll);
        Rect sb = new Rect(c.x, c.yMax - 18f * k, c.width, 18f * k);
        Sunken(sb, Face);
        sText.fontSize = Mathf.RoundToInt(12 * k); sText.wordWrap = false;
        L(new Rect(sb.x + 6f * k, sb.y + 2f * k, sb.width, 16f * k), "Done", sText, Color.black);
        L(new Rect(sb.xMax - 120f * k, sb.y + 2f * k, 120f * k, 16f * k), "Internet zone", sText, Color.black);
        sText.wordWrap = true;
    }

    // ---------------------------------------------------------------- error box
    void ErrorApp(Win w, Rect c)
    {
        Icon("error", new Rect(c.x + 14f * k, c.y + 14f * k, 32f * k, 32f * k));
        sText.fontSize = Mathf.RoundToInt(13 * k); sText.wordWrap = true;
        L(new Rect(c.x + 60f * k, c.y + 14f * k, c.width - 72f * k, c.height - 50f * k), w.text ?? "", sText, Color.black);
        Button(new Rect(c.x + (c.width - 80f * k) / 2f, c.yMax - 30f * k, 80f * k, 24f * k), "OK", w, () => Close(w));
    }

    // ---------------------------------------------------------------- taskbar + start menu
    void Taskbar()
    {
        Rect bar = new Rect(scr.x, scr.yMax - 30f * k, scr.width, 30f * k);
        D(bar, Face); D(new Rect(bar.x, bar.y, bar.width, px), Color.white);
        Rect sb = new Rect(bar.x + 3f * k, bar.y + 4f * k, 74f * k, 22f * k);
        if (startOpen) Sunken(sb, Face); else Bevel(sb, true, Face);
        Icon("start", new Rect(sb.x + 5f * k, sb.y + 3f * k, 16f * k, 16f * k));
        sBold.fontSize = Mathf.RoundToInt(13 * k);
        L(new Rect(sb.x + 25f * k, sb.y, sb.width, sb.height), "Start", sBold, Color.black);
        HotR(sb, null, () => { startOpen = !startOpen; }, null, true);

        // tray
        Rect tray = new Rect(bar.xMax - 104f * k, bar.y + 4f * k, 100f * k, 22f * k);
        Sunken(tray, Face);
        D(new Rect(tray.x + 8f * k, tray.y + 8f * k, 4f * k, 6f * k), Color.black); D(new Rect(tray.x + 12f * k, tray.y + 5f * k, 4f * k, 12f * k), Color.black);
        var nt = p.Timer;
        sCenter.fontSize = Mathf.RoundToInt(12 * k);
        L(new Rect(tray.x + 22f * k, tray.y, tray.width - 24f * k, tray.height), nt ? nt.TimeText : "", sCenter, Color.black);

        // window buttons
        var open = new List<Win>(); foreach (var w in wins.Values) if (w.open) open.Add(w);
        open.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        float x0 = sb.xMax + 6f * k, avail = tray.x - 6f * k - x0;
        float bw = open.Count > 0 ? Mathf.Min(150f * k, avail / open.Count - 3f * k) : 0f;
        var act = Active;
        sText.fontSize = Mathf.RoundToInt(12 * k); sText.wordWrap = false;
        for (int i = 0; i < open.Count; i++)
        {
            var w = open[i];
            Rect r = new Rect(x0 + i * (bw + 3f * k), bar.y + 4f * k, bw, 22f * k);
            bool on = w == act;
            bool flash = w.id == "msgs" && unread && Mathf.Repeat(Time.time, 1f) < 0.5f;
            if (on) Sunken(r, new Color(0.85f, 0.85f, 0.85f)); else Bevel(r, true, flash ? Navy : Face);
            Icon(w.icon, new Rect(r.x + 4f * k, r.y + 3f * k, 16f * k, 16f * k));
            L(new Rect(r.x + 24f * k, r.y + 4f * k, r.width - 28f * k, 16f * k), w.title, sText, flash && !on ? Color.white : Color.black);
            HotR(r, null, () => { if (w == Active) Minimize(w); else Focus(w); });
        }
        sText.wordWrap = true;
    }

    void StartMenu()
    {
        string[,] items = { { "decrypt", "Decrypt", "decrypt" }, { "chat", "Messages", "msgs" }, { "web", "Internet", "browser" },
                            { "pc", "My Computer", "computer" }, { "folder", "Documents", "archive" }, { "bin", "Recycle Bin", "bin" },
                            { "", "", "" }, { "shutdown", "Shut Down...", "shutdown" } };
        float ih = 30f * k, mw = 210f * k;
        int n = items.GetLength(0);
        float mh = 0f; for (int i = 0; i < n; i++) mh += items[i, 0] == "" ? 8f * k : ih;
        Rect m = new Rect(scr.x + 2f * k, scr.yMax - 30f * k - mh - 6f * k, mw, mh + 6f * k);
        Bevel(m, true, Face);
        HotR(m, null, null, null, true);
        // side band with rotated caption
        Rect band = new Rect(m.x + 3f * px, m.y + 3f * px, 24f * k, m.height - 6f * px);
        for (int i = 0; i < 16; i++) D(new Rect(band.x, band.y + band.height * i / 16f, band.width, band.height / 16f + 1f), Color.Lerp(Navy, new Color(0.06f, 0.52f, 0.82f), i / 15f));
        var mtx = GUI.matrix;
        Vector2 pivot = new Vector2(band.x + band.width / 2f, band.yMax - 6f * k);
        GUIUtility.RotateAroundPivot(-90f, pivot);
        sBold.fontSize = Mathf.RoundToInt(15 * k);
        L(new Rect(pivot.x, pivot.y - band.width / 2f, m.height, band.width), "Workstation 98", sBold, Color.white);
        GUI.matrix = mtx;
        float y = m.y + 3f * px;
        sText.fontSize = Mathf.RoundToInt(13 * k); sText.wordWrap = false;
        for (int i = 0; i < n; i++)
        {
            if (items[i, 0] == "") { D(new Rect(band.xMax + 4f * k, y + 3f * k, m.xMax - band.xMax - 10f * k, px), Dark); D(new Rect(band.xMax + 4f * k, y + 3f * k + px, m.xMax - band.xMax - 10f * k, px), Color.white); y += 8f * k; continue; }
            Rect r = new Rect(band.xMax + 2f * k, y, m.xMax - band.xMax - 6f * k, ih);
            bool hov = r.Contains(mouse);
            if (hov) D(r, Navy);
            Icon(items[i, 0], new Rect(r.x + 6f * k, r.y + 3f * k, 24f * k, 24f * k));
            L(new Rect(r.x + 38f * k, r.y + 8f * k, r.width, 16f * k), items[i, 1], sText, hov ? Color.white : Color.black);
            string what = items[i, 2];
            HotR(r, null, () => Launch(what), null, true);
            y += ih;
        }
        sText.wordWrap = true;
    }

    // ------------------------------------------------------------------ textures
    static Texture2D MakeCursor()
    {
        string[] rows =
        {
            "B           ", "BB          ", "BWB         ", "BWWB        ", "BWWWB       ", "BWWWWB      ", "BWWWWWB     ",
            "BWWWWWWB    ", "BWWWWWWWB   ", "BWWWWWWWWB  ", "BWWWWWBBBBB ", "BWWBWWB     ", "BWB BWWB    ", "BB  BWWB    ",
            "B    BWWB   ", "     BWWB   ", "      BWWB  ", "      BWWB  ", "       BB   ",
        };
        var t = new Texture2D(12, rows.Length, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < 12; x++)
            {
                char ch = x < rows[y].Length ? rows[y][x] : ' ';
                t.SetPixel(x, rows.Length - 1 - y, ch == 'B' ? Color.black : ch == 'W' ? Color.white : new Color(0, 0, 0, 0));
            }
        t.Apply(); return t;
    }

    static Texture2D MakeCircle()
    {
        const int s = 32; var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
            {
                float d = new Vector2(x + 0.5f - s / 2f, y + 0.5f - s / 2f).magnitude;
                t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(s / 2f - d)));
            }
        t.Apply(); return t;
    }

    static string HexLine()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(UnityEngine.Random.Range(0, 0xFFFF).ToString("X4")).Append(":  ");
        for (int i = 0; i < 12; i++) sb.Append(UnityEngine.Random.Range(0, 256).ToString("X2")).Append(' ');
        return sb.ToString();
    }
}
