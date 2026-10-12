using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Sanity and the M.A.D. broadcast.
//  - sanity drains while threats are active and recovers slowly when it is quiet
//  - every so often a M.A.D. broadcast comes: three warning tones, then a harsh drone with stuttering,
//    reversed voices. Covering your ears (LMB+RMB / wheel / Space) almost fully protects you;
//    listening drains sanity fast. At zero sanity you die.
//  - the full-screen distortion (Mandela/MadDistortion) and the audio state follow sanity and the broadcast:
//    ears covered = muffled and quiet, low sanity = ringing ears and a slightly muffled world.
public class SanitySystem : MonoBehaviour
{
    public PanicPlayer player;
    public ThreatManager threats;
    public NightTimer timer;

    [Range(0f, 1f)] public float sanity = 1f;
    [Header("Drain and recovery per second")]
    public float threatDrain = 0.005f;
    public float madDrain = 0.11f;
    public float madDrainCovered = 0.01f;
    public float recover = 0.012f;

    [Header("M.A.D. broadcast")]
    public Vector2 madInterval = new Vector2(80f, 150f);
    public float madWarning = 3.2f;
    public float madDuration = 7f;
    public AudioClip[] madVoices;

    public bool Broadcasting => phase == 2;
    public bool Warning => phase == 1;

    int phase; float phaseTimer, nextMad, earBlend, intensity, flash;
    AudioSource drone, voice, tinnitus, beeps;
    AudioClip droneClip, beepClip, ringClip;
    List<AudioClip> stutters = new List<AudioClip>();
    AudioLowPassFilter lowpass;
    float voiceTimer;

    static readonly int IntensityId = Shader.PropertyToID("_MadIntensity");
    static readonly int FlashId = Shader.PropertyToID("_MadFlash");

    void Start()
    {
        if (!player) player = FindFirstObjectByType<PanicPlayer>();
        if (!threats) threats = FindFirstObjectByType<ThreatManager>();
        if (!timer) timer = FindFirstObjectByType<NightTimer>();
        lowpass = player.cam.GetComponent<AudioLowPassFilter>();
        droneClip = MakeDrone(); beepClip = MakeBeeps(); ringClip = MakeRing();
        if (madVoices != null) for (int i = 0; i < madVoices.Length; i++) if (madVoices[i]) stutters.Add(Stutter(madVoices[i], 100 + i));
        drone = NewSource(droneClip, true); voice = NewSource(null, false); beeps = NewSource(beepClip, false);
        tinnitus = NewSource(ringClip, true); tinnitus.volume = 0f; tinnitus.Play();
        nextMad = Random.Range(madInterval.x, madInterval.y) * 0.8f;
        Shader.SetGlobalFloat(IntensityId, 0f); Shader.SetGlobalFloat(FlashId, 0f);
    }

    AudioSource NewSource(AudioClip c, bool loop)
    {
        var a = gameObject.AddComponent<AudioSource>();
        a.clip = c; a.loop = loop; a.playOnAwake = false; a.spatialBlend = 0f; a.volume = 0f;
        return a;
    }

    public void TriggerMad() { if (phase == 0) nextMad = 0f; }
    public void Spike() { flash = 1f; intensity = Mathf.Max(intensity, 0.7f); }

