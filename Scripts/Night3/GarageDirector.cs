using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Night 3 - the garage. Everything Mark does makes noise, and noise brings them.
//   THE BOLT  - his wife's voice at the door to the house, the handle rattles, the bolt starts sliding open
//               by itself. Run over and slam it shut before it opens.
//   THE GATE  - fists on the roll-up door, fingers under the gap. Freeze: no noise, no light, until they go.
//   THE VISIT - three slow knocks on the house door: something is coming in no matter what.
//               Get in the car and duck. No light.
// At 6:00 it is too late. If the engine starts first, she comes to the door one last time.
public class GarageDirector : MonoBehaviour
{
    [Header("Refs")]
    public GaragePlayer player;
    public NightTimer timer;
    public GarageGate gate;
    public Transform houseDoor;               // hinge
    public float houseDoorOpenAngle = 95f;
    public Transform bolt;
    public Vector3 boltLockedLocal, boltOpenLocal;
    public GameObject alternatesGroup, intruder;   // the one that comes in / kills (groom)
    public GameObject[] hideOthers;
    public GameObject wife;                   // only in the ending
    public GameObject fingers;                // under the roll-up door
    public Light kitchenLight, bulb, gateLight;
    public Light[] headlights;
    public Renderer[] headlightGlass;
    public Transform carRoot;

    [Header("Path (world, floor)")]
    public Vector3 doorInside = new Vector3(-2.7f, 0f, -4.2f);
    public Vector3 carFrontRight = new Vector3(-3.35f, 0f, -1.9f);
    public Vector3 carRearRight = new Vector3(-3.35f, 0f, -6.6f);
    public Vector3 carLookAt = new Vector3(-5.0f, 1.0f, -4.4f);
    public Vector3 wifeDoorway = new Vector3(-2.15f, 0f, -4.2f);
    public Vector3 wifeAtCar = new Vector3(-3.75f, 0f, -4.0f);
    public Vector3 wifeAtGate = new Vector3(-5.0f, 0f, -1.4f);
    public float driveToZ = 9f;

    [Header("Timing")]
    public float firstThreat = 20f;
    public Vector2 cooldownEasy = new Vector2(30f, 42f), cooldownHard = new Vector2(12f, 20f);
    public float walkSpeed = 0.8f, cycleSpeed = 1.15f;

    [Header("Sound")]
    public AudioClip[] wifeLines;             // through the door
    public AudioClip wifeFinal;               // "I'm here" - repeated like a stuck tape at the end
    public AudioClip wifeWhy;                 // "why aren't you looking at me"
    public AudioClip creakClip, jumpscare, alertTone, engineStart, engineLoop, accel, doorKnock;
    public AudioClip handleClip;      // somebody trying the door handle
    [Tooltip("On: Door Knock is a single knock and is played 3 times. Off: Door Knock already has all the knocks in it and is played once.")]
    public bool repeatDoorKnock = true;
    public AudioClip knockLoud;       // banging on the house door between her lines
    public AudioClip gateRiseClip;    // the roll-up door being torn up from outside

    public float Noise => noise;
    public bool BoltLocked => boltT <= 0.001f;
    public bool Over => over;

    float noise, cooldown, boltT, doorAngle, wifeTimer;
    bool over, busy, inRoom, gateThreat, boltThreat, finale;
    float gateNoiseStart, bulbBase = 1f;
    Quaternion doorClosed;
    Animator anim; AltReach reach;
    AudioSource loop;
    AudioClip bang0, bang1, rattle, boltClip, scrape;
    float fade; Color fadeColor = Color.black; string endText, endSub; bool endDead;
    int endPhase; float endT;

