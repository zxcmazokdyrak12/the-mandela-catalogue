using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Night 2 - the bedroom. Something knocks on the bedroom door. Three seconds later it comes in.
// In bed or at the laptop: it gets you. Under the desk: it walks in, looks at the empty bed, stands there... and leaves.
// Come out while it is still in the room and it sees you.
public class BedroomDirector : MonoBehaviour
{
    public BedroomPlayer player;
    public NightTimer timer;
    public Transform door;                 // hinge
    public float doorOpenAngle = -100f;    // negative swings into the room
    public GameObject alternatesGroup, intruder;   // the one that comes in (groom)
    public GameObject[] hideOthers;

    [Header("Path (world, floor height)")]
    public Vector3 hallSpot = new Vector3(0.7f, 2.85f, -0.35f);
    public Vector3 doorwaySpot = new Vector3(0.7f, 2.85f, -1.55f);
    public Vector3 bedFootSpot = new Vector3(0.62f, 2.85f, -2.45f);
    public Vector3 bedLookAt = new Vector3(-0.35f, 3.4f, -3.9f);

    [Header("Timing")]
    public float graceAfterKnock = 3f;
    public float firstKnock = 22f;
    public Vector2 cooldownEasy = new Vector2(38f, 55f), cooldownHard = new Vector2(16f, 26f);
    public float walkSpeed = 0.75f, cycleSpeed = 1.15f;

    [Header("Sound")]
    public AudioClip knockClip, burstClip, creakClip, jumpscare, alertTone;
    [Tooltip("On: Knock Clip is a single knock and is played 3-4 times. Off: Knock Clip already has all the knocks in it and is played once.")]
    public bool repeatKnockClip;
    bool generatedKnock;
    public AudioClip[] doorVoices;
    public Light hallLight;

    public bool Over => over;
    public bool AltInRoom => inRoom;

    bool over, inRoom, busy; float cooldown, doorAngle;
    Quaternion doorClosed;
    float fade; Color fadeColor = Color.black; string endText, endSub; bool endDead;
    Animator anim; AltReach reach;

    void Start()
    {
        if (!player) player = FindFirstObjectByType<BedroomPlayer>();
        if (!timer) timer = FindFirstObjectByType<NightTimer>();
        if (door) doorClosed = door.rotation;
        if (!knockClip) { knockClip = SfxBank.Thud(); generatedKnock = true; }
        cooldown = firstKnock;
        if (timer) timer.OnNightEnd += () => StartCoroutine(Survived());
        if (intruder) { anim = intruder.GetComponent<Animator>(); intruder.SetActive(false); }
    }

    float Aggression => timer ? Mathf.Clamp01(0.2f + 0.14f * timer.Hour) : 0.3f;

    void Update()
    {
        if (over || !timer || !timer.Running || busy) return;
        if ((cooldown -= Time.deltaTime) > 0f) return;
        StartCoroutine(Visit());
    }

    void SetDoor(float a) { doorAngle = a; if (door) door.rotation = Quaternion.AngleAxis(a, Vector3.up) * doorClosed; }

    void Play(AudioClip c, Vector3 at, float vol, float pitch = 1f)
    {
        if (!c) return;
        var go = new GameObject("BR_Sfx"); go.transform.position = at;
        var a = go.AddComponent<AudioSource>(); a.clip = c; a.spatialBlend = 1f; a.minDistance = 1.5f; a.maxDistance = 20f; a.volume = vol; a.pitch = pitch; a.dopplerLevel = 0f;
        a.Play(); Destroy(go, c.length / pitch + 0.2f);
    }

    IEnumerator Visit()
    {
        busy = true;
        Vector3 doorPos = door ? door.position + Vector3.up * 1.2f + Vector3.left * 0.4f : hallSpot + Vector3.up;
        // sometimes a familiar voice first
        if (doorVoices != null && doorVoices.Length > 0 && Random.value < 0.45f)
        {
            var v = doorVoices[Random.Range(0, doorVoices.Length)];
            Play(v, doorPos + Vector3.forward * 0.6f, 0.9f, Random.Range(0.9f, 0.97f));
            yield return new WaitForSeconds(v.length + 0.4f);
        }
        // knock. knock. knock.
        int knocks = Random.value < 0.3f ? 4 : 3;
        if (generatedKnock || repeatKnockClip)
            for (int i = 0; i < knocks; i++) { Play(knockClip, doorPos, 1f, Random.Range(0.85f, 0.95f)); yield return new WaitForSeconds(0.36f); }
        else { Play(knockClip, doorPos, 1f); yield return new WaitForSeconds(knockClip.length); }
        for (float t = 0f; t < graceAfterKnock; t += Time.deltaTime) { if (over) yield break; yield return null; }

        // it comes in
        if (player.state == BedroomPlayer.State.Moving)
        {
            // half-way to the desk counts as not hidden - unless it is the very last step
            for (float t = 0f; t < 0.2f && player.state == BedroomPlayer.State.Moving; t += Time.deltaTime) yield return null;
        }
        Play(burstClip, doorPos, 1f);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.14f) { SetDoor(Mathf.Lerp(0f, doorOpenAngle, t)); yield return null; }
        SetDoor(doorOpenAngle);
        if (hallLight) hallLight.enabled = true;