    void Update()
    {
        float dt = Time.deltaTime;
        bool over = threats && threats.Over;
        earBlend = Mathf.MoveTowards(earBlend, player.EarsCovered ? 1f : 0f, dt * 5f);
        var kb = Keyboard.current;
        if (kb != null && kb.f8Key.wasPressedThisFrame) TriggerMad();

        float drain = 0f;
        if (!over)
        {
            drain += threatDrain * (threats ? threats.ActiveThreats : 0);
            UpdateBroadcast(dt, ref drain);
            sanity = Mathf.Clamp01(sanity - drain * dt + (drain <= 0f ? recover * dt : 0f));
            if (sanity <= 0f && threats) threats.KillBy(player.cam.transform.position + player.cam.transform.forward * 0.6f, "mad");
        }
        else if (phase != 0) StopBroadcast();

        // visuals
        float low = Mathf.Pow(1f - sanity, 1.5f) * 0.3f;
        float target = low;
        if (phase == 1) target += 0.08f + 0.08f * Mathf.PerlinNoise(Time.time * 8f, 0f);
        if (phase == 2) target += Mathf.Lerp(0.85f, 0.28f, earBlend) * (0.85f + 0.15f * Mathf.Sin(Time.time * 7f));
        intensity = Mathf.MoveTowards(intensity, Mathf.Clamp01(target), dt * 2.5f);
        if (phase == 2 && earBlend < 0.5f && Random.value < dt * 1.6f) flash = 1f;
        flash = Mathf.MoveTowards(flash, 0f, dt * 9f);
        Shader.SetGlobalFloat(IntensityId, intensity);
        Shader.SetGlobalFloat(FlashId, flash * 0.85f);
    }

    void UpdateBroadcast(float dt, ref float drain)
    {
        switch (phase)
        {
            case 0:
                bool allowed = timer == null || timer.night > 1 || timer.Hour >= 1;
                float agg = threats ? threats.Aggression : 0.3f;
                if (allowed) nextMad -= dt * (0.6f + agg);
                if (nextMad <= 0f)
                {
                    phase = 1; phaseTimer = madWarning;
                    beeps.volume = 0.55f; beeps.Play();
                    player.Toast("COVER YOUR EARS", 2.5f);
                }
                break;
            case 1:
                if ((phaseTimer -= dt) > 0f) break;
                phase = 2; phaseTimer = madDuration; voiceTimer = 0f;
                drone.volume = 0.9f; drone.Play();
                break;
            case 2:
                drain += Mathf.Lerp(madDrain, madDrainCovered, earBlend);
                if ((voiceTimer -= dt) <= 0f && stutters.Count > 0)
                {
                    voiceTimer = Random.Range(1.1f, 2.2f);
                    voice.pitch = Random.Range(0.7f, 1.05f);
                    voice.PlayOneShot(stutters[Random.Range(0, stutters.Count)], 0.9f);
                }
                if ((phaseTimer -= dt) <= 0f) StopBroadcast();
                break;
        }
    }

    void StopBroadcast()
    {
        phase = 0;
        drone.Stop(); voice.Stop(); beeps.Stop();
        float agg = threats ? threats.Aggression : 0.3f;
        nextMad = Random.Range(Mathf.Lerp(madInterval.x, madInterval.x * 0.6f, agg), Mathf.Lerp(madInterval.y, madInterval.y * 0.6f, agg));
    }

    // runs after PanicPlayer.Update and overrides the listener state with the combined audio "snapshot"
    void LateUpdate()
    {
        float lowSan = Mathf.Pow(1f - sanity, 2f);
        float cutoff = 22000f * Mathf.Pow(650f / 22000f, earBlend);
        cutoff *= Mathf.Lerp(1f, 0.28f, lowSan);
        if (phase == 2) cutoff = Mathf.Max(cutoff, 650f);
        if (lowpass) lowpass.cutoffFrequency = cutoff;
        float vol = Mathf.Lerp(1f, 0.35f, earBlend);
        if (threats && threats.Over) vol = Mathf.Min(vol, AudioListener.volume / Mathf.Max(0.01f, GameSettings.Volume));
        AudioListener.volume = vol * GameSettings.Volume;
        tinnitus.volume = lowSan * 0.12f + (phase == 2 ? 0.05f * (1f - earBlend) : 0f);
    }

    void OnDisable() { Shader.SetGlobalFloat(IntensityId, 0f); Shader.SetGlobalFloat(FlashId, 0f); }

    // ---------------------------------------------------------------- procedural audio
    const int SR = 44100;

