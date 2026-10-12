using UnityEngine;
using System;

// Procedural sound effects, so the prototype has audio without any external files.
// Every clip can be replaced by a real recording later (PanicPlayer / TVSet have slots for them).
public static class SfxBank
{
    public const int SR = 44100;
    static System.Random rng = new System.Random(7);
    static float Rnd() { return (float)(rng.NextDouble() * 2.0 - 1.0); }

    struct Biquad
    {
        float b0, b1, b2, a1, a2, x1, x2, y1, y2;
        void Set(float nb0, float nb1, float nb2, float na0, float na1, float na2)
        { b0 = nb0 / na0; b1 = nb1 / na0; b2 = nb2 / na0; a1 = na1 / na0; a2 = na2 / na0; }
        public void BandPass(float f, float q)
        { float w = 2f * Mathf.PI * f / SR, al = Mathf.Sin(w) / (2f * q); Set(al, 0f, -al, 1f + al, -2f * Mathf.Cos(w), 1f - al); }
        public void LowPass(float f, float q)
        { float w = 2f * Mathf.PI * f / SR, al = Mathf.Sin(w) / (2f * q), c = Mathf.Cos(w); Set((1f - c) / 2f, 1f - c, (1f - c) / 2f, 1f + al, -2f * c, 1f - al); }
        public void HighPass(float f, float q)
        { float w = 2f * Mathf.PI * f / SR, al = Mathf.Sin(w) / (2f * q), c = Mathf.Cos(w); Set((1f + c) / 2f, -(1f + c), (1f + c) / 2f, 1f + al, -2f * c, 1f - al); }
        public float Run(float x)
        { float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = x; y2 = y1; y1 = y; return y; }
    }

    static AudioClip Clip(string name, float[] d, float peakTarget = 0.9f)
    {
        float peak = 0f;
        for (int i = 0; i < d.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
        if (peak > 1e-5f) for (int i = 0; i < d.Length; i++) d[i] *= peakTarget / peak;
        var c = AudioClip.Create(name, d.Length, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }

    // renders seconds+fade, then crossfades the tail into the head so the clip loops without a click
    static AudioClip Loop(string name, float seconds, float fade, Func<float, float> gen, float peakTarget = 0.9f)
    {
        int n = (int)(SR * seconds), m = (int)(SR * fade);
        var b = new float[n + m];
        for (int i = 0; i < b.Length; i++) b[i] = gen(i / (float)SR);
        var d = new float[n];
        for (int i = 0; i < n; i++) d[i] = b[i];
        for (int i = 0; i < m; i++) { float w = i / (float)m; d[i] = b[i] * w + b[n + i] * (1f - w); }
        return Clip(name, d, peakTarget);
    }

    // heavy door slam; size < 1 = lighter wooden door (wardrobe)
    public static AudioClip Slam(float size)
    {
        int n = (int)(SR * 1.3f); var d = new float[n];
        var lp = new Biquad(); lp.LowPass(900f / size, 0.7f);
        var tail = new Biquad(); tail.BandPass(160f / size, 0.8f);
        var hp = new Biquad(); hp.HighPass(2500f, 0.7f);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float thump = Mathf.Sin(2f * Mathf.PI * (48f / size) * t + 4f * Mathf.Exp(-t * 40f)) * Mathf.Exp(-t * 9f);
            float knock = Mathf.Sin(2f * Mathf.PI * (165f / size) * t) * Mathf.Exp(-t * 28f) * 0.6f;
            float crack = lp.Run(Rnd()) * Mathf.Exp(-t * 55f) * 2.2f;
            float lt = t - 0.04f;
            float latch = lt > 0f ? hp.Run(Rnd()) * Mathf.Exp(-lt * 260f) * 0.5f : 0f;
            float verb = tail.Run(Rnd()) * Mathf.Exp(-t * 4.5f) * 0.3f;
            d[i] = thump + knock + crack + latch + verb;
        }
        return Clip("sfx_slam", d);
    }

    public static AudioClip Curtain()
    {
        int n = (int)(SR * 0.65f); var d = new float[n];
        var bp = new Biquad(); var hp = new Biquad(); hp.HighPass(3000f, 0.7f);
        float[] rings = { 0.015f, 0.04f, 0.06f, 0.085f, 0.11f, 0.125f, 0.15f, 0.185f, 0.21f };
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            bp.BandPass(Mathf.Lerp(600f, 2400f, Mathf.Clamp01(t / 0.22f)), 0.9f);
            float env = Mathf.Clamp01(t / 0.025f) * Mathf.Exp(-t * 7.5f);
            float swish = bp.Run(Rnd()) * env;
            float ring = 0f;
            for (int k = 0; k < rings.Length; k++)
            {
                float rt = t - rings[k];
                if (rt > 0f && rt < 0.03f) ring += Mathf.Sin(2f * Mathf.PI * (2900f + 230f * k) * rt) * Mathf.Exp(-rt * 260f);
            }
            d[i] = swish * 1.5f + hp.Run(ring) * 0.35f;
        }
        return Clip("sfx_curtain", d);
    }

