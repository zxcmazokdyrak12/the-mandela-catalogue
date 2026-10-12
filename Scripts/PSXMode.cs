using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// PS1 / low-poly retro look switch.
//  - _PSXSnap   : vertex snapping grid for the Mandela/PSX Lit shader (geometry wobble)
//  - _PSXRes... : full-screen pixelation + palette + Bayer dither (PSXPost renderer feature)
//  - in play mode also drops the real render resolution (point upscale, no MSAA) - big win on integrated GPUs
// F9 toggles it for comparison.
[ExecuteAlways]
[DefaultExecutionOrder(-200)]
public class PSXMode : MonoBehaviour
{
    public static bool On = true;

    [Header("Look")]
    public bool startOn = true;
    [Tooltip("Virtual vertical resolution (PS1 was 240).")]
    public float resolution = 240f;
    [Tooltip("Vertex snap grid, lower = more wobble.")]
    public float vertexSnap = 160f;
    [Tooltip("Colour levels per channel (PS1 = 32).")]
    public float colorLevels = 32f;
    [Range(0f, 2f)] public float dither = 1f;

    [Header("Fog")]
    public bool overrideFog = true;
    public Color fogColor = new Color(0.01f, 0.012f, 0.018f);
    public float fogDensity = 0.11f;

    [Header("Performance (play mode only)")]
    public bool lowerRenderScale = true;
    [Tooltip("Real render height = resolution * this. 2 keeps vertex wobble crisp.")]
    public float supersample = 2f;

    float savedScale = -1f; UpscalingFilterSelection savedFilter; int savedMsaa;
    bool savedFog; FogMode savedFogMode; Color savedFogColor; float savedFogDensity; bool fogSaved;

    void OnEnable()
    {
        if (Application.isPlaying) On = startOn;
        Apply();
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat("_PSXOn", 0f);
        Shader.SetGlobalFloat("_PSXSnap", 0f);
        RestoreRuntime();
    }

    void Update()
    {
        if (Application.isPlaying)
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f9Key.wasPressedThisFrame) On = !On;
        }
        else On = startOn;
        Apply();
    }

    void Apply()
    {
        Shader.SetGlobalFloat("_PSXOn", On ? 1f : 0f);
        Shader.SetGlobalFloat("_PSXSnap", On ? vertexSnap : 0f);
        Shader.SetGlobalFloat("_PSXRes", resolution);
        Shader.SetGlobalFloat("_PSXLevels", colorLevels);
        Shader.SetGlobalFloat("_PSXDither", dither);
        if (!Application.isPlaying) return;

        if (overrideFog)
        {
            if (!fogSaved) { savedFog = RenderSettings.fog; savedFogMode = RenderSettings.fogMode; savedFogColor = RenderSettings.fogColor; savedFogDensity = RenderSettings.fogDensity; fogSaved = true; }
            if (On) { RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogColor = fogColor; RenderSettings.fogDensity = fogDensity; }
            else { RenderSettings.fog = savedFog; RenderSettings.fogMode = savedFogMode; RenderSettings.fogColor = savedFogColor; RenderSettings.fogDensity = savedFogDensity; }
        }

        var a = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (!a || !lowerRenderScale) return;
        if (savedScale < 0f) { savedScale = a.renderScale; savedFilter = a.upscalingFilter; savedMsaa = a.msaaSampleCount; }
        if (On)
        {
            float target = Mathf.Clamp(resolution * supersample / Mathf.Max(1, Screen.height), 0.1f, 1f);
            if (Mathf.Abs(a.renderScale - target) > 0.01f) a.renderScale = target;
            a.upscalingFilter = UpscalingFilterSelection.Point;
            a.msaaSampleCount = 1;
        }
        else RestoreRuntime();
    }

    void RestoreRuntime()
    {
        if (fogSaved && Application.isPlaying) { RenderSettings.fog = savedFog; RenderSettings.fogMode = savedFogMode; RenderSettings.fogColor = savedFogColor; RenderSettings.fogDensity = savedFogDensity; }
        var a = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (a && savedScale > 0f) { a.renderScale = savedScale; a.upscalingFilter = savedFilter; a.msaaSampleCount = savedMsaa; }
    }

    void OnApplicationQuit() { RestoreRuntime(); }
}