    static AudioClip MakeDrone()
    {
        float len = 4f; var d = new float[(int)(SR * len)];
        var rnd = new System.Random(13); float p1 = 0, p2 = 0, p3 = 0, lp = 0;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SR;
            p1 += 55f / SR; p2 += 82.7f / SR; p3 += (440f + 220f * Mathf.Sin(t * Mathf.PI * 0.5f)) / SR;
            float saw = (p1 % 1f) * 2f - 1f + ((p2 % 1f) * 2f - 1f) * 0.8f;
            float whine = Mathf.Sin(2f * Mathf.PI * p3) * 0.25f;
            float n = (float)rnd.NextDouble() * 2f - 1f; lp += (n - lp) * 0.08f;
            float chop = 0.6f + 0.4f * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 7.5f * t));
            float v = (saw * 0.5f + whine + lp * 1.4f) * chop;
            d[i] = Mathf.Clamp(v * 0.8f, -0.85f, 0.85f);
        }
        // seamless loop
        int fade = SR / 20;
        for (int i = 0; i < fade; i++) { float a = i / (float)fade; d[i] = d[i] * a + d[d.Length - fade + i] * (1f - a); }
        var c = AudioClip.Create("MadDrone", d.Length - fade, 1, SR, false);
        var o = new float[d.Length - fade]; System.Array.Copy(d, o, o.Length); c.SetData(o, 0);
        return c;
    }

    static AudioClip MakeBeeps()
    {
        float len = 3f; var d = new float[(int)(SR * len)];
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SR, local = t % 1f;
            float on = local < 0.45f ? Mathf.Min(1f, local * 200f) * Mathf.Min(1f, (0.45f - local) * 200f) : 0f;
            d[i] = (Mathf.Sin(2f * Mathf.PI * 700f * t) + Mathf.Sin(2f * Mathf.PI * 1047f * t)) * 0.35f * on;
        }
        var c = AudioClip.Create("MadBeeps", d.Length, 1, SR, false); c.SetData(d, 0); return c;
    }

    static AudioClip MakeRing()
    {
        float len = 2f; var d = new float[(int)(SR * len)];
        for (int i = 0; i < d.Length; i++) { float t = i / (float)SR; d[i] = Mathf.Sin(2f * Mathf.PI * 4200f * t) * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 0.5f * t)); }
        var c = AudioClip.Create("Tinnitus", d.Length, 1, SR, false); c.SetData(d, 0); return c;
    }

    // cassette-style mangling: chop into grains, repeat, reverse, drop out, add hiss
    static AudioClip Stutter(AudioClip src, int seed)
    {
        var raw = new float[src.samples * src.channels];
        if (!src.GetData(raw, 0)) return src;
        var mono = new float[src.samples];
        for (int i = 0; i < mono.Length; i++) { float s = 0; for (int ch = 0; ch < src.channels; ch++) s += raw[i * src.channels + ch]; mono[i] = s / src.channels; }
        var rnd = new System.Random(seed);
        var outp = new List<float>(mono.Length * 2);
        int sr = src.frequency, pos = 0;
        while (pos < mono.Length)
        {
            int g = (int)(sr * (0.05f + 0.1f * (float)rnd.NextDouble()));
            g = Mathf.Min(g, mono.Length - pos);
            double r = rnd.NextDouble();
            int repeats = r < 0.3 ? 2 + rnd.Next(3) : 1;
            bool rev = rnd.NextDouble() < 0.18;
            bool gap = rnd.NextDouble() < 0.08;
            for (int k = 0; k < repeats; k++)
                for (int i = 0; i < g; i++)
                {
                    float env = Mathf.Min(1f, Mathf.Min(i, g - i) / (sr * 0.004f));
                    float s = gap ? 0f : mono[pos + (rev ? g - 1 - i : i)] * env;
                    outp.Add(s + ((float)rnd.NextDouble() * 2f - 1f) * 0.02f);
                }
            pos += g;
        }
        var c = AudioClip.Create(src.name + "_stutter", outp.Count, 1, sr, false);
        c.SetData(outp.ToArray(), 0);
        return c;
    }
}
