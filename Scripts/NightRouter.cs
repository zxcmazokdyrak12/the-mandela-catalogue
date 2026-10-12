using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Three nights, three places, and the days in between. Picks the setup before anything else starts:
//   night 1 - downstairs corridor (PanicPlayer + ThreatManager)
//   day 1   - free walk, downstairs -> upstairs -> bedroom door (DayPlayer + DayDirector)
//   night 2 - upstairs bedroom   (BedroomPlayer + BedroomDirector)
//   day 2   - free walk, bedroom -> downstairs -> kitchen -> garage door
//   night 3 - garage            (GaragePlayer + GarageDirector)
[DefaultExecutionOrder(-1000)]
public class NightRouter : MonoBehaviour
{
    [Tooltip("Editor testing: force a night (0 = use the real progression).")]
    public int debugNight;
    [Tooltip("Editor testing: force a day (0 = off, 1 = the day after night 1, 2 = after night 2).")]
    public int debugDay;
    [Tooltip("Editor testing: start with the opening (PSA tape, car, Lynn's note).")]
    public bool debugIntro;

    [Header("Night 1 (corridor)")]
    public Behaviour[] night1Only;
    [Header("Night 2 (bedroom)")]
    public Behaviour[] night2Only;
    public GameObject[] night2Objects;     // turned on only for night 2 (hall light etc.)
    public Transform player;
    public Vector3 night2PlayerPos = new Vector3(-0.35f, 3.98f, -4.05f);

    [Header("Night 3 (garage)")]
    public Behaviour[] night3Only;
    public GameObject[] night3Objects;
    public Behaviour[] notNight3;         // lights of the other rooms that would eat the garage's per-object light slots
    public Vector3 night3PlayerPos = new Vector3(-3.30f, 1.62f, -3.20f);

    [Header("Day (between the nights)")]
    public GameObject dayRoot;            // everything that exists only by day: mess, things to look at, daylight fills
    public DayDirector dayDirector;
    public Behaviour[] nightOnly;         // night systems switched off by day (timer, sanity, TV, gust...)
    public GameObject[] nightObjects;     // hidden by day (hotspots, arms...)

    [Header("Intro (before night 1)")]
    public IntroDirector introDirector;
    public GameObject introRoot;          // the note on the fridge, the lamp, things to look at

    public static int Night { get; private set; }
    public static int Day { get; private set; }
    public static bool IsDay => Day > 0;
    public static bool IsIntro { get; private set; }
    public static bool InMenu = true;      // first load after launching the game = the main menu
    static bool debugUsed;                // the debug fields only apply to the first load after pressing Play
    bool comingSoon;