    void Start()
    {
        if (!player) player = FindFirstObjectByType<GaragePlayer>();
        if (!timer) timer = FindFirstObjectByType<NightTimer>();
        if (houseDoor) doorClosed = houseDoor.rotation;
        if (bulb) bulbBase = bulb.intensity;
        bang0 = GarageSfx.MetalBang(3); bang1 = GarageSfx.MetalBang(8); rattle = handleClip ? handleClip : GarageSfx.Rattle(); boltClip = GarageSfx.Bolt(); scrape = GarageSfx.Scrape();
        if (!doorKnock) doorKnock = SfxBank.Thud();
        if (!creakClip) creakClip = SfxBank.Creak(1.4f);
        cooldown = firstThreat; wifeTimer = 9f;
        if (timer) timer.OnNightEnd += () => { if (!finale) StartCoroutine(Kill("dawn")); };
        if (intruder) { anim = intruder.GetComponent<Animator>(); intruder.SetActive(false); }
        if (wife) wife.SetActive(false);
        if (fingers) fingers.SetActive(false);
        if (kitchenLight) kitchenLight.enabled = false;
        foreach (var l in headlights) if (l) l.enabled = false;
        SetBolt(0f); SetDoor(0f);
        loop = gameObject.AddComponent<AudioSource>(); loop.loop = true; loop.spatialBlend = 0f; loop.playOnAwake = false;
    }

    float Aggression => timer ? Mathf.Clamp01(0.2f + 0.14f * timer.Hour) : 0.3f;

    public void AddNoise(float x) { if (!over) noise = Mathf.Clamp01(noise + x); }

    void Update()
    {
        if (bulb) bulb.intensity = Mathf.Lerp(bulb.intensity, (Mathf.PerlinNoise(Time.time * 2.3f, 0.7f) > 0.18f ? 1f : 0.25f) * bulbBase, Time.deltaTime * 20f);
        if (over || !timer || !timer.Running) return;
        noise = Mathf.Max(0f, noise - Time.deltaTime * 0.055f);

        // she keeps talking to him through the door
        if (!busy && (wifeTimer -= Time.deltaTime) <= 0f)
        {
            wifeTimer = Random.Range(18f, 32f);
            if (knockLoud && Random.value < 0.25f) Voice(knockLoud, DoorPos + Vector3.right * 0.4f, 0.8f);
            else if (wifeLines != null && wifeLines.Length > 0) Voice(wifeLines[Random.Range(0, wifeLines.Length)], DoorPos + Vector3.right * 0.6f, 0.9f, Random.Range(0.94f, 1.0f));
        }

        if (busy) return;
        // too loud: they come in right now
        if (noise >= 0.999f) { StartCoroutine(Visit(2.6f)); return; }
        cooldown -= Time.deltaTime * (1f + noise * 1.5f);
        if (cooldown > 0f) return;
        float r = Random.value, visitW = Mathf.Lerp(0.15f, 0.35f, Aggression);
        if (r < visitW) StartCoroutine(Visit(4.6f));
        else if (r < visitW + 0.42f) StartCoroutine(BoltThreat());
        else StartCoroutine(GateThreat());
    }

    void NextCooldown() => cooldown = Random.Range(Mathf.Lerp(cooldownEasy.x, cooldownHard.x, Aggression), Mathf.Lerp(cooldownEasy.y, cooldownHard.y, Aggression));

    Vector3 DoorPos => houseDoor ? houseDoor.position + Vector3.up * 1.2f : doorInside + Vector3.up * 1.2f;
    Vector3 GatePos => gate ? gate.transform.position + Vector3.up * 0.8f : new Vector3(-5f, 0.8f, -1.2f);

    void SetBolt(float t) { boltT = Mathf.Clamp01(t); if (bolt) bolt.localPosition = Vector3.Lerp(boltLockedLocal, boltOpenLocal, boltT); }
    void SetDoor(float a) { doorAngle = a; if (houseDoor) houseDoor.rotation = Quaternion.AngleAxis(a, Vector3.up) * doorClosed; }

    public void LockBolt()
    {
        SetBolt(0f);
        AddNoise(0.03f);
    }

    AudioSource Voice(AudioClip c, Vector3 at, float vol, float pitch = 1f)
    {
        if (!c) return null;
        var go = new GameObject("G_Sfx"); go.transform.position = at;
        var a = go.AddComponent<AudioSource>(); a.clip = c; a.spatialBlend = 1f; a.minDistance = 1.5f; a.maxDistance = 25f; a.volume = vol; a.pitch = pitch; a.dopplerLevel = 0f;
        a.Play(); Destroy(go, c.length / Mathf.Max(0.1f, pitch) + 0.3f);
        return a;
    }

