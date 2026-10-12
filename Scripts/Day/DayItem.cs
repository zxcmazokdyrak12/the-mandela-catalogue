using UnityEngine;

// Something you can look at / use in the daytime house. Put it on an object with a collider (trigger is fine).
//   Look - shows its lines as subtitles and plays its clip (optionally through a phone/radio filter)
//   Exit - the way on to the next night (bedroom door, garage door): fade out, then the night starts
public class DayItem : MonoBehaviour
{
    public enum Kind { Look, Exit }
    public Kind kind = Kind.Look;
    [Tooltip("Shown under the crosshair when you point at it.")]
    public string label = "Look";
    [TextArea(1, 3)] public string[] lines;
    public float lineTime = 3.4f;

    [Header("Sound")]
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 0.8f;
    public float clipDelay;
    [Tooltip("Thin and crackly - answering machine, radio, TV speaker.")]
    public bool speakerFilter;
    public AudioClip preClip;          // e.g. the answering machine beep, plays first

    [Header("Behaviour")]
    public bool repeatable = true;
    public GameObject reveal;          // switched on when used
    public GameObject hide;            // switched off when used

    [HideInInspector] public int uses;
    public bool Usable => kind == Kind.Exit || repeatable || uses == 0;

    public void Use()
    {
        if (!Usable) return;
        uses++;
        if (reveal) reveal.SetActive(true);
        if (hide) hide.SetActive(false);
        if (IntroDirector.I && IntroDirector.I.isActiveAndEnabled) IntroDirector.I.Use(this);
        else if (DayDirector.I) DayDirector.I.Use(this);
    }
}
