using UnityEngine;
using UnityEngine.InputSystem;

// Daytime free walk: WASD + mouse look, Shift to run, LMB to use what you point at (DayItem).
// Lives on the Player next to the night controllers and is only switched on by day.
// The Player transform is the eye; the capsule hangs below it.
public class DayPlayer : MonoBehaviour
{
    public Camera cam;
    public float walkSpeed = 1.75f;
    public float runSpeed = 3.0f;
    public float sensitivity = 0.11f;
    public float reach = 2.3f;
    public float gravity = -15f;
    public AudioClip[] stepClips;
    [Range(0f, 1f)] public float stepVolume = 0.32f;

    [HideInInspector] public bool frozen;
    public DayItem Hover { get; private set; }

    CharacterController cc;
    float yaw, pitch, vy, stepAcc, bob, bobAmp;
    AudioSource src; int lastStep = -1;
    readonly RaycastHit[] hits = new RaycastHit[16];

    void OnEnable()
    {
        cc = GetComponent<CharacterController>();
        if (!cc) cc = gameObject.AddComponent<CharacterController>();
        cc.height = 1.70f; cc.radius = 0.24f; cc.center = new Vector3(0f, -0.79f, 0f);
        cc.stepOffset = 0.4f; cc.slopeLimit = 55f; cc.skinWidth = 0.02f; cc.minMoveDistance = 0f;
        cc.enabled = true;
        if (!cam) cam = GetComponentInChildren<Camera>();
        yaw = transform.eulerAngles.y; pitch = 0f;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        cam.transform.localPosition = Vector3.zero; cam.transform.localRotation = Quaternion.identity;
        if (!src) { src = gameObject.AddComponent<AudioSource>(); src.spatialBlend = 0f; src.playOnAwake = false; }
        if (stepClips == null || stepClips.Length == 0) { var pp = GetComponent<PanicPlayer>(); if (pp) stepClips = pp.stepClips; }
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    void OnDisable() { if (cc) cc.enabled = false; }

    public void Place(Vector3 eye, Vector3 lookAt)
    {
        if (cc) cc.enabled = false;
        transform.position = eye;
        if (cc) cc.enabled = true;
        Vector3 d = lookAt - eye;
        Vector3 flat = new Vector3(d.x, 0f, d.z);
        if (flat.sqrMagnitude > 1e-4f) yaw = Quaternion.LookRotation(flat).eulerAngles.y;
        pitch = -Mathf.Atan2(d.y, Mathf.Max(0.01f, flat.magnitude)) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        vy = 0f;
    }

    void Update()
    {
        var m = Mouse.current; var kb = Keyboard.current;
        float dt = Time.deltaTime;
        if (GameMenu.Paused) return;
        if (!frozen && m != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 d = m.delta.ReadValue() * sensitivity * GameSettings.Sensitivity;
            yaw += d.x; pitch = Mathf.Clamp(pitch - d.y, -80f, 80f);
        }
        if (m != null && m.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; return; }
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        Vector3 move = Vector3.zero; bool run = false;
        if (!frozen && kb != null)
        {
            float x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
            float z = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
            move = transform.right * x + transform.forward * z;
            if (move.sqrMagnitude > 1f) move.Normalize();
            run = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        }
        float speed = run ? runSpeed : walkSpeed;
        if (cc.isGrounded && vy < 0f) vy = -2f;
        vy += gravity * dt;
        Vector3 before = transform.position;
        cc.Move((move * speed + Vector3.up * vy) * dt);
        Vector3 dl = transform.position - before; dl.y = 0f;
        float hs = dl.magnitude / Mathf.Max(dt, 1e-4f);

        // head bob + footsteps
        bobAmp = Mathf.MoveTowards(bobAmp, cc.isGrounded && hs > 0.3f ? Mathf.Clamp01(hs / 2.4f) : 0f, dt * 4f);
        if (cc.isGrounded && hs > 0.3f)
        {
            float stride = run ? 0.78f : 0.62f;
            bob += dl.magnitude / stride * Mathf.PI;
            stepAcc += dl.magnitude;
            if (stepAcc >= stride) { stepAcc = 0f; Step(run); }
        }
        float bx = Mathf.Sin(bob) * 0.014f * bobAmp, by = -Mathf.Abs(Mathf.Cos(bob)) * 0.03f * bobAmp;
        cam.transform.localPosition = new Vector3(bx, by, 0f);
        cam.transform.localRotation = Quaternion.Euler(pitch, 0f, -bx * 40f);

        // what are we pointing at
        Hover = null;
        if (frozen) return;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        int n = Physics.RaycastNonAlloc(ray, hits, reach, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, 0, n, HitCmp.I);
        for (int i = 0; i < n; i++)
        {
            var c = hits[i].collider;
            if (c.transform.IsChildOf(transform)) continue;
            var it = c.GetComponentInParent<DayItem>();
            if (it && it.isActiveAndEnabled && it.Usable) { Hover = it; break; }
            if (!c.isTrigger) break;
        }
        if (Hover && m != null && m.leftButton.wasPressedThisFrame) Hover.Use();
    }

    class HitCmp : System.Collections.Generic.IComparer<RaycastHit>
    {
        public static readonly HitCmp I = new HitCmp();
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }

    void Step(bool run)
    {
        if (stepClips == null || stepClips.Length == 0) return;
        int k = Random.Range(0, stepClips.Length);
        if (k == lastStep) k = (k + 1) % stepClips.Length;
        lastStep = k;
        src.pitch = Random.Range(0.93f, 1.06f);
        src.PlayOneShot(stepClips[k], stepVolume * (run ? 1.25f : 1f));
    }
}
