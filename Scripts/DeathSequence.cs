using System.Collections;
using UnityEngine;

// Death: you turn and the alternate is just standing where it got in, head slowly leaning over.
// Your flashlight flickers and dies. In the dark it closes the distance. The light snaps back on with its face
// inches away and its hands coming for you - scream, M.A.D. tear, shake - then a hard cut to black and silence.
//   door / window  -> the groom          stairs / tv -> the hooded one          mad -> the revolver (nothing shown, cut on the shot)
public class DeathSequence : MonoBehaviour
{
    public PanicPlayer player;
    public GameObject alternatesGroup;
    public GameObject groom;
    public GameObject hooded;

    [Header("Where they appear")]
    public Vector3 doorSpawn = new Vector3(0.2f, 0f, -3.75f);
    public Vector3 windowSpawn = new Vector3(0.7f, 0f, 4.3f);
    public Vector3 stairsSpawn = new Vector3(-1.0f, 0f, 1.65f);
    public Vector3 tvSpawn = new Vector3(-1.2f, 0f, 0.45f);

    [Header("Timing")]
    public float stareTime = 1.4f;       // it stands in the doorway, head slowly leaning
    public float flickerTime = 0.6f;     // the flashlight dies
    public float darkTime = 0.55f;       // pitch black (it moves)
    public float revealDistance = 0.5f;  // where it is when the light comes back
    public float revealTime = 0.6f;      // face, scream, shake - then the cut
    public float cutSilence = 0.9f;
    public float screamTail = 0.9f;
    [Header("Sound")]
    public AudioClip jumpscare;           // plays when the light snaps back on (falls back to the sting)
    [Range(0f, 1f)] public float jumpscareVolume = 0.9f;
    [Header("M.A.D. ending (sanity runs out)")]
    public GameObject revolver;           // prefab, grip at the origin, muzzle along +Z, sights up
    public AudioClip hammerCock, gunshot;
    public float gunRaiseTime = 1.5f;     // the revolver comes up into view, he looks at it
    public float gunHoldTime = 1.6f;      // hammer cocked, shaking
    public float templeTime = 2.0f;       // it goes up to his head, he looks away down the corridor
    public float rushSpeed = 7.5f;
    public float lyingTime = 2.4f;
    public float faceLightIntensity = 2.2f;
    Light faceLight;
    void OnDestroy() { if (faceLight) Destroy(faceLight.gameObject); }

