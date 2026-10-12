using UnityEngine;

// Procedural sounds for the garage (night 3): metal bangs on the roll-up door, the chain, the fuel can,
// the door handle and bolt, the starter motor and the engine.
public static class GarageSfx
{
    const int SR = 44100;

    static AudioClip Make(string name, float[] d) { var c = AudioClip.Create(name, d.Length, 1, SR, false); c.SetData(d, 0); return c; }
    static float Noise(System.Random r) => (float)r.NextDouble() * 2f - 1f;

    // a fist on corrugated metal: a dull boom with a long rattling tail
    public static AudioClip MetalBang(int seed)
    {
        var r = new System.Random(seed); int n = (int)(SR * 1.1f); var d = new float[n];
        float lp = 0f, hp = 0f, prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float boom = Mathf.Sin(2f * Mathf.PI * (62f + 30f * Mathf.Exp(-t * 18f)) * t) * Mathf.Exp(-t * 7f);
            float x = Noise(r);
            lp += (x - lp) * 0.08f; hp = x - prev; prev = x;
            float rattle = (lp * 0.9f + hp * 0.15f) * Mathf.Exp(-t * 3.2f) * (0.6f + 0.4f * Mathf.Sin(t * 90f));
            float ring = (Mathf.Sin(2f * Mathf.PI * 431f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 977f * t)) * Mathf.Exp(-t * 9f) * 0.12f;
            d[i] = Mathf.Clamp((boom * 0.9f + rattle * 0.9f + ring) * 0.9f, -1f, 1f);
        }
        return Make("MetalBang", d);
    }

    // one tug on the chain hoist: links clattering and a clunk from the door
    public static AudioClip Chain(int seed)
    {
        var r = new System.Random(seed); int n = (int)(SR * 0.55f); var d = new float[n];
        for (int k = 0; k < 9; k++)
        {
            int at = (int)(SR * (0.02f + k * 0.035f + (float)r.NextDouble() * 0.01f));
            float f = 2200f + (float)r.NextDouble() * 1800f;
            for (int i = 0; i < (int)(SR * 0.05f) && at + i < n; i++)
            {
                float t = i / (float)SR;
                d[at + i] += Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 90f) * 0.35f + Noise(r) * Mathf.Exp(-t * 200f) * 0.25f;
            }
        }
        int c0 = (int)(SR * 0.33f);
        for (int i = 0; c0 + i < n; i++) { float t = i / (float)SR; d[c0 + i] += Mathf.Sin(2f * Mathf.PI * 90f * t) * Mathf.Exp(-t * 14f) * 0.7f + Noise(r) * Mathf.Exp(-t * 30f) * 0.2f; }
        for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(d[i], -1f, 1f);
        return Make("Chain", d);
    }

    // fuel pouring from a jerrycan: glugs
    public static AudioClip Glug()
    {
        var r = new System.Random(4); int n = SR * 2; var d = new float[n]; float lp = 0f;
        float next = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float x = Noise(r); lp += (x - lp) * 0.05f;
            float g = 0f;
            float ph = t - next;
            if (ph > 0.28f) next = t + (float)r.NextDouble() * 0.05f;
            if (ph >= 0f && ph < 0.12f) g = Mathf.Sin(2f * Mathf.PI * (180f + 400f * ph) * ph) * Mathf.Exp(-ph * 30f);
            d[i] = Mathf.Clamp(lp * 0.6f + g * 0.6f, -1f, 1f);
        }
        return Make("Glug", d);
    }

    // somebody trying the handle on the other side
    public static AudioClip Rattle()
    {
        var r = new System.Random(9); int n = (int)(SR * 0.9f); var d = new float[n];
        for (int k = 0; k < 6; k++)
        {
            int at = (int)(SR * (k * 0.13f + (float)r.NextDouble() * 0.03f));
            for (int i = 0; i < (int)(SR * 0.08f) && at + i < n; i++)
            {
                float t = i / (float)SR;
                d[at + i] += (Noise(r) * 0.4f + Mathf.Sin(2f * Mathf.PI * 1300f * t) * 0.3f) * Mathf.Exp(-t * 70f);
            }
        }
        return Make("Rattle", d);
    }

    // the bolt sliding (lock / unlock)
    public static AudioClip Bolt()
    {
        var r = new System.Random(2); int n = (int)(SR * 0.35f); var d = new float[n]; float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float x = Noise(r); lp += (x - lp) * 0.3f;
            float slide = t < 0.22f ? lp * 0.35f : 0f;
            float clack = t > 0.22f ? Mathf.Sin(2f * Mathf.PI * 1800f * t) * Mathf.Exp(-(t - 0.22f) * 80f) * 0.6f : 0f;
            d[i] = Mathf.Clamp(slide + clack, -1f, 1f);
        }
        return Make("Bolt", d);
    }

    // the starter motor turning the engine over without it catching
    public static AudioClip Crank()
    {
        var r = new System.Random(5); int n = (int)(SR * 1.3f); var d = new float[n]; float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float whine = Mathf.Sin(2f * Mathf.PI * (330f + 40f * Mathf.Sin(t * 30f)) * t) * 0.18f;
            float chug = Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * 7.5f * t)), 6f);
            float x = Noise(r); lp += (x - lp) * 0.12f;
            float env = Mathf.Clamp01(t * 12f) * Mathf.Clamp01((1.3f - t) * 6f);
            d[i] = Mathf.Clamp((whine + lp * chug * 1.1f + Mathf.Sin(2f * Mathf.PI * 60f * t) * chug * 0.5f) * env, -1f, 1f);
        }
        return Make("Crank", d);
    }

    // the engine catching and roaring
    public static AudioClip EngineStart()
    {
        var r = new System.Random(6); int n = (int)(SR * 2.2f); var d = new float[n]; float lp = 0f, ph = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float rpm = t < 0.5f ? Mathf.Lerp(8f, 45f, t / 0.5f) : Mathf.Lerp(45f, 22f, Mathf.Clamp01((t - 0.5f) / 1.2f));
            ph += rpm / SR;
            float fire = Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * ph)), 4f);
            float x = Noise(r); lp += (x - lp) * 0.1f;
            float body = Mathf.Sin(2f * Mathf.PI * ph * 0.5f) * 0.3f + Mathf.Sin(2f * Mathf.PI * ph) * 0.25f;
            d[i] = Mathf.Clamp((body + lp * fire * 1.4f) * Mathf.Clamp01(t * 10f), -1f, 1f);
        }
        return Make("EngineStart", d);
    }

    // seamless idle loop (1 s)
    public static AudioClip EngineIdle()
    {
        var r = new System.Random(7); int n = SR; var d = new float[n]; float lp = 0f;
        const float rpm = 22f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float ph = rpm * t;
            float fire = Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * ph)), 4f);
            float x = Noise(r); lp += (x - lp) * 0.1f;
            d[i] = Mathf.Clamp(Mathf.Sin(2f * Mathf.PI * ph * 0.5f) * 0.25f + Mathf.Sin(2f * Mathf.PI * ph) * 0.2f + lp * fire * 1.1f, -1f, 1f);
        }
        return Make("EngineIdle", d);
    }

    // scraping under the door (fingernails on concrete)
    public static AudioClip Scrape()
    {
        var r = new System.Random(11); int n = (int)(SR * 1.6f); var d = new float[n]; float hp = 0f, prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float x = Noise(r); hp = x - prev; prev = x;
            float env = Mathf.Pow(Mathf.Abs(Mathf.Sin(t * 5.5f)), 2f) * Mathf.Clamp01((1.6f - t) * 3f);
            d[i] = Mathf.Clamp(hp * env * 0.35f, -1f, 1f);
        }
        return Make("Scrape", d);
    }
}
