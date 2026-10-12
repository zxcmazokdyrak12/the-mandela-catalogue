using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Player settings (kept in PlayerPrefs) and the saved progress.
public static class GameSettings
{
    static bool loaded;
    static float volume = 0.8f, sensitivity = 1f, brightness = 0.5f;
    static bool subtitles = true, retro = true;
    static int language;

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        volume = PlayerPrefs.GetFloat("mc_volume", 0.8f);
        sensitivity = PlayerPrefs.GetFloat("mc_sens", 1f);
        brightness = PlayerPrefs.GetFloat("mc_bright", 0.5f);
        subtitles = PlayerPrefs.GetInt("mc_subs", 1) == 1;
        retro = PlayerPrefs.GetInt("mc_retro", 1) == 1;
        language = PlayerPrefs.GetInt("mc_lang", Application.systemLanguage == SystemLanguage.Russian ? 1 : 0);
    }

    /// master volume 0..1 (scripts that fade the listener multiply by this)
    public static float Volume { get { Load(); return volume; } set { Load(); volume = Mathf.Clamp01(value); PlayerPrefs.SetFloat("mc_volume", volume); } }
    /// mouse sensitivity multiplier 0.25..2.5
    public static float Sensitivity { get { Load(); return sensitivity; } set { Load(); sensitivity = Mathf.Clamp(value, 0.25f, 2.5f); PlayerPrefs.SetFloat("mc_sens", sensitivity); } }
    /// brightness 0..1, 0.5 = as designed
    public static float Brightness { get { Load(); return brightness; } set { Load(); brightness = Mathf.Clamp01(value); PlayerPrefs.SetFloat("mc_bright", brightness); } }
    public static bool Subtitles { get { Load(); return subtitles; } set { Load(); subtitles = value; PlayerPrefs.SetInt("mc_subs", value ? 1 : 0); } }
    public static bool Retro { get { Load(); return retro; } set { Load(); retro = value; PlayerPrefs.SetInt("mc_retro", value ? 1 : 0); PSXMode.On = value; } }
    /// 0 = English, 1 = Russian
    public static int Language { get { Load(); return language; } set { Load(); language = value; PlayerPrefs.SetInt("mc_lang", value); } }

    public static void Save() { PlayerPrefs.Save(); }

    // ------------------------------------------------------------------ progress
    // stages: 0 night 1, 1 day 1, 2 night 2, 3 day 2, 4 night 3
    public const int StageCount = 5;
    public static int Reached => PlayerPrefs.GetInt("mc_reached", -1);
    public static int Last => PlayerPrefs.GetInt("mc_last", -1);
    public static bool Finished => PlayerPrefs.GetInt("mc_done", 0) == 1;

    public static void Reach(int stage)
    {
        if (stage > Reached) PlayerPrefs.SetInt("mc_reached", stage);
        PlayerPrefs.SetInt("mc_last", stage);
        PlayerPrefs.Save();
    }
    public static void MarkFinished() { PlayerPrefs.SetInt("mc_done", 1); PlayerPrefs.Save(); }
    public static void ResetProgress() { PlayerPrefs.DeleteKey("mc_reached"); PlayerPrefs.DeleteKey("mc_last"); PlayerPrefs.DeleteKey("mc_done"); PlayerPrefs.Save(); }

    public static int StageNight(int stage) => stage switch { 0 => 1, 1 => 2, 2 => 2, 3 => 3, _ => 3 };
    public static int StageDay(int stage) => stage == 1 ? 1 : stage == 3 ? 2 : 0;
    public static int CurrentStage() => NightRouter.Day > 0 ? (NightRouter.Day == 1 ? 1 : 3) : NightRouter.Night <= 1 ? 0 : NightRouter.Night == 2 ? 2 : 4;

    /// set the carry-over statics so the next scene load starts at that stage
    public static void Queue(int stage)
    {
        NightTimer.carryDay = StageDay(stage);
        NightTimer.carryNight = StageNight(stage);
    }
}

// Applies brightness to the global volume every frame (on top of whatever the night/day set as the base).
[DefaultExecutionOrder(1000)]
public class SettingsApplier : MonoBehaviour
{
    Volume vol; ColorAdjustments ca; float basePost; bool haveBase; int frames;

    void Start()
    {
        AudioListener.volume = GameSettings.Volume;
        PSXMode.On = GameSettings.Retro;
    }

    void LateUpdate()
    {
        if (frames < 2) { frames++; return; }      // let DayDirector etc. set up the profile first
        if (!vol) { vol = FindFirstObjectByType<Volume>(); if (!vol) return; }
        if (!haveBase)
        {
            var prof = vol.profile;                // instanced copy - never touches the asset
            if (!prof.TryGet(out ca)) return;
            basePost = ca.postExposure.value; haveBase = true;
        }
        ca.postExposure.Override(basePost + (GameSettings.Brightness - 0.5f) * 2.4f);
    }
}