    public IEnumerator Run(Vector3 threatPoint, string cause, AudioClip sting)
    {
        float tail = screamTail;
        var camT = player.cam.transform; var cam = player.cam;
        var src = gameObject.AddComponent<AudioSource>(); src.spatialBlend = 0f;
        var sanity = FindFirstObjectByType<SanitySystem>();
        var arms = player.arms;
        Light beam = player.flashlight;
        Material lens = player.flashLens ? player.flashLens.material : null;
        System.Action<bool, float> SetLight = (on, k) =>
        {
            if (beam) { beam.enabled = on; beam.intensity = player.flashIntensity * k; }
            if (lens) lens.SetColor("_EmissionColor", on ? new Color(1f, 0.92f, 0.75f) * 8f * k : Color.black);
        };
        System.Action<float> HoldArms = fear => { if (arms) { arms.Drive(true, false, false, 0f, false); arms.fear = fear; } };

        GameObject alt = null; Vector3 spawn = threatPoint;
        switch (cause)
        {
            case "door": alt = groom; spawn = doorSpawn; break;
            case "window": alt = groom; spawn = windowSpawn; break;
            case "stairs": alt = hooded; spawn = stairsSpawn; break;
            case "tv": alt = hooded; spawn = tvSpawn; break;
        }

        if (alt)
        {
            if (alternatesGroup) alternatesGroup.SetActive(true);
            groom.SetActive(alt == groom); hooded.SetActive(alt == hooded);
            Vector3 camFlat0 = new Vector3(camT.position.x, 0f, camT.position.z);
            Vector3 face = camFlat0 - spawn; face.y = 0f;
            alt.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(face.normalized));
            var anim = alt.GetComponent<Animator>();
            anim.applyRootMotion = false; anim.speed = 1f; anim.Play("Idle", 0, 0.37f);
            anim.Update(0f); anim.speed = 0f;      // dead still - no breathing, no sway. Only the head moves.
            var reach = alt.GetComponent<AltReach>(); if (!reach) reach = alt.AddComponent<AltReach>();
            reach.target = camT; reach.weight = 0f; reach.headTilt = 0f; reach.headTwitch = 0f;

            // 1. you turn - it is just standing there. The head slowly leans over.
            player.Freeze(spawn + Vector3.up * 1.55f, 0.14f);
            if (cause == "door") player.CreakDoorTo(100f, 0.18f);
            if (cause == "window") player.ForceOpenCurtains();
            SetLight(true, 1f);
            for (float t = 0f; t < stareTime; t += Time.deltaTime)
            {
                reach.headTilt = Mathf.SmoothStep(0f, 32f, t / stareTime);
                HoldArms(t / stareTime);
                yield return null;
            }

            // 2. the flashlight dies on you
            for (float t = 0f; t < flickerTime; t += Time.deltaTime)
            {
                SetLight(Random.value > 0.35f + 0.6f * (t / flickerTime), Random.Range(0.3f, 1f));
                HoldArms(1f);
                yield return null;
            }
            SetLight(false, 0f);

            // 3. darkness. It is not in the doorway any more.
            Vector3 fwd = camT.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 camFlat = new Vector3(camT.position.x, 0f, camT.position.z);
            alt.transform.SetPositionAndRotation(camFlat + fwd * revealDistance, Quaternion.LookRotation(-fwd));
            reach.weight = 1f; reach.headTilt = -20f; reach.headTwitch = 4f;
            var altRenderers = alt.GetComponentsInChildren<Renderer>();
            foreach (var r in altRenderers) r.enabled = false;     // nothing to see in the dark - not even a moonlit outline
            for (float t = 0f, dark = darkTime + Random.value * 0.35f; t < dark; t += Time.deltaTime) { HoldArms(1f); yield return null; }

            // 4. light snaps back on - it is in your face (and your eyes snap to it)
            var headBone = anim.GetBoneTransform(HumanBodyBones.Head);
            if (headBone) player.Freeze(headBone.position + Vector3.up * 0.03f, 0.05f);
            foreach (var r in altRenderers) r.enabled = true;
            SetLight(true, 1.4f);
            src.pitch = 1f;
            if (jumpscare) src.PlayOneShot(jumpscare, jumpscareVolume); else if (sting) src.PlayOneShot(sting, 1f);
            if (sanity) sanity.Spike();
            if (arms) arms.Act("Defend", 5f);
            Vector3 a0 = alt.transform.position, a1 = camFlat + fwd * (revealDistance - 0.12f);
            Vector3 camBase = camT.localPosition;
            for (float t = 0f; t < revealTime; t += Time.deltaTime)
            {
                alt.transform.position = Vector3.Lerp(a0, a1, Mathf.Clamp01(t / 0.18f));
                reach.headTwitch = 10f;
                float k = 1f - t / revealTime * 0.5f;
                camT.localPosition = camBase + Random.insideUnitSphere * 0.018f * k;
                camT.localRotation = camT.localRotation * Quaternion.Euler(Random.insideUnitSphere * 1.6f * k);
                HoldArms(1f);
                yield return null;
            }
        }
        else if (revolver && arms)
        {
            // M.A.D.: nobody comes. The broadcast has already won - Mark does what it tells him.
            // He looks down: the revolver comes up into his hand. Hammer back. Shaking.
            Vector3 flat = camT.forward; flat.y = 0f; flat.Normalize();
            Vector3 eye = camT.position;
            player.Freeze(eye + flat * 0.6f + Vector3.down * 0.35f, 0.9f, false);
            SetLight(true, 1f);
            var gun = Instantiate(revolver);
            gun.name = "Revolver";
            arms.HoldGun(gun.transform);
            arms.Act("GunLow", 60f);
            for (float t = 0f; t < gunRaiseTime; t += Time.deltaTime) { HoldArms(1f); yield return null; }
            if (hammerCock) src.PlayOneShot(hammerCock, 0.8f);
            for (float t = 0f; t < gunHoldTime; t += Time.deltaTime) { HoldArms(1f); yield return null; }
            // up to the temple. He turns his eyes away, down the empty corridor.
            arms.Act("GunTemple", 60f);
            Vector3 left = Vector3.Cross(flat, Vector3.up);
            player.Freeze(eye + flat * 2f + left * 0.35f + Vector3.down * 0.15f, 1.4f, false);
            for (float t = 0f; t < templeTime; t += Time.deltaTime)
            {
                HoldArms(1f);
                if (sanity && Random.value < 0.04f) sanity.Spike();
                yield return null;
            }
            // nothing is shown: the shot and the cut are the same frame
            if (gunshot) src.PlayOneShot(gunshot, 1f);
            tail = 0.14f;
        }
        else
        {
            // fallback without the revolver: the broadcast just takes you
            player.Freeze(camT.position + camT.forward, 0.1f);
            if (sanity) sanity.Spike();
            if (sting) src.PlayOneShot(sting, 1f);
            Vector3 camBase = camT.localPosition;
            for (float t = 0f; t < 0.9f; t += Time.deltaTime)
            {
                if (sanity) sanity.Spike();
                camT.localPosition = camBase + Random.insideUnitSphere * 0.02f;
                yield return null;
            }
        }

