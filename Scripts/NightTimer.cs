using UnityEngine;

// Night clock: 12 AM -> 6 AM. Each in-game hour lasts secondsPerHour real seconds.
// No HUD - the hour is announced with a chime and a short fading line of text.
public class NightTimer : MonoBehaviour
{
    public static int carryNight = 1;          // survives the scene reload between nights / after a death
    public static int carryDay;                 // >0: the next load is the day after that night (NightRouter / DayDirector)

    public int night = 1;
    public float secondsPerHour = 60f;
    public int hours = 6;
    public float elapsed;

    public event System.Action<int> OnHour;
    public event System.Action OnNightEnd;

    bool running = true;
    AudioSource src; AudioClip chime;
    PanicPlayer player;

    public bool Running => running;
    public int Hour => Mathf.Min(hours, (int)(elapsed / Mathf.Max(1f, secondsPerHour)));
    public float Progress => Mathf.Clamp01(elapsed / (hours * secondsPerHour));
    public string ClockText => Hour == 0 ? "12 AM" : Hour + " AM";

    void Awake() { night = Mathf.Max(1, carryNight); }

    void Start()
    {
        src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false; src.spatialBlend = 0f;
        chime = MakeChime();
        player = FindFirstObjectByType<PanicPlayer>();
        var battery = FindFirstObjectByType<FlashlightBattery>();
        if (battery) battery.Refill();
    }

    void Update()
    {
        if (!running) return;
        int before = Hour;
        elapsed += Time.deltaTime;
        int now = Hour;
        if (now == before) return;
        src.PlayOneShot(chime, now >= hours ? 0.8f : 0.45f);
        OnHour?.Invoke(now);
        if (now >= hours) { running = false; OnNightEnd?.Invoke(); }
    }

    public void Pause() { running = false; }
    public void Resume() { if (Hour < hours) running = true; }
    // "3:41 AM" - the wall clock, minutes included
    public string TimeText
    {
        get
        {
            int h = Hour; int min = h >= hours ? 0 : (int)((elapsed - h * secondsPerHour) / Mathf.Max(1f, secondsPerHour) * 60f);
            return (h == 0 ? 12 : h) + ":" + min.ToString("00") + " AM";
        }
    }
    public void SkipHour() { elapsed = (Hour + 1) * secondsPerHour - 0.05f; }

    // soft wall-clock bell: a few inharmonic partials with long decay
    static AudioClip MakeChime()
    {
        const int sr = 44100; float len = 3f;
        var d = new float[(int)(sr * len)];
        float[] f = { 523f, 1443f, 2824f, 1046f };
        float[] a = { 0.6f, 0.25f, 0.12f, 0.2f };
        float[] k = { 1.4f, 2.6f, 4f, 2f };
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)sr, v = 0f;
            for (int p = 0; p < f.Length; p++) v += a[p] * Mathf.Sin(2f * Mathf.PI * f[p] * t) * Mathf.Exp(-k[p] * t);
            d[i] = v * Mathf.Min(1f, t * 400f) * 0.6f;
        }
        var c = AudioClip.Create("Chime", d.Length, 1, sr, false);
        c.SetData(d, 0);
        return c;
    }
}