    // plug ripped out of the socket, then it hits the floor
    public static AudioClip PlugPop()
    {
        int n = (int)(SR * 0.75f); var d = new float[n];
        var hp = new Biquad(); hp.HighPass(1800f, 0.8f);
        var lp = new Biquad(); lp.LowPass(1500f, 0.8f);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float snap = hp.Run(Rnd()) * Mathf.Exp(-t * 170f) * 1.6f;
            float tick = Mathf.Sin(2f * Mathf.PI * 2100f * t) * Mathf.Exp(-t * 90f) * 0.4f;
            float buzz = t < 0.06f ? Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 50f * t)) * Mathf.Exp(-t * 70f) * 0.15f : 0f;
            float h1 = t - 0.38f, h2 = t - 0.47f;
            float floor = h1 > 0f ? (Mathf.Sin(2f * Mathf.PI * 360f * h1) * 0.5f + lp.Run(Rnd())) * Mathf.Exp(-h1 * 55f) * 0.7f : 0f;
            float bounce = h2 > 0f ? Mathf.Sin(2f * Mathf.PI * 410f * h2) * Mathf.Exp(-h2 * 85f) * 0.3f : 0f;
            d[i] = snap + tick + buzz + floor + bounce;
        }
        return Clip("sfx_plug", d);
    }

    // body landing / bumping into furniture
    public static AudioClip Thud()
    {
        int n = (int)(SR * 0.8f); var d = new float[n];
        var lp = new Biquad(); lp.LowPass(380f, 0.7f);
        var bp = new Biquad(); bp.BandPass(1300f, 0.6f);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float body = Mathf.Sin(2f * Mathf.PI * 62f * t + 2f * Mathf.Exp(-t * 25f)) * Mathf.Exp(-t * 11f);
            float rumble = lp.Run(Rnd()) * Mathf.Exp(-t * 14f) * 1.3f;
            float cloth = bp.Run(Rnd()) * Mathf.Exp(-t * 9f) * 0.25f;
            float ht = t - 0.05f;
            float hands = ht > 0f ? Rnd() * Mathf.Exp(-ht * 120f) * 0.3f : 0f;
            d[i] = body + rumble + cloth + hands;
        }
        return Clip("sfx_thud", d);
    }

    public static AudioClip Step(int seed)
    {
        var r = new System.Random(seed);
        int n = (int)(SR * 0.3f); var d = new float[n];
        var lp = new Biquad(); lp.LowPass(600f + 300f * (float)r.NextDouble(), 0.7f);
        var bp = new Biquad(); bp.BandPass(2000f + 900f * (float)r.NextDouble(), 1.3f);
        float f0 = 80f + 30f * (float)r.NextDouble();
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float nz = (float)(r.NextDouble() * 2.0 - 1.0);
            float heel = Mathf.Sin(2f * Mathf.PI * f0 * t) * Mathf.Exp(-t * 38f);
            float scuff = lp.Run(nz) * Mathf.Exp(-t * 32f) * 1.2f;
            float click = bp.Run(nz) * Mathf.Exp(-t * 75f) * 0.35f;
            float tt = t - 0.05f;
            float toe = tt > 0f ? Mathf.Sin(2f * Mathf.PI * f0 * 1.3f * tt) * Mathf.Exp(-tt * 45f) * 0.45f : 0f;
            d[i] = heel + scuff + click + toe;
        }
        return Clip("sfx_step" + seed, d);
    }

    // stick-slip hinge creak
    public static AudioClip Creak(float dur)
    {
        int n = (int)(SR * dur); var d = new float[n];
        var f1 = new Biquad(); var f2 = new Biquad(); var f3 = new Biquad();
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR, u = t / dur;
            float rate = 16f + 34f * Mathf.PerlinNoise(t * 1.3f, 0.5f) + 14f * Mathf.Sin(u * Mathf.PI);
            phase += rate / SR;
            float x = Rnd() * 0.015f;
            if (phase >= 1f) { phase -= 1f; x += 1f + 0.4f * Rnd(); }
            f1.BandPass(480f + 200f * Mathf.PerlinNoise(t * 0.8f, 2f), 16f);
            f2.BandPass(1100f + 320f * Mathf.PerlinNoise(t * 0.7f, 4f), 13f);
            f3.BandPass(2350f, 9f);
            float env = Mathf.Clamp01(u / 0.08f) * Mathf.Clamp01((1f - u) / 0.15f) * (0.55f + 0.45f * Mathf.PerlinNoise(t * 3f, 7f));
            d[i] = (f1.Run(x) + f2.Run(x) * 0.7f + f3.Run(x) * 0.25f) * env;
        }
        return Clip("sfx_creak", d);
    }

    public static AudioClip Heartbeat()
    {
        int n = (int)(SR * 0.55f); var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float a = (Mathf.Sin(2f * Mathf.PI * 48f * t) + 0.35f * Mathf.Sin(2f * Mathf.PI * 96f * t)) * t * 60f * Mathf.Exp(-t * 30f);
            float t2 = t - 0.17f;
            float b = t2 > 0f ? (Mathf.Sin(2f * Mathf.PI * 60f * t2) + 0.35f * Mathf.Sin(2f * Mathf.PI * 120f * t2)) * t2 * 60f * Mathf.Exp(-t2 * 34f) * 0.75f : 0f;
            d[i] = a + b;
        }
        return Clip("sfx_heart", d);
    }

    // CRT static hiss with line whine and mains hum (loop)
    public static AudioClip TVStatic()
    {
        var hp = new Biquad(); hp.HighPass(650f, 0.7f);
        var lp = new Biquad(); lp.LowPass(8500f, 0.7f);
        return Loop("sfx_tvstatic", 2f, 0.25f, t =>
            lp.Run(hp.Run(Rnd())) * 0.8f
            + Mathf.Sin(2f * Mathf.PI * 15734f * t) * 0.015f
            + Mathf.Sin(2f * Mathf.PI * 50f * t) * 0.02f
            + Mathf.Sin(2f * Mathf.PI * 150f * t) * 0.012f);
    }

    // quiet house at night: low rumble, wind, fridge hum (loop)
    public static AudioClip RoomTone()
    {
        var lp = new Biquad(); lp.LowPass(200f, 0.7f);
        var lp2 = new Biquad(); lp2.LowPass(90f, 0.7f);
        float br = 0f;
        return Loop("sfx_room", 6f, 1f, t =>
        {
            br = br * 0.996f + Rnd() * 0.04f;
            float wind = lp.Run(Rnd()) * (0.65f + 0.35f * Mathf.PerlinNoise(t * 0.4f, 3f));
            return lp2.Run(br) * 1.2f + wind * 1.5f + Mathf.Sin(2f * Mathf.PI * 50f * t) * 0.01f;
        }, 0.6f);
    }
}
