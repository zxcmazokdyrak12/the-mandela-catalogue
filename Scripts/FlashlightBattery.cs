using UnityEngine;

// Flashlight battery: drains while the light is on, dims as it runs low, stutters below the low threshold
// and dies at zero. A dead flashlight can be shaken (press RMB again) for a weak, short-lived flicker.
public class FlashlightBattery : MonoBehaviour
{
    [Range(0f, 1f)] public float charge = 1f;
    [Tooltip("Seconds of continuous light from a full battery")]
    public float fullDuration = 300f;
    [Range(0f, 0.5f)] public float lowThreshold = 0.2f;
    public float shakeDuration = 0.35f;

    float shakeTimer;
    public bool Empty => charge <= 0f;

    // call once per frame; returns the brightness multiplier (0 = dark)
    public float Tick(bool on, bool shakePressed, float dt)
    {
        if (on && charge > 0f) charge = Mathf.Max(0f, charge - dt / Mathf.Max(1f, fullDuration));
        if (charge <= 0f)
        {
            if (on && shakePressed) shakeTimer = shakeDuration;
            shakeTimer -= dt;
            if (!on || shakeTimer <= 0f) return 0f;
            return 0.22f * (0.5f + 0.5f * Mathf.PerlinNoise(Time.time * 30f, 7.1f));
        }
        float m = Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(0f, 0.5f, charge));
        if (charge < lowThreshold)
        {
            float n = Mathf.PerlinNoise(Time.time * 7f, 3.3f);
            float cut = Mathf.Lerp(0.5f, 0.12f, charge / lowThreshold);
            if (n < cut) m *= 0.06f;
            else m *= 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 40f, 1.7f);
        }
        return m;
    }

    public void Refill() { charge = 1f; shakeTimer = 0f; }
}