    // ---------------------------------------------------------------- THE BOLT
    IEnumerator BoltThreat()
    {
        busy = true; boltThreat = true;
        if (wifeLines != null && wifeLines.Length > 0) { var v = wifeLines[Random.Range(0, wifeLines.Length)]; Voice(v, DoorPos + Vector3.right * 0.6f, 1f); yield return new WaitForSeconds(v.length * 0.7f); }
        Voice(rattle, DoorPos, 1f); yield return new WaitForSeconds(0.9f);
        Voice(rattle, DoorPos, 1f, 1.1f);
        // somebody is working the bolt from the other side
        if (BoltLocked) Voice(boltClip, DoorPos, 0.5f, 0.6f);
        float dur = Mathf.Lerp(6.5f, 4.2f, Aggression);
        float t = boltT;
        while (t < 1f)
        {
            if (over) yield break;
            if (BoltLocked && t > 0.05f) { boltThreat = false; busy = false; NextCooldown(); yield break; }   // he slammed it shut
            t += Time.deltaTime / dur;
            SetBolt(t);
            yield return null;
        }
        // it is open
        Voice(creakClip, DoorPos, 1f);
        for (float a = 0f; a < 1f; a += Time.deltaTime / 0.9f) { SetDoor(Mathf.Lerp(0f, houseDoorOpenAngle * 0.4f, a)); yield return null; }
        StartCoroutine(Kill("door"));
    }

