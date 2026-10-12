using UnityEngine;

// Background ambience for each night, looped, faded in at the start.
// Clips are pre-made seamless loops (Assets/Audio/Ambience).
public class NightAmbience : MonoBehaviour
{
    public AudioClip night1, night2, night3;
    [Range(0f, 1f)] public float volume = 0.35f;
    public float fadeIn = 4f;
    AudioSource src;

    void Start()
    {
        int n = NightRouter.Night > 0 ? NightRouter.Night : 1;
        var clip = n == 3 ? night3 : n == 2 ? night2 : night1;
        if (!clip) return;
        src = gameObject.AddComponent<AudioSource>();
        src.clip = clip; src.loop = true; src.spatialBlend = 0f; src.playOnAwake = false;
        src.volume = 0f; src.priority = 32;
        src.Play();
    }

    void Update()
    {
        if (src && src.volume < volume) src.volume = Mathf.MoveTowards(src.volume, volume, Time.deltaTime * volume / Mathf.Max(0.1f, fadeIn));
    }
}