        if (!player.Hidden) { StartCoroutine(Kill(player.AtLaptop ? "laptop" : "bed")); yield break; }
        // hidden, but the room gives you away
        if (player.LaptopOn) { StartCoroutine(Kill("screen")); yield break; }
        if (player.FlashOn) { StartCoroutine(Kill("light")); yield break; }

        // ---- search: it walks in, looks at the bed, stands... and goes
        inRoom = true; player.CanLeaveDesk = false;
        ShowIntruder(doorwaySpot, bedFootSpot - doorwaySpot);
        yield return Walk(doorwaySpot, bedFootSpot);
        if (over) yield break;
        yield return TurnTo(bedLookAt, 0.7f);
        if (over) yield break;
        anim.Play("StairIdle", 0, Random.value);
        if (reach) for (float t = 0f; t < 2.6f; t += Time.deltaTime) { reach.headTilt = Mathf.SmoothStep(0f, 28f, t / 1.6f); if (CheckBetrayal()) yield break; yield return null; }
        // ...a long look at the desk
        yield return TurnTo(player.transform.position, 1.1f);
        if (over) yield break;
        for (float t = 0f; t < 1.8f; t += Time.deltaTime) { if (reach) reach.headTilt = Mathf.Lerp(28f, -12f, t / 1.8f); if (CheckBetrayal()) yield break; yield return null; }
        if (reach) reach.headTilt = 0f;
        yield return TurnTo(doorwaySpot + Vector3.up, 0.6f);
        if (over) yield break;
        yield return Walk(bedFootSpot, hallSpot);
        if (over) yield break;
        intruder.SetActive(false);
        inRoom = false; player.CanLeaveDesk = true;
        Play(creakClip, doorPos, 0.6f);
        float a0 = doorAngle;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 1.6f) { SetDoor(Mathf.Lerp(a0, 0f, t * t)); yield return null; }
        SetDoor(0f);
        if (hallLight) hallLight.enabled = false;
        cooldown = Random.Range(Mathf.Lerp(cooldownEasy.x, cooldownHard.x, Aggression), Mathf.Lerp(cooldownEasy.y, cooldownHard.y, Aggression));
        busy = false;
    }

    bool CheckBetrayal()
    {
        if (over) return true;
        if (inRoom && !player.Hidden && player.state != BedroomPlayer.State.Moving) { StartCoroutine(Kill("desk")); return true; }
        if (inRoom && player.Hidden && player.FlashOn) { StartCoroutine(Kill("light")); return true; }
        return false;
    }

    void ShowIntruder(Vector3 at, Vector3 dir)
    {
        if (alternatesGroup) alternatesGroup.SetActive(true);
        if (hideOthers != null) foreach (var g in hideOthers) if (g) g.SetActive(false);
        intruder.SetActive(true);
        foreach (var r in intruder.GetComponentsInChildren<Renderer>()) r.enabled = true;
        anim = intruder.GetComponent<Animator>();
        reach = intruder.GetComponent<AltReach>(); if (!reach) reach = intruder.AddComponent<AltReach>();
        reach.enabled = true; reach.target = null; reach.weight = 0f; reach.headTilt = 0f; reach.headTwitch = 0f;
        var ik = intruder.GetComponent<StairFeetIK>(); if (ik) ik.enabled = false;
        anim.enabled = true; anim.speed = 1f; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        dir.y = 0f;
        intruder.transform.SetPositionAndRotation(at, Quaternion.LookRotation(dir.normalized));
    }

    IEnumerator Walk(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a; d.y = 0f;
        intruder.transform.rotation = Quaternion.LookRotation(d.normalized);
        anim.Play("Walk", 0, Random.value); anim.SetFloat("WalkSpeed", walkSpeed / cycleSpeed);
        float dur = d.magnitude / walkSpeed;
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur)
        {
            intruder.transform.position = Vector3.Lerp(a, b, t);
            if (CheckBetrayal()) yield break;
            yield return null;
        }
        intruder.transform.position = b;
        anim.SetFloat("WalkSpeed", 0f);
    }

    IEnumerator TurnTo(Vector3 p, float dur)
    {
        Vector3 d = p - intruder.transform.position; d.y = 0f;
        Quaternion r0 = intruder.transform.rotation, r1 = Quaternion.LookRotation(d.normalized);
        anim.SetFloat("WalkSpeed", 0.25f);
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur)
        {
            intruder.transform.rotation = Quaternion.Slerp(r0, r1, t * t * (3f - 2f * t));
            if (CheckBetrayal()) yield break;
            yield return null;
        }
        anim.SetFloat("WalkSpeed", 0f);
    }

    // ---------------------------------------------------------------- outcomes
    IEnumerator Kill(string cause)
    {
        if (over) yield break;
        over = true; busy = true;
        timer.Pause();
        var camT = player.cam.transform;
        // it is already at you: inches from your face, hands coming up
        Vector3 fwd = camT.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 at = camT.position + fwd * 0.55f; at.y = 2.85f;
        ShowIntruder(at, -fwd);
        anim.Play("StairIdle", 0, 0.3f);
        reach.target = camT; reach.weight = 1f; reach.headTilt = -18f; reach.headTwitch = 10f;
        var head = anim.GetBoneTransform(HumanBodyBones.Head);
        player.Freeze(head ? head.position : at + Vector3.up * 1.6f);
        var src = gameObject.AddComponent<AudioSource>(); src.spatialBlend = 0f;
        if (jumpscare) src.PlayOneShot(jumpscare, 0.9f);
        var sanity = FindFirstObjectByType<SanitySystem>(); if (sanity) sanity.Spike();
        Vector3 basePos = camT.localPosition;
        for (float t = 0f; t < 0.65f; t += Time.deltaTime)
        {
            camT.localPosition = basePos + Random.insideUnitSphere * 0.02f * (1f - t);
            camT.localRotation = Quaternion.Euler(Random.insideUnitSphere * 2f * (1f - t));
            yield return null;
        }
        player.cam.cullingMask = 0; player.cam.clearFlags = CameraClearFlags.SolidColor; player.cam.backgroundColor = Color.black;
        yield return new WaitForSecondsRealtime(0.8f);
        src.Stop();
        AudioListener.volume = 0f;
        yield return new WaitForSecondsRealtime(0.8f);
        AudioListener.volume = 0.6f * GameSettings.Volume;
        fade = 1f;
        endDead = true;
        endText = cause == "laptop" ? "IT WAS STANDING BEHIND YOU" : cause == "desk" ? "IT WAS STILL IN THE ROOM" : cause == "screen" ? "IT SAW THE SCREEN" : cause == "light" ? "IT SAW THE LIGHT" : "THEY CAME INTO YOUR ROOM";
        endSub = cause == "laptop" ? "You were too busy with the files." : cause == "desk" ? "It had not left yet." : cause == "screen" ? "You left the laptop on." : cause == "light" ? "You turned on the flashlight under the desk." : "They found you in bed.";
        GameHUD.EndInfo = "NIGHT " + timer.night + "   ·   " + timer.TimeText;
        if (alertTone && !GameHUD.BurstOn) { var a = gameObject.AddComponent<AudioSource>(); a.spatialBlend = 0f; a.ignoreListenerVolume = true; a.volume = 0.5f; a.clip = alertTone; a.PlayDelayed(0.3f); }
        for (float w = 0f; w < GameHUD.DeathHold; w += Time.unscaledDeltaTime)
        {
            var m = Mouse.current;
            if (w > 5f && m != null && m.leftButton.wasPressedThisFrame) break;
            yield return null;
        }
        AudioListener.volume = GameSettings.Volume;
        NightTimer.carryNight = timer.night;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    IEnumerator Survived()
    {
        if (over) yield break;
        over = true;
        yield return new WaitForSeconds(2.5f);
        fadeColor = new Color(0.85f, 0.82f, 0.75f);
        endDead = false;
        for (float f = 0f; f < 1f; f += Time.deltaTime / 2.5f) { fade = f; yield return null; }
        fade = 1f;
        endText = "Night " + timer.night + " survived.";
        endSub = player && player.decrypt >= 1f ? "All files decrypted." : "Archive decrypted: " + Mathf.FloorToInt((player ? player.decrypt : 0f) * 100f) + "%.";
        yield return new WaitForSeconds(4.5f);
        NightTimer.carryNight = timer.night + 1; NightTimer.carryDay = timer.night;   // the day comes first
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void OnGUI()
    {
        if (fade <= 0f) return;
        GUI.depth = -10;
        GameHUD.DrawEnd(fade, fadeColor, endText, endSub, endDead);
    }

    // test hook
    public void KnockNow() { if (!busy && !over) cooldown = 0f; }
}