    // ---------------------------------------------------------------- THE GATE
    IEnumerator GateThreat()
    {
        busy = true;
        for (int i = 0; i < 3; i++) { Voice(i % 2 == 0 ? bang0 : bang1, GatePos + Vector3.forward * 0.4f, 1f, Random.Range(0.9f, 1.05f)); yield return new WaitForSeconds(0.55f); }
        yield return new WaitForSeconds(0.4f);
        gateThreat = true; gateNoiseStart = noise;
        if (fingers) { fingers.SetActive(true); fingers.transform.position = new Vector3(fingers.transform.position.x, gate ? gate.transform.position.y + gate.Gap : 0f, fingers.transform.position.z); }
        Voice(scrape, GatePos, 0.9f);
        float dur = Random.Range(6.5f, 9f);
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            if (over) yield break;
            if (gate) gate.yank = Mathf.Abs(Mathf.Sin(t * 7f)) * 0.025f;
            if (noise > gateNoiseStart + 0.05f || player.FlashOn)
            {
                // it heard him
                if (gateRiseClip) Voice(gateRiseClip, GatePos, 1f, 1.15f);
                for (float y = 0f; y < 1f; y += Time.deltaTime / 0.35f) { if (gate) gate.yank = Mathf.Lerp(0.02f, 0.65f, y); yield return null; }
                StartCoroutine(Kill(player.FlashOn ? "light" : "gate"));
                yield break;
            }
            gateNoiseStart = Mathf.Max(gateNoiseStart - Time.deltaTime * 0.055f, 0f) + 0f;
            gateNoiseStart = Mathf.Min(gateNoiseStart, noise);
            yield return null;
        }
        if (gate) gate.yank = 0f;
        if (fingers) fingers.SetActive(false);
        gateThreat = false; busy = false; NextCooldown();
    }

    // ---------------------------------------------------------------- THE VISIT
    IEnumerator Visit(float warn)
    {
        busy = true;
        if (repeatDoorKnock) for (int i = 0; i < 3; i++) { Voice(doorKnock, DoorPos, 1f, 0.75f); yield return new WaitForSeconds(0.9f); }
        else { Voice(doorKnock, DoorPos, 1f); yield return new WaitForSeconds(doorKnock.length); }
        for (float t = 0f; t < warn - 2.7f; t += Time.deltaTime) { if (over) yield break; yield return null; }
        // the bolt slides back on its own, the door opens
        Voice(boltClip, DoorPos, 0.7f, 0.8f); SetBolt(1f);
        Voice(creakClip, DoorPos, 1f);
        for (float a = 0f; a < 1f; a += Time.deltaTime / 0.8f) { SetDoor(Mathf.Lerp(0f, houseDoorOpenAngle, a)); yield return null; }
        if (kitchenLight) kitchenLight.enabled = true;
        if (!(player.InCar && player.Ducked)) { StartCoroutine(Kill(player.InCar ? "car" : "visit")); yield break; }
        if (player.FlashOn) { StartCoroutine(Kill("light")); yield break; }

        inRoom = true;
        ShowIntruder(doorInside, carFrontRight - doorInside);
        yield return Walk(doorInside, carFrontRight); if (over) yield break;
        yield return TurnTo(carLookAt, 0.8f); if (over) yield break;
        anim.Play("StairIdle", 0, Random.value);
        for (float t = 0f; t < 2.6f; t += Time.deltaTime) { if (reach) reach.headTilt = Mathf.SmoothStep(0f, 32f, t / 1.5f); if (Watch()) yield break; yield return null; }
        if (reach) reach.headTilt = 0f;
        yield return Walk(carFrontRight, carRearRight); if (over) yield break;
        yield return TurnTo(carLookAt, 0.7f); if (over) yield break;
        for (float t = 0f; t < 1.8f; t += Time.deltaTime) { if (Watch()) yield break; yield return null; }
        yield return Walk(carRearRight, doorInside); if (over) yield break;
        intruder.SetActive(false);
        inRoom = false;
        for (float a = 0f; a < 1f; a += Time.deltaTime / 1.2f) { SetDoor(Mathf.Lerp(houseDoorOpenAngle, 0f, a * a)); yield return null; }
        SetDoor(0f);
        if (kitchenLight) kitchenLight.enabled = false;
        // it leaves the bolt open behind it
        busy = false; NextCooldown();
        cooldown = Mathf.Min(cooldown, 10f);
        StartCoroutine(BoltThreatSoon());
    }

    IEnumerator BoltThreatSoon()
    {
        yield return new WaitForSeconds(4f);
        if (!busy && !over && !BoltLocked) StartCoroutine(BoltThreat());
    }

    bool Watch()
    {
        if (over) return true;
        if (inRoom && (!player.InCar || !player.Ducked) && player.state != GaragePlayer.State.Moving) { StartCoroutine(Kill(player.InCar ? "car" : "visit")); return true; }
        if (inRoom && player.FlashOn) { StartCoroutine(Kill("light")); return true; }
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
        intruder.transform.SetPositionAndRotation(at, Quaternion.LookRotation(dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward));
    }

    IEnumerator Walk(Vector3 a, Vector3 b) { yield return WalkFigure(intruder, anim, a, b, walkSpeed, true); }

    IEnumerator WalkFigure(GameObject fig, Animator an, Vector3 a, Vector3 b, float speed, bool watch)
    {
        Vector3 d = b - a; d.y = 0f;
        fig.transform.rotation = Quaternion.LookRotation(d.normalized);
        an.Play("Walk", 0, Random.value); an.SetFloat("WalkSpeed", speed / cycleSpeed);
        float dur = d.magnitude / speed;
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur)
        {
            fig.transform.position = Vector3.Lerp(a, b, t);
            if (watch && Watch()) yield break;
            yield return null;
        }
        fig.transform.position = b;
        an.SetFloat("WalkSpeed", 0f);
    }

    IEnumerator TurnTo(Vector3 p, float dur)
    {
        Vector3 d = p - intruder.transform.position; d.y = 0f;
        Quaternion r0 = intruder.transform.rotation, r1 = Quaternion.LookRotation(d.normalized);
        anim.SetFloat("WalkSpeed", 0.25f);
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur)
        {
            intruder.transform.rotation = Quaternion.Slerp(r0, r1, t * t * (3f - 2f * t));
            if (Watch()) yield break;
            yield return null;
        }
        anim.SetFloat("WalkSpeed", 0f);
    }

    // ---------------------------------------------------------------- death
    IEnumerator Kill(string cause)
    {
        if (over) yield break;
        over = true; busy = true;
        if (timer) timer.Pause();
        if (gate) gate.yank = 0f;
        var camT = player.cam.transform;
        var src = gameObject.AddComponent<AudioSource>(); src.spatialBlend = 0f;
        if (cause != "dawn" && intruder)
        {
            Vector3 fwd = camT.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward; fwd.Normalize();
            Vector3 at = camT.position + fwd * 0.55f; at.y = 0f;
            ShowIntruder(at, -fwd);
            anim.Play("StairIdle", 0, 0.3f);
            reach.target = camT; reach.weight = 1f; reach.headTilt = -18f; reach.headTwitch = 10f;
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            player.Freeze(head ? head.position : at + Vector3.up * 1.6f);
            if (jumpscare) src.PlayOneShot(jumpscare, 0.9f);
            Vector3 basePos = camT.localPosition;
            for (float t = 0f; t < 0.65f; t += Time.deltaTime)
            {
                camT.localPosition = basePos + Random.insideUnitSphere * 0.02f * (1f - t);
                camT.localRotation = Quaternion.Euler(Random.insideUnitSphere * 2f * (1f - t));
                yield return null;
            }
        }
        else player.Freeze(camT.position + camT.forward);
        player.cam.cullingMask = 0; player.cam.clearFlags = CameraClearFlags.SolidColor; player.cam.backgroundColor = Color.black;
        yield return new WaitForSecondsRealtime(0.8f);
        src.Stop(); loop.Stop();
        AudioListener.volume = 0f;
        yield return new WaitForSecondsRealtime(0.8f);
        AudioListener.volume = 0.6f * GameSettings.Volume;
        fade = 1f; endDead = true;
        switch (cause)
        {
            case "door": endText = "SHE LET HERSELF IN"; endSub = "You didn't lock the door."; break;
            case "gate": endText = "IT HEARD YOU"; endSub = "You made a sound while it was at the garage door."; break;
            case "light": endText = "IT SAW THE LIGHT"; endSub = "You turned on the flashlight."; break;
            case "car": endText = "IT SAW YOU IN THE CAR"; endSub = "You didn't duck down."; break;
            case "dawn": endText = "IT WAS MORNING"; endSub = "You never left."; break;
            case "wife": endText = "YOU STAYED"; endSub = "She got in the car."; break;
            default: endText = "IT FOUND YOU"; endSub = "You weren't hiding in the car."; break;
        }
        GameHUD.EndInfo = "NIGHT " + (timer ? timer.night : 3) + "   ·   " + (timer ? timer.TimeText : "");
        if (alertTone && !GameHUD.BurstOn) { var a = gameObject.AddComponent<AudioSource>(); a.spatialBlend = 0f; a.ignoreListenerVolume = true; a.volume = 0.5f; a.clip = alertTone; a.PlayDelayed(0.3f); }
        for (float w = 0f; w < GameHUD.DeathHold; w += Time.unscaledDeltaTime)
        {
            var m = Mouse.current;
            if (w > 5f && m != null && m.leftButton.wasPressedThisFrame) break;
            yield return null;
        }
        AudioListener.volume = GameSettings.Volume;
        NightTimer.carryNight = timer ? timer.night : 3;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ---------------------------------------------------------------- the ending
    public static bool DebugDrive;   // test hook: acts as the click that drives off in the finale
    public void StartFinale() { if (!over && !finale) StartCoroutine(Finale()); }

    IEnumerator Finale()
    {
        finale = true; over = true; busy = true;
        if (timer) timer.Pause();
        GameHUD.HideClock = true;
        if (gate) gate.yank = 0f;
        if (fingers) fingers.SetActive(false);
        if (intruder) intruder.SetActive(false);
        player.BeginFinale();
        var src = gameObject.AddComponent<AudioSource>(); src.spatialBlend = 0f;
        if (engineStart) src.PlayOneShot(engineStart, 1f);
        yield return new WaitForSeconds(engineStart ? Mathf.Min(engineStart.length, 2.2f) : 1.5f);
        if (engineLoop) { loop.clip = engineLoop; loop.volume = 0.55f; loop.Play(); }
        foreach (var l in headlights) if (l) l.enabled = true;
        foreach (var r in headlightGlass) if (r) { r.material.EnableKeyword("_EMISSION"); r.material.SetColor("_EmissionColor", new Color(1f, 0.95f, 0.8f) * 3f); }
        yield return new WaitForSeconds(1.2f);

        // the door to the house opens by itself
        Voice(boltClip, DoorPos, 0.7f, 0.9f); SetBolt(1f);
        yield return new WaitForSeconds(0.6f);
        Voice(creakClip, DoorPos, 1f, 0.85f);
        if (kitchenLight) kitchenLight.enabled = true;
        var w = wife ? wife : intruder;
        var wa = w.GetComponent<Animator>();
        var wr = w.GetComponent<AltReach>(); if (!wr) wr = w.AddComponent<AltReach>();
        if (alternatesGroup) alternatesGroup.SetActive(true);
        w.SetActive(true);
        wa.enabled = true; wa.applyRootMotion = false; wa.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        Vector3 toCar = wifeAtCar - wifeDoorway; toCar.y = 0f;
        w.transform.SetPositionAndRotation(wifeDoorway, Quaternion.LookRotation(toCar.normalized));
        wa.Play("StairIdle", 0, 0.2f);
        wr.enabled = true; wr.target = null; wr.weight = 0f; wr.headTilt = 0f; wr.headTwitch = 0f;
        for (float a = 0f; a < 1f; a += Time.deltaTime / 2.2f) { SetDoor(Mathf.Lerp(0f, houseDoorOpenAngle, a * (2f - a))); yield return null; }

        // Mark turns to look at her
        var head = wa.GetBoneTransform(HumanBodyBones.Head);
        Vector3 her = head ? head.position : wifeDoorway + Vector3.up * 1.6f;
        yield return player.Turn(her, 1.2f);
        player.SetBaseLook(her - player.transform.position, 70f);
        yield return new WaitForSeconds(0.6f);

        // she sounds just like her. Until she doesn't.
        if (wifeWhy) { Voice(wifeWhy, her, 1f); yield return new WaitForSeconds(wifeWhy.length + 0.5f); }
        if (wifeFinal)
        {
            Voice(wifeFinal, her, 1f); yield return new WaitForSeconds(wifeFinal.length + 0.15f);
            float[] pitches = { 1f, 0.97f, 0.9f, 0.8f, 0.66f };
            for (int i = 0; i < pitches.Length; i++)
            {
                var a = Voice(wifeFinal, her, 1f, pitches[i]);
                float cut = Mathf.Min(0.55f, wifeFinal.length * 0.5f) / pitches[i];
                wr.headTilt = 8f + i * 9f; wr.headTwitch = i * 3f;
                yield return new WaitForSeconds(cut);
                if (a) a.Stop();
            }
        }
        wr.headTilt = 52f; wr.headTwitch = 14f;

        // she comes for the car. Drive.
        endPhase = -1;  // prompt
        float walked = 0f;
        Vector3 from = wifeDoorway;
        wa.Play("Walk", 0, 0f); wa.SetFloat("WalkSpeed", 0.5f / cycleSpeed);
        bool go = false;
        while (!go)
        {
            walked += Time.deltaTime / 6.5f;
            w.transform.position = Vector3.Lerp(from, wifeAtCar, Mathf.Clamp01(walked));
            var m = Mouse.current;
            if (m != null && m.leftButton.wasPressedThisFrame && walked > 0.05f) go = true;
            if (DebugDrive && walked > 0.3f) { go = true; DebugDrive = false; }   // trailer / test hook
            if (walked >= 1f) { endPhase = 0; over = false; StartCoroutine(Kill("wife")); yield break; }
            yield return null;
        }
        endPhase = 0;

        // DRIVE
        if (accel) src.PlayOneShot(accel, 1f);
        loop.pitch = 1.25f; loop.volume = 0.8f;
        player.SetBaseLook(Vector3.forward, 60f);
        StartCoroutine(player.Turn(player.transform.position + Vector3.forward * 5f + Vector3.down * 0.3f, 0.6f));
        Vector3 seat = carRoot.InverseTransformPoint(player.transform.position);
        Vector3 start = carRoot.position;
        float speed = 0f; float z = start.z;
        bool wifeMoved = false, lookedBack = false;
        while (z < driveToZ)
        {
            speed = Mathf.Min(speed + Time.deltaTime * 2.2f, 3.4f);
            z += speed * Time.deltaTime;
            float y = z < -1.2f ? start.y : Mathf.Lerp(start.y, start.y - 0.5f, Mathf.Clamp01((z + 1.2f) / 3.5f));
            carRoot.position = new Vector3(start.x, y + Mathf.Sin(z * 9f) * 0.004f, z);
            player.transform.position = carRoot.TransformPoint(seat) + lookOff;
            if (!wifeMoved && z > -0.5f)
            {
                // when he looks back, she is standing in the garage door, waving
                wifeMoved = true;
                w.transform.SetPositionAndRotation(wifeAtGate, Quaternion.LookRotation(Vector3.forward));
                wa.Play("StairIdle", 0, 0f); wa.SetFloat("WalkSpeed", 0f);
                wr.enabled = false;
                var wave = w.GetComponent<AltWave>(); if (!wave) wave = w.AddComponent<AltWave>();
                wave.enabled = true; if (gateLight) { gateLight.enabled = true; gateLight.intensity = 2.5f; gateLight.range = 7f; } wave.lookAt = player.cam ? player.cam.transform : null;
            }
            if (!lookedBack && z > 0.3f) { lookedBack = true; StartCoroutine(LookBack()); }
            yield return null;
        }
        // radio
        if (alertTone) { var a = gameObject.AddComponent<AudioSource>(); a.spatialBlend = 0f; a.volume = 0.45f; a.clip = alertTone; a.Play(); }
        for (float f = 0f; f < 1f; f += Time.deltaTime / 1.5f) { fade = f; fadeColor = Color.black; endDead = false; endText = ""; endSub = ""; speed = Mathf.MoveTowards(speed, 0f, Time.deltaTime * 2.6f); z += speed * Time.deltaTime; carRoot.position = new Vector3(start.x, carRoot.position.y, z); player.transform.position = carRoot.TransformPoint(seat); yield return null; }
        fade = 1f; loop.Stop();
        endPhase = 1; endT = 0f;
    }

    IEnumerator LookBack()
    {
        var me = player.transform;
        player.SetBaseLook(Vector3.back, 70f);       // let him look over his shoulder
        StartCoroutine(Lean(new Vector3(0.28f, 0.14f, 0.05f), 0.8f));
        Vector3 back = me.position - Vector3.forward * 8f + Vector3.down * 0.4f;
        yield return player.Turn(back, 0.9f);
        for (float t = 0f; t < 1.6f; t += Time.deltaTime) { me.rotation = Quaternion.LookRotation(back - me.position); yield return null; }
        player.SetBaseLook(Vector3.forward, 60f);
        StartCoroutine(Lean(Vector3.zero, 0.6f));
        yield return player.Turn(me.position + Vector3.forward * 6f, 0.6f);
    }

    Vector3 lookOff;
    IEnumerator Lean(Vector3 to, float dur)
    {
        Vector3 from = lookOff;
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur) { lookOff = Vector3.Lerp(from, to, t * t * (3f - 2f * t)); yield return null; }
        lookOff = to;
    }

    public static string[] CreditLines => Credits;
    static readonly string[] Credits =
    {
        "THE MANDELA CATALOGUE",
        "a free fan game",
        "",
        "Based on The Mandela Catalogue by Alex Kister.",
        "All rights to the original setting and characters belong to Alex Kister.",
        "",
        "MODELS",
        "Interior Village  ·  PSX Derelict Furniture (Daniel Jurys)  ·  PSX Doors Pack (Kasugay)",
        "PSX Style Cars (GGBot)  ·  Retro House Pack  ·  Tree Pack  ·  Hands (FUZE Technologies)",
        "Workshop Kit Free Sampler (Ink & Oddity Studio)  ·  Garage props pack  ·  Low poly jacket",
        "Universal Animation Library (Quaternius)  ·  2000s pack  ·  S&W Model 29",
        "",
        "TEXTURES",
        "SBS Horror Texture Pack (Screaming Brain Studios)",
        "",
        "SOUND",
        "freesound.org: castironcarousel, drinkingpeter, qubodup, xloizlol, pblzr,",
        "marcusgraham325, flem0527, lth_stp, mikasibuyi, solarphasing, waveplaysfx, erokia",
        "Car sounds: GGBot",
        "",
        "Voices generated with ElevenLabs",
        "",
        "FONTS",
        "DejaVu  ·  Liberation Serif (Red Hat, OFL)  ·  GNU FreeFont",
        "",
        "Menu style inspired by Graveyard Shift at Freddy's (THERESNOSTEAK)",
        "Lynn's photos: the developer's sister - thank you",
        "",
        "Made with Unity and Blender",
        "",
        "Thank you for playing.",
    };

    void LateUpdate()
    {
        if (GameMenu.Paused) return;
        if (endPhase >= 1) endT += Time.deltaTime;
        if (endPhase == 1 && endT > 2f)
        {
            var m = Mouse.current;
            if (endT > 22f && m != null && m.leftButton.wasPressedThisFrame)
            {
                GameSettings.MarkFinished();
                NightTimer.carryNight = 1; NightTimer.carryDay = 0;
                GameMenu.BackToMenu();
            }
        }
    }

    GUIStyle sBig, sMid, sCred;
    void OnGUI()
    {
        GUI.depth = -10;
        float w = Screen.width, h = Screen.height, k = h / 720f;
        if (sBig == null)
        {
            sBig = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontStyle = FontStyle.Bold };
            sMid = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            sCred = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, wordWrap = true };
        }
        if (endPhase == -1)
        {
            float blink = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * 4f));
            sBig.fontSize = Mathf.RoundToInt(30 * k);
            GUI.color = new Color(0f, 0f, 0f, 0.7f); GUI.Label(new Rect(2, h * 0.72f + 2, w, 50 * k), Loc.T("LMB — DRIVE"), sBig);
            GUI.color = new Color(1f, 0.25f, 0.2f, blink); GUI.Label(new Rect(0, h * 0.72f, w, 50 * k), Loc.T("LMB — DRIVE"), sBig);
            GUI.color = Color.white;
            return;
        }
        if (endPhase >= 1)
        {
            GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            sMid.fontSize = Mathf.RoundToInt(24 * k);
            float a1 = Mathf.Clamp01((endT - 1f) / 1.5f) * Mathf.Clamp01((11f - endT) / 1.5f);
            float a2 = Mathf.Clamp01((endT - 4.5f) / 1.5f) * Mathf.Clamp01((11f - endT) / 1.5f);
            GUI.color = new Color(0.9f, 0.9f, 0.85f, a1);
            GUI.Label(new Rect(0, h * 0.40f, w, 40 * k), Loc.T("Mark Heathcliff left Mandela County that night."), sMid);
            GUI.color = new Color(0.85f, 0.15f, 0.12f, a2);
            GUI.Label(new Rect(0, h * 0.40f + 50 * k, w, 40 * k), Loc.T("His wife never did."), sMid);
            if (endT > 12f)
            {
                float scroll = (endT - 12f) * 42f * k;
                sCred.fontSize = Mathf.RoundToInt(18 * k);
                float y = h - scroll;
                for (int i = 0; i < Credits.Length; i++)
                {
                    bool head = i == 0 || Credits[i] == "MODELS" || Credits[i] == "TEXTURES" || Credits[i] == "SOUND";
                    sCred.fontStyle = head ? FontStyle.Bold : FontStyle.Normal;
                    sCred.fontSize = Mathf.RoundToInt((i == 0 ? 30 : 18) * k);
                    GUI.color = i == 0 ? new Color(0.85f, 0.12f, 0.1f) : new Color(0.85f, 0.85f, 0.8f);
                    GUI.Label(new Rect(0, y, w, 40 * k), Loc.T(Credits[i]), sCred);
                    y += (i == 0 ? 46 : 30) * k;
                }
            }
            if (endT > 22f)
            {
                sMid.fontSize = Mathf.RoundToInt(16 * k);
                GUI.color = new Color(1f, 1f, 1f, 0.5f + 0.4f * Mathf.Abs(Mathf.Sin(endT * 2f)));
                GUI.Label(new Rect(0, h - 50 * k, w, 30 * k), Loc.T("LMB — back to the beginning"), sMid);
            }
            GUI.color = Color.white;
            return;
        }
        if (fade <= 0f) return;
        if (endDead) GameHUD.DrawEnd(fade, fadeColor, endText, endSub, endDead);
        else { GUI.color = new Color(0f, 0f, 0f, fade); GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture); GUI.color = Color.white; }
    }

    // test hooks
    public void ThreatNow(string which)
    {
        if (busy || over) return;
        if (which == "bolt") StartCoroutine(BoltThreat());
        else if (which == "gate") StartCoroutine(GateThreat());
        else StartCoroutine(Visit(4.6f));
    }
}