    void Awake()
    {
        // the debug fields are for the editor only - a build always starts from the menu
        bool dbg = Application.isEditor && !debugUsed && (debugNight > 0 || debugDay > 0 || debugIntro);
        HideWindowShadow();
        debugUsed = true;
        if (dbg) InMenu = false;
        if (dbg && debugIntro) IntroDirector.Pending = true;

        bool intro = !InMenu && IntroDirector.Pending && introDirector;
        IsIntro = intro;
        if (introRoot) introRoot.SetActive(intro);
        if (introDirector) introDirector.enabled = intro;
        if (intro)
        {
            // the opening: nothing hunts you yet - IntroDirector runs the PSA, the car and the house
            Night = 1; Day = 0; NightTimer.carryNight = 1; NightTimer.carryDay = 0;
            Off(night1Only); Off(night2Only); Off(night3Only); Off(nightOnly);
            Hide(night2Objects); Hide(night3Objects); Hide(nightObjects);
            if (dayRoot) dayRoot.SetActive(false);
            if (dayDirector) dayDirector.enabled = false;
            foreach (var b in nightOnly) if (b is NightAmbience) b.enabled = true;
            return;
        }
        if (dbg && debugDay > 0) NightTimer.carryDay = debugDay;
        else if (dbg) { NightTimer.carryDay = 0; NightTimer.carryNight = debugNight; }

        if (InMenu)
        {
            // main menu: the living room at night with nothing running - GameMenu flies the camera
            Night = 1; Day = 0; NightTimer.carryNight = 1; NightTimer.carryDay = 0;
            Off(night1Only); Off(night2Only); Off(night3Only); Off(nightOnly);
            Hide(night2Objects); Hide(night3Objects); Hide(nightObjects);
            if (dayRoot) dayRoot.SetActive(false);
            if (dayDirector) dayDirector.enabled = false;
            foreach (var b in nightOnly) if (b is NightAmbience) b.enabled = true;
            return;
        }

        Day = NightTimer.carryDay > 0 && NightTimer.carryDay <= 2 ? NightTimer.carryDay : 0;
        if (Day > 0)
        {
            Night = Day + 1;                      // the night that comes after this day
            NightTimer.carryNight = Night;
            Off(night1Only); Off(night2Only); Off(night3Only); Off(nightOnly);
            Hide(night2Objects); Hide(night3Objects); Hide(nightObjects);
            if (dayRoot) dayRoot.SetActive(true);
            if (dayDirector) dayDirector.enabled = true;
            GameSettings.Reach(GameSettings.CurrentStage());
            return;
        }
        if (dayRoot) dayRoot.SetActive(false);
        if (dayDirector) dayDirector.enabled = false;

        Night = Mathf.Max(1, NightTimer.carryNight);
        if (Night > 3) Night = 1;
        NightTimer.carryNight = Night;
        bool n2 = Night == 2;
        foreach (var b in night1Only) if (b) b.enabled = Night == 1;
        foreach (var b in night2Only) if (b) b.enabled = n2;
        foreach (var g in night2Objects) if (g) g.SetActive(n2);
        if (n2 && player) player.position = night2PlayerPos;
        bool n3 = Night == 3;
        foreach (var b in night3Only) if (b) b.enabled = n3;
        foreach (var g in night3Objects) if (g) g.SetActive(n3);
        if (n3 && player) player.position = night3PlayerPos;
        if (n3 && notNight3 != null) foreach (var b in notNight3) if (b) b.enabled = false;
        if (Night <= 3) GameSettings.Reach(GameSettings.CurrentStage());
        if (Night >= 4)
        {
            comingSoon = true;
            var t = FindFirstObjectByType<NightTimer>(); if (t) t.enabled = false;
            foreach (var b in night2Only) if (b) b.enabled = false;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
    }

    // the figure's shadow on the stairwell wall belongs to night 1 (ThreatManager turns it on when it needs it)
    static void HideWindowShadow()
    {
        var tm = FindFirstObjectByType<ThreatManager>(FindObjectsInactive.Include);
        if (tm && tm.windowShadow) tm.windowShadow.enabled = false;
        if (tm && tm.windowBacklight) tm.windowBacklight.enabled = false;
    }

    static void Off(Behaviour[] a) { if (a != null) foreach (var b in a) if (b) b.enabled = false; }
    static void Hide(GameObject[] a) { if (a != null) foreach (var g in a) if (g) g.SetActive(false); }

    void Update()
    {
        if (!comingSoon) return;
        var m = Mouse.current;
        if (m != null && m.leftButton.wasPressedThisFrame) { NightTimer.carryNight = 1; SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); }
    }

    GUIStyle st;
    void OnGUI()
    {
        if (!comingSoon) return;
        if (st == null) st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
        float k = Screen.height / 720f;
        GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        st.fontSize = Mathf.RoundToInt(46 * k); GUI.color = new Color(0.8f, 0.1f, 0.1f);
        GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, 70 * k), Loc.T("NIGHT 3"), st);
        st.fontSize = Mathf.RoundToInt(20 * k); GUI.color = new Color(0.8f, 0.8f, 0.75f);
        GUI.Label(new Rect(0, Screen.height * 0.35f + 80 * k, Screen.width, 40 * k), Loc.T("To be continued.   LMB — start over"), st);
        GUI.color = Color.white;
    }
}