        // 5. hard cut: black and silence
        cam.cullingMask = 0;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        SetLight(false, 0f);
        yield return new WaitForSecondsRealtime(tail);   // the scream carries on into the black for a moment...
        src.Stop();                                              // ...and is cut off
        float vol = AudioListener.volume;
        AudioListener.volume = 0f;
        yield return new WaitForSecondsRealtime(cutSilence);
        AudioListener.volume = vol;
    }

    // ---------------------------------------------------------------- ragdoll built on the spot from the humanoid bones
    Rigidbody fallBody;

    // no body any more: the head (camera) drops to the floor, the arms go limp, the flashlight rolls away
    void FallDown(Vector3 push)
    {
        var cam = player.cam.transform;
        var arms = player.arms;
        player.enabled = false;
        arms.Limp();
        var prop = arms.flashlightProp;
        if (prop && prop.gameObject.activeInHierarchy)
        {
            prop.SetParent(null, true);
            var pr = prop.gameObject.AddComponent<Rigidbody>(); pr.mass = 0.4f; pr.interpolation = RigidbodyInterpolation.Interpolate;
            var pc = prop.gameObject.AddComponent<CapsuleCollider>(); pc.direction = 2; pc.radius = 0.03f; pc.height = 0.23f;
            pr.AddForce((push + Vector3.up * 0.5f) * 1.2f, ForceMode.Impulse);
            pr.AddTorque(Random.onUnitSphere * 0.05f, ForceMode.Impulse);
        }
        cam.SetParent(null, true);
        var rb = cam.gameObject.AddComponent<Rigidbody>();
        rb.mass = 5f; rb.linearDamping = 0.2f; rb.angularDamping = 1.5f; rb.interpolation = RigidbodyInterpolation.Interpolate;
        var sc = cam.gameObject.AddComponent<SphereCollider>(); sc.radius = 0.11f;
        foreach (var col in player.GetComponentsInChildren<Collider>()) if (col != sc) Physics.IgnoreCollision(col, sc);
        rb.AddForce(push * 2.4f + Vector3.up * 0.8f, ForceMode.VelocityChange);
        rb.AddTorque(Vector3.Cross(Vector3.up, push) * 5f + Random.insideUnitSphere * 2f, ForceMode.VelocityChange);
        fallBody = rb;
    }

    void Ragdoll(Vector3 push)
    {
        if (player.arms) { FallDown(push); return; }
        var body = player.body;
        var anim = body.GetComponent<Animator>();
        player.enabled = false;
        body.enabled = false;
        anim.enabled = false;
        System.Func<HumanBodyBones, Transform> B = b => anim.GetBoneTransform(b);

        var hips = B(HumanBodyBones.Hips);
        var chest = B(HumanBodyBones.UpperChest) ? B(HumanBodyBones.UpperChest) : B(HumanBodyBones.Chest);
        var neck = B(HumanBodyBones.Neck);
        var head = B(HumanBodyBones.Head);

        var rHips = Part(hips, B(HumanBodyBones.Spine), 0.13f, 12f, null, 0f);
        var rChest = Part(chest, neck, 0.15f, 12f, rHips, 25f);
        var rNeck = Part(neck, head, 0.06f, 4f, rChest, 35f);
        var hs = neck.gameObject.AddComponent<SphereCollider>();
        hs.center = neck.InverseTransformPoint(head.position + Vector3.up * 0.08f);
        hs.radius = 0.1f / Mathf.Max(1e-4f, neck.lossyScale.x);
        foreach (var side in new[] { 0, 1 })
        {
            var ua = B(side == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var la = B(side == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var hd = B(side == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var ul = B(side == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            var ll = B(side == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            var ft = B(side == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            var rua = Part(ua, la, 0.05f, 2.5f, rChest, 60f);
            Part(la, hd, 0.04f, 1.8f, rua, 70f);
            var rul = Part(ul, ll, 0.075f, 7f, rHips, 45f);
            Part(ll, ft, 0.055f, 4f, rul, 60f);
        }

        // camera rides the head down
        player.cam.transform.SetParent(neck, true);

        // the flashlight slips out of the hand and keeps shining
        var prop = body.flashlightProp;
        if (prop && prop.gameObject.activeInHierarchy)
        {
            prop.SetParent(null, true);
            var pr = prop.gameObject.AddComponent<Rigidbody>(); pr.mass = 0.4f; pr.interpolation = RigidbodyInterpolation.Interpolate;
            var pc = prop.gameObject.AddComponent<CapsuleCollider>(); pc.direction = 2; pc.radius = 0.03f; pc.height = 0.23f;
            pr.AddForce((push + Vector3.up * 0.5f) * 1.2f, ForceMode.Impulse);
            pr.AddTorque(Random.onUnitSphere * 0.05f, ForceMode.Impulse);
        }

        rChest.AddForce((push * 2.6f + Vector3.up * 0.4f) * 12f, ForceMode.Impulse);
        rNeck.AddForce(push * 8f, ForceMode.Impulse);
    }

    static Rigidbody Part(Transform bone, Transform toward, float radius, float mass, Rigidbody parent, float swing)
    {
        var rb = bone.gameObject.AddComponent<Rigidbody>();
        rb.mass = mass; rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.angularDamping = 0.6f;
        if (toward)
        {
            var col = bone.gameObject.AddComponent<CapsuleCollider>();
            Vector3 local = bone.InverseTransformPoint(toward.position);
            Vector3 a = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
            col.direction = a.x > a.y && a.x > a.z ? 0 : a.y > a.z ? 1 : 2;
            col.center = local * 0.5f;
            float s = Mathf.Max(1e-4f, bone.lossyScale.x);
            col.radius = radius / s;
            col.height = local.magnitude + col.radius;
        }
        if (parent)
        {
            var j = bone.gameObject.AddComponent<CharacterJoint>();
            j.connectedBody = parent;
            j.enableProjection = true;
            var lo = j.lowTwistLimit; lo.limit = -swing * 0.6f; j.lowTwistLimit = lo;
            var hi = j.highTwistLimit; hi.limit = swing * 0.6f; j.highTwistLimit = hi;
            var s1 = j.swing1Limit; s1.limit = swing; j.swing1Limit = s1;
            var s2 = j.swing2Limit; s2.limit = swing * 0.7f; j.swing2Limit = s2;
        }
        return rb;
    }
}
