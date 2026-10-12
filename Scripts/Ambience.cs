using UnityEngine;

// Night room tone (generated) - put on any object in the house.
public class Ambience : MonoBehaviour
{
    [Range(0, 1)] public float roomToneVolume = 0.25f;
    public AudioClip roomTone;

    void Start()
    {
        var a = gameObject.AddComponent<AudioSource>();
        a.clip = roomTone ? roomTone : SfxBank.RoomTone();
        a.loop = true; a.spatialBlend = 0f; a.volume = roomToneVolume;
        a.Play();
    }
}
