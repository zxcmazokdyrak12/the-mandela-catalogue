using UnityEngine;

// Mark's first-person arms. No body, no Mixamo.
// Built procedurally in PS1 style (rigid low-poly segments, tiny point-filtered textures) and animated by hand:
// every action is an authored hand pose in camera space (where the wrist is, where the palm faces, how closed the fist is);
// the wrist is pulled there by a damped spring, the arm follows with two-bone IK, fingers curl per joint.
// Secondary motion: hands lag behind camera turns and steps, breathing, fear tremor, idle finger fidget.
[DefaultExecutionOrder(100)]
public class FPArms : MonoBehaviour
{
    public Transform cam;
    public Transform flashlightProp;
    [Header("Imported hands (blend shapes Hold, Rock, Paper, Point over Open)")]
    public UnityEngine.Mesh handMeshR, handMeshL;
    public Material handMaterial;
    public Vector3 handMeshOffset = Vector3.zero;
    [Header("Rigged arms (overrides everything above when set)")]
    public GameObject rigPrefab;
    public float rigScale = 1.3f;
    public Vector3 rigShoulderCentre = new Vector3(0f, -0.27f, -0.05f);
    public Material rigMaterial;
    public Color skinColor = new Color(0.80f, 0.62f, 0.52f);
    public Color sleeveColor = new Color(0.19f, 0.20f, 0.17f);

    [Header("Feel")]
    public float stiffness = 160f;
    [Range(0.2f, 1.5f)] public float damping = 0.6f;
    public float rotSharpness = 15f;
    [Range(0f, 1f)] public float turnInertia = 0.55f;
    public float breath = 0.004f;
    public float tremor = 0.0012f;
    [Range(0f, 1f)] public float fear;

    [Header("Flashlight grip")]
    public Vector3 hollow = new Vector3(0f, -0.03f, 0.068f);   // centre of the fist, hand space
    public float fistHalf = 0.045f, lensOut = 0.05f, lensFromCentre = 0.115f, propScale = 1.1f;
    [Tooltip("Light bouncing back from the lens and nearby surfaces onto the hands.")]
    public float spillIntensity = 0.6f;
    public float skinGlow = 0.05f;
    Light beam, spill;

    [Header("Revolver grip (right hand)")]
    public Vector3 gunGrip = new Vector3(0f, -0.035f, 0.085f);   // grip centre, hand-pose space (z fingers, y back of the hand)
    public Vector3 gunEuler = new Vector3(0f, 0f, 90f);          // gun up = thumb side
    Transform gun;
    public void HoldGun(Transform g) { gun = g; if (gun) gun.SetParent(transform, true); }

    // ---------------------------------------------------------------- poses
    struct HandPose
    {
        public Vector3 pos, fingers, palm, elbow; public float curl, thumb; public Vector4 shape;   // shape = Hold, Rock, Paper, Point weights
        public HandPose(Vector3 p, Vector3 f, Vector3 pa, Vector3 e, float c, float t) { pos = p; fingers = f; palm = pa; elbow = e; curl = c; thumb = t; shape = new Vector4(c, 0f, 0f, 0f); }
        public HandPose Sh(float hold, float rock, float paper, float point) { var h = this; h.shape = new Vector4(hold, rock, paper, point); return h; }
        public HandPose Mirror() { var h = new HandPose(M(pos), M(fingers), M(palm), M(elbow), curl, thumb); h.shape = shape; return h; }
        public HandPose At(Vector3 p) { var h = this; h.pos = p; return h; }
        static Vector3 M(Vector3 v) => new Vector3(-v.x, v.y, v.z);
    }
    // authored for the RIGHT hand in camera space (x right, y up, z forward); left = mirror
    static readonly Vector3 ElbowDown = new Vector3(0.42f, -0.55f, 0f);
    static readonly HandPose Rest       = new HandPose(new Vector3(0.27f, -0.64f, 0.12f), new Vector3(0.1f, -1f, 0.35f), new Vector3(-1f, 0f, 0.1f), new Vector3(0.45f, -0.6f, -0.1f), 0.35f, 0.3f).Sh(0.3f, 0f, 0f, 0f);
    static readonly HandPose Shove      = new HandPose(new Vector3(0.05f, -0.1f, 0.40f), new Vector3(0f, 1f, 0.15f), new Vector3(0f, 0f, 1f), new Vector3(0.38f, -0.45f, 0.05f), -0.25f, 0f).Sh(0f, 0f, 1f, 0f);
    static readonly HandPose CurtGrab   = new HandPose(new Vector3(0.17f, -0.02f, 0.42f), new Vector3(-0.1f, 0.4f, 1f), new Vector3(-1f, 0f, 0.2f), new Vector3(0.4f, -0.4f, 0.15f), 0.95f, 0.9f).Sh(0.55f, 0.45f, 0f, 0f);
    static readonly HandPose PlugReach  = new HandPose(new Vector3(0.02f, -0.18f, 0.36f), new Vector3(-0.1f, -0.4f, 1f), new Vector3(0f, -1f, -0.3f), new Vector3(0.38f, -0.5f, 0f), 0.55f, 0.7f).Sh(0.55f, 0f, 0f, 0.2f);
    static readonly HandPose WardGrab   = new HandPose(new Vector3(-0.02f, -0.04f, 0.42f), new Vector3(-0.2f, 0f, 1f), new Vector3(-1f, 0f, 0f), new Vector3(0.4f, -0.45f, 0.05f), 0.85f, 0.75f).Sh(0.9f, 0f, 0f, 0f);
    static readonly HandPose Ears       = new HandPose(new Vector3(0.15f, -0.02f, 0.07f), new Vector3(0f, 1f, -0.1f), new Vector3(-1f, 0f, 0f), new Vector3(0.5f, -0.15f, 0.2f), 0.15f, 0.2f).Sh(0f, 0f, 0.7f, 0f);
    static readonly HandPose Terrified  = new HandPose(new Vector3(0.075f, -0.17f, 0.24f), new Vector3(-0.4f, 0.9f, 0.1f), new Vector3(-0.2f, -0.1f, -1f), new Vector3(0.32f, -0.5f, 0.05f), 0.6f, 0.5f).Sh(0.15f, 0f, 0f, 0f);
    static readonly HandPose Run        = new HandPose(new Vector3(0.19f, -0.33f, 0.26f), new Vector3(0f, 0.2f, 1f), new Vector3(-1f, 0f, 0f), new Vector3(0.4f, -0.6f, -0.1f), 0.9f, 0.8f).Sh(0f, 0.9f, 0f, 0f);
    static readonly HandPose LimpPose   = new HandPose(new Vector3(0.3f, -1.1f, 0f), new Vector3(0f, -1f, 0f), new Vector3(-1f, 0f, 0f), new Vector3(0.45f, -0.6f, -0.1f), 0.2f, 0.1f);
    // left hand holding the flashlight - authored in LEFT space (approved grip: palm down, index toward you, lens past the little finger)
    static readonly Vector3 FlashPos = new Vector3(-0.2f, -0.2f, 0.4f), FlashPalm = new Vector3(0.6f, -0.78f, 0.15f), FlashSide = new Vector3(-0.3f, -0.05f, -0.95f);
    static readonly Vector3 FlashElbow = new Vector3(-0.38f, -0.45f, 0.1f);

    // ---------------------------------------------------------------- state from the controller
    bool flash, ears, running, hidden, limp; float runPhase;
    string act; float actUntil;
    Vector3? reachL, reachR;
    float flashW;

    public void Drive(bool flashOn, bool earsCovered, bool isRunning, float phase, bool isHidden)
    {
        flash = flashOn; ears = earsCovered; running = isRunning; runPhase = phase; hidden = isHidden;
    }
    public void Act(string name, float dur) { if (name == null || !RightAct(name, out _)) return; act = name; actUntil = Time.time + dur; }
    public void Clear() { act = null; }
    public void Reach(Vector3? l, Vector3? r) { reachL = l; reachR = r ?? l; }
    public void Limp() { limp = true; }

    // ---------------------------------------------------------------- rig
    class Arm
    {
        public float s;
        public Transform shoulder, upper, fore, hand;
        public Transform[] joints = new Transform[12]; public Quaternion[] jointRest = new Quaternion[12];
        public Transform[] thumb = new Transform[3]; public Quaternion[] thumbRest = new Quaternion[3];
        public Vector3 pos, vel; public Quaternion rot = Quaternion.identity; public float curl, thumbCurl;
        public HandPose target; public Quaternion targetRot;
        public SkinnedMeshRenderer smr; public Vector4 w;
        public bool rig; public float A = 0.29f, B = 0.25f;
        public Quaternion upInv, foreInv, handInv;
        public Vector3[] jointAxis = new Vector3[12]; public Vector3[] thumbAxis = new Vector3[3];
        public float curlSign = 1f, thumbSign = 1f;
    }
    Arm R, L;
    const float A = 0.29f, B = 0.25f;
    Quaternion prevCamRot; Vector3 prevCamPos; bool primed;
    Material skinMat, sleeveMat;

    void Awake()
    {
        if (!cam && Camera.main) cam = Camera.main.transform;
        Build();
    }

    void LateUpdate()
    {
        if (!cam) return;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (dt <= 0f) return;
        transform.SetPositionAndRotation(cam.position, cam.rotation);

        // camera motion in its own space -> hands stay behind a little (inertia)
        Quaternion dq = primed ? Quaternion.Inverse(prevCamRot) * cam.rotation : Quaternion.identity;
        Vector3 dMove = primed ? cam.InverseTransformVector(cam.position - prevCamPos) : Vector3.zero;
        prevCamRot = cam.rotation; prevCamPos = cam.position; primed = true;

        ChooseTargets();
        Step(R, dq, dMove, dt);
        Step(L, dq, dMove, dt);
        Solve(R); Solve(L);
        PlaceFlashlight(dt);
        if (gun)
        {
            Quaternion hr = transform.rotation * R.rot;
            gun.SetPositionAndRotation(R.hand.position + hr * gunGrip, hr * Quaternion.Euler(gunEuler));
        }
    }

    void ChooseTargets()
    {
        bool actOn = act != null && Time.time < actUntil;
        float t = Time.time;
        float sR = Mathf.Sin(runPhase * 0.5f);

        // right hand
        HandPose r;
        if (limp) r = LimpPose;
        else if (ears) r = Ears;
        else if (actOn && RightAct(act, out r)) { }
        else if (reachR.HasValue) r = ReachPose(reachR.Value);
        else if (hidden) r = Terrified;
        else if (running) r = Run.At(Run.pos + new Vector3(0f, 0.07f * sR, 0.08f * sR));
        else r = Rest;

        // left hand
        HandPose l;
        bool flashHold = false;
        if (limp) l = LimpPose.Mirror();
        else if (ears) l = Ears.Mirror();
        else if (flash) { l = FlashPose(); flashHold = true; }
        else if (actOn && LeftAct(act, out l)) { }
        else if (reachL.HasValue) l = ReachPose(reachL.Value).Mirror().At(cam.InverseTransformPoint(reachL.Value));
        else if (hidden) l = Terrified.Mirror();
        else if (running) l = Run.Mirror().At(Run.Mirror().pos + new Vector3(0f, -0.07f * sR, -0.08f * sR));
        else l = Rest.Mirror();

        if (flashHold && running) l.pos += new Vector3(0.01f * sR, 0.02f * Mathf.Abs(sR), 0f);
        Set(R, r); Set(L, l);
        flashW = Mathf.MoveTowards(flashW, flashHold ? 1f : 0f, Time.deltaTime * 6f);
    }

    bool RightAct(string a, out HandPose p)
    {
        switch (a)
        {
            case "Push": case "Shove": p = Shove; return true;
            case "Defend": p = new HandPose(new Vector3(0.17f, -0.17f, 0.25f), new Vector3(-0.15f, 1f, 0.1f), new Vector3(0f, 0f, 1f), new Vector3(0.35f, -0.4f, 0.05f), 0.15f, 0.2f).Sh(0f, 0f, 0.55f, 0f); return true;
            case "CurtainGrab": p = CurtGrab; return true;
            case "CurtainClose": p = CurtGrab.At(new Vector3(0.04f, -0.03f, 0.44f)); return true;
            case "CurtainOpen": p = CurtGrab.At(new Vector3(0.30f, -0.05f, 0.36f)); return true;
            case "PlugReach": p = PlugReach; return true;
            case "PlugTug": p = PlugReach.At(new Vector3(0.04f, -0.15f, 0.27f)); p.curl = 0.95f; p.thumb = 0.9f; p.shape = new Vector4(0.6f, 0.4f, 0f, 0f); return true;
            case "Lever": case "PlugRip": p = new HandPose(new Vector3(0.17f, -0.2f, 0.22f), new Vector3(-0.5f, 0.1f, 1f), new Vector3(-0.2f, -1f, 0.1f), ElbowDown, 0.95f, 0.9f).Sh(0.4f, 0.6f, 0f, 0f); return true;
            case "WardGrab": p = WardGrab; return true;
            case "WardPull": p = WardGrab.At(new Vector3(0.16f, -0.08f, 0.22f)); return true;
            case "WardHold": p = WardGrab.At(new Vector3(-0.06f, -0.02f, 0.38f)); return true;
            case "WardPush": p = Shove.At(new Vector3(0.0f, -0.06f, 0.45f)); return true;
            // revolver: held low in front of him, then at the right temple (barrel pointing left into the head)
            case "GunLow": p = new HandPose(new Vector3(0.05f, -0.17f, 0.33f), new Vector3(-0.2f, 0.15f, 1f), new Vector3(-1f, -0.2f, 0f), new Vector3(0.4f, -0.55f, -0.05f), 1f, 0.8f).Sh(1f, 0f, 0f, 0f); return true;
            case "GunTemple": p = new HandPose(new Vector3(0.40f, -0.07f, 0.05f), new Vector3(-1f, 0.05f, -0.05f), new Vector3(0f, 0f, -1f), new Vector3(0.55f, -0.35f, -0.1f), 1f, 0.8f).Sh(1f, 0f, 0f, 0f); return true;
        }
        p = Rest; return false;
    }
    bool LeftAct(string a, out HandPose p)
    {
        switch (a)
        {
            case "CurtainGrab": case "CurtainClose": case "CurtainOpen":
                RightAct(a, out p); p = p.Mirror(); return true;
        }
        p = Rest.Mirror(); return false;
    }

    HandPose ReachPose(Vector3 world)
    {
        Vector3 fw = cam.forward; fw.y = 0f; if (fw.sqrMagnitude < 1e-4f) fw = Vector3.forward;
        return new HandPose(cam.InverseTransformPoint(world), cam.InverseTransformDirection(fw.normalized),
                            cam.InverseTransformDirection(Vector3.down), ElbowDown, 0.12f, 0.2f).Sh(0f, 0f, 0.85f, 0f);
    }

    HandPose FlashPose()
    {
        Vector3 palm = FlashPalm.normalized, side = FlashSide.normalized;
        Vector3 fingers = Vector3.Cross(palm, side).normalized;
        return new HandPose(FlashPos + new Vector3(Mathf.Sin(Time.time * 1.3f) * 0.004f, Mathf.Sin(Time.time * 0.9f) * 0.003f, 0f), fingers, palm, FlashElbow, 1f, 0.85f).Sh(1f, 0f, 0f, 0f);
    }

    void Set(Arm a, HandPose p)
    {
        a.target = p;
        a.targetRot = Quaternion.LookRotation(p.fingers.normalized, -p.palm.normalized);
    }

    void Step(Arm a, Quaternion dq, Vector3 dMove, float dt)
    {
        float t = Time.time + a.s * 3.1f;
        Vector3 goal = a.target.pos;
        goal.y += Mathf.Sin(t * 1.7f) * breath;
        float trem = tremor + fear * 0.006f + (hidden ? 0.003f : 0f);
        goal += new Vector3(Mathf.PerlinNoise(t * 9f, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t * 9f) - 0.5f, Mathf.PerlinNoise(t * 7f, t * 3f) - 0.5f) * 2f * trem;

        // world-space inertia: when the head turns or moves, the hand is left behind a bit
        Vector3 rotated = Quaternion.Inverse(dq) * a.pos;
        a.pos = Vector3.Lerp(a.pos, rotated, turnInertia);
        a.pos -= dMove * 0.35f;

        float k = limp ? 60f : stiffness, c = 2f * Mathf.Sqrt(k) * (limp ? 0.3f : damping);
        for (int i = 0; i < 2; i++)
        {
            float h = dt * 0.5f;
            Vector3 acc = k * (goal - a.pos) - c * a.vel;
            if (limp) acc += cam.InverseTransformDirection(Physics.gravity) * 0.6f;
            a.vel += acc * h; a.pos += a.vel * h;
        }
        Quaternion lagRot = Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(dq), turnInertia * 0.6f);
        a.rot = Quaternion.Slerp(lagRot * a.rot, a.targetRot, 1f - Mathf.Exp(-rotSharpness * dt));
        float fidget = (Mathf.PerlinNoise(t * 0.6f, 4f) - 0.5f) * 0.08f;
        a.curl = Mathf.Lerp(a.curl, Mathf.Clamp(a.target.curl + fidget, -0.4f, 1f), 1f - Mathf.Exp(-11f * dt));
        a.thumbCurl = Mathf.Lerp(a.thumbCurl, a.target.thumb, 1f - Mathf.Exp(-11f * dt));
        Vector4 tw = a.target.shape; float tot = tw.x + tw.y + tw.z + tw.w; if (tot > 1f) tw /= tot;
        tw.x = Mathf.Clamp01(tw.x + fidget * (1f - tw.x));
        a.w = Vector4.Lerp(a.w, tw, 1f - Mathf.Exp(-12f * dt));
    }

    // two-bone IK from the shoulder to the wrist, elbow pulled toward the pose's hint
    void Solve(Arm a)
    {
        Vector3 S = a.shoulder.position;
        Vector3 W = transform.TransformPoint(a.pos);
        Vector3 H = transform.TransformPoint(a.target.elbow);
        Vector3 d = W - S; float len = d.magnitude;
        float A = a.A, B = a.B;
        float reach = (A + B) * 0.999f;
        if (len > reach) { W = S + d / len * reach; len = reach; }
        len = Mathf.Max(len, Mathf.Abs(A - B) + 0.01f);
        Vector3 dir = (W - S).normalized;
        Vector3 hint = H - S; Vector3 n = hint - dir * Vector3.Dot(hint, dir);
        if (n.sqrMagnitude < 1e-6f) n = -transform.up; n.Normalize();
        float cosA = Mathf.Clamp((A * A + len * len - B * B) / (2f * A * len), -1f, 1f);
        float sinA = Mathf.Sqrt(1f - cosA * cosA);
        Vector3 E = S + dir * (A * cosA) + n * (A * sinA);
        Quaternion handWorld = transform.rotation * a.rot;
        if (a.rig)
        {
            a.upper.rotation = Quaternion.LookRotation(E - S, n) * a.upInv;
            Vector3 ff = (W - E).normalized;
            Vector3 hu = handWorld * Vector3.up; Vector3 fu = Vector3.Slerp(n, hu, 0.35f);
            a.fore.rotation = Quaternion.LookRotation(ff, fu) * a.foreInv;
            a.hand.rotation = handWorld * a.handInv;
            float rc = a.curl;
            for (int fi = 0; fi < 4; fi++)
            {
                float ex = fi * 0.04f;
                float[] ang = { (rc + ex) * 80f - 4f, (rc + ex) * 95f, (rc + ex) * 65f };
                for (int j = 0; j < 3; j++)
                {
                    int k = fi * 3 + j; if (!a.joints[k]) continue;
                    a.joints[k].localRotation = a.jointRest[k] * Quaternion.AngleAxis(ang[j] * a.curlSign, a.jointAxis[k]);
                }
            }
            float tc2 = a.thumbCurl;
            float[] ta = { tc2 * 20f, tc2 * 35f, tc2 * 50f };
            for (int j = 0; j < 3; j++) if (a.thumb[j]) a.thumb[j].localRotation = a.thumbRest[j] * Quaternion.AngleAxis(ta[j] * a.thumbSign, a.thumbAxis[j]);
            return;
        }
        a.upper.rotation = Quaternion.LookRotation(E - S, n);
        Vector3 handUp = handWorld * Vector3.up;
        Vector3 foreFwd = (W - E).normalized;
        Vector3 foreUp = Vector3.Slerp(-n, handUp, 0.6f);
        a.fore.rotation = Quaternion.LookRotation(foreFwd, foreUp);
        a.fore.position = E;
        a.hand.position = E + foreFwd * B;
        a.hand.rotation = handWorld;

        if (a.smr)
        {
            a.smr.SetBlendShapeWeight(0, a.w.x * 100f); a.smr.SetBlendShapeWeight(1, a.w.y * 100f);
            a.smr.SetBlendShapeWeight(2, a.w.z * 100f); a.smr.SetBlendShapeWeight(3, a.w.w * 100f);
            return;
        }
        // fingers
        float c = a.curl;
        for (int f = 0; f < 4; f++)
        {
            float extra = f * 0.04f;
            a.joints[f * 3 + 0].localRotation = a.jointRest[f * 3 + 0] * Quaternion.Euler((c + extra) * 82f - 4f, 0f, 0f);
            a.joints[f * 3 + 1].localRotation = a.jointRest[f * 3 + 1] * Quaternion.Euler((c + extra) * 98f, 0f, 0f);
            a.joints[f * 3 + 2].localRotation = a.jointRest[f * 3 + 2] * Quaternion.Euler((c + extra) * 68f, 0f, 0f);
        }
        float tc = a.thumbCurl;
        a.thumb[0].localRotation = a.thumbRest[0] * Quaternion.Euler(tc * 22f, 0f, 0f);
        a.thumb[1].localRotation = a.thumbRest[1] * Quaternion.Euler(tc * 40f, 0f, 0f);
        a.thumb[2].localRotation = a.thumbRest[2] * Quaternion.Euler(tc * 55f, 0f, 0f);
    }

    void PlaceFlashlight(float dt)
    {
        if (spill) { spill.enabled = beam && beam.enabled && !limp; spill.intensity = spillIntensity * (beam ? Mathf.Clamp01(beam.intensity / 6.5f) : 0f); }
        if (!flashlightProp || limp) return;
        bool show = flashW > 0.3f;
        if (flashlightProp.gameObject.activeSelf != show) flashlightProp.gameObject.SetActive(show);
        if (!show) return;
        flashlightProp.localScale = Vector3.one * propScale;
        // the beam follows where the hand actually points: deviation of the hand from its pose tilts the light
        Quaternion dev = (transform.rotation * L.rot) * Quaternion.Inverse(transform.rotation * L.targetRot);
        Quaternion look = Quaternion.Slerp(Quaternion.identity, dev, 0.8f) * Quaternion.LookRotation(cam.forward, cam.up);
        Vector3 fwd = look * Vector3.forward;
        Vector3 centre = L.hand.position + (transform.rotation * L.rot) * hollow;
        float along = 0f;
        flashlightProp.SetPositionAndRotation(centre + fwd * (fistHalf + lensOut - lensFromCentre * propScale - along), look);
    }

    // ---------------------------------------------------------------- procedural PS1 mesh building
    void Build()
    {
        skinMat = MakeMat(MakeTex(skinColor, 1), 0.25f);
        skinMat.EnableKeyword("_EMISSION"); skinMat.SetColor("_EmissionColor", skinColor * skinGlow);
        skinMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        sleeveMat = MakeMat(MakeTex(sleeveColor, 2), 0.05f);
        if (handMaterial) { handMaterial = new Material(handMaterial); handMaterial.EnableKeyword("_EMISSION"); handMaterial.SetColor("_EmissionColor", skinColor * skinGlow); }
        if (rigPrefab) { BuildRig(); }
        else { R = BuildArm(1f); L = BuildArm(-1f); }
        if (flashlightProp)
        {
            beam = flashlightProp.GetComponentInChildren<Light>(true);
            var sg = new GameObject("Spill");
            sg.transform.SetParent(flashlightProp, false);
            sg.transform.localPosition = new Vector3(0f, 0.03f, 0.06f);
            spill = sg.AddComponent<Light>();
            spill.type = LightType.Point; spill.range = 0.75f; spill.intensity = spillIntensity;
            spill.color = new Color(1f, 0.9f, 0.78f); spill.shadows = LightShadows.None;
        }
        foreach (var a in new[] { R, L })
        {
            a.pos = (a.s > 0 ? Rest : Rest.Mirror()).pos;
            a.rot = Quaternion.LookRotation(Rest.fingers, -Rest.palm);
            a.target = a.s > 0 ? Rest : Rest.Mirror();
            a.targetRot = a.rot;
        }
    }

    // ---------------------------------------------------------------- rigged arms (one skinned mesh, both arms)
    void BuildRig()
    {
        var go = Instantiate(rigPrefab, transform);
        go.name = "ArmsRig";
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * rigScale;
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (rigMaterial) r.sharedMaterial = rigMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.updateWhenOffscreen = true;
        }
        // find the two arm chains: clavicle -> upper -> fore -> hand(5 fingers)
        var hands = new System.Collections.Generic.List<Transform>();
        foreach (var t in go.GetComponentsInChildren<Transform>()) if (t.childCount == 5) hands.Add(t);
        go.transform.localPosition = Vector3.zero;
        // centre the shoulders on rigShoulderCentre
        Vector3 mid = Vector3.zero; foreach (var h in hands) mid += h.parent.parent.position; mid /= Mathf.Max(1, hands.Count);
        go.transform.position += transform.TransformPoint(rigShoulderCentre) - mid;
        foreach (var h in hands)
        {
            float s = Mathf.Sign(transform.InverseTransformPoint(h.position).x);
            var a = SetupRigArm(h, s);
            if (s > 0) R = a; else L = a;
        }
        foreach (var a in new[] { R, L })
        {
            if (a == null) continue;
            a.pos = (a.s > 0 ? Rest : Rest.Mirror()).pos;
            a.rot = Quaternion.LookRotation(Rest.fingers, -Rest.palm);
            a.target = a.s > 0 ? Rest : Rest.Mirror();
            a.targetRot = a.rot;
        }
    }

    Arm SetupRigArm(Transform hand, float s)
    {
        var a = new Arm { s = s, rig = true };
        a.hand = hand; a.fore = hand.parent; a.upper = a.fore.parent; a.shoulder = a.upper;
        a.A = Vector3.Distance(a.upper.position, a.fore.position);
        a.B = Vector3.Distance(a.fore.position, a.hand.position);
        var chains = new System.Collections.Generic.List<Transform>();
        foreach (Transform c in hand) chains.Add(c);
        // thumb = the chain that starts closest to the wrist; the others ordered by distance from the thumb (index first)
        Vector3 fingersGuess = Vector3.zero; foreach (var c in chains) fingersGuess += c.position - hand.position; fingersGuess.Normalize();
        chains.Sort((x, y) => Vector3.Dot(x.position - hand.position, fingersGuess).CompareTo(Vector3.Dot(y.position - hand.position, fingersGuess)));
        Transform thumb = chains[0]; chains.RemoveAt(0);
        chains.Sort((x, y) => Vector3.Distance(x.position, thumb.position).CompareTo(Vector3.Distance(y.position, thumb.position)));
        Vector3 fingers = (chains[1].position - hand.position).normalized;            // middle finger
        Vector3 side = (chains[0].position - chains[3].position).normalized;          // little -> index
        Vector3 X = -s * side;                                                          // pose frame: right index on -x, left on +x
        Vector3 dorsal = Vector3.Cross(fingers, X).normalized;
        Vector3 palm = -dorsal;
        Quaternion frame = Quaternion.LookRotation(fingers, dorsal);
        a.handInv = Quaternion.Inverse(frame) * hand.rotation;
        // elbow pole at bind: the arms hang almost straight, so fall back to 'back and out'
        Vector3 S = a.upper.position, E = a.fore.position, W = a.hand.position;
        Vector3 dir = (W - S).normalized; Vector3 pole = (E - S) - dir * Vector3.Dot(E - S, dir);
        if (pole.magnitude < 0.01f) pole = transform.TransformDirection(new Vector3(0.5f * s, 0f, -1f));
        pole.Normalize();
        a.upInv = Quaternion.Inverse(Quaternion.LookRotation(E - S, pole)) * a.upper.rotation;
        a.foreInv = Quaternion.Inverse(Quaternion.LookRotation(W - E, pole)) * a.fore.rotation;
        // finger joints: curl about the knuckle line, towards the palm
        for (int fi = 0; fi < 4; fi++)
        {
            Transform t = chains[fi];
            for (int j = 0; j < 3 && t; j++)
            {
                int k = fi * 3 + j;
                a.joints[k] = t; a.jointRest[k] = t.localRotation;
                a.jointAxis[k] = t.InverseTransformDirection(side);
                t = t.childCount > 0 ? t.GetChild(0) : null;
            }
        }
        Vector3 fdir = fingers;
        a.curlSign = Vector3.Dot(Vector3.Cross(side, fdir), palm) > 0f ? 1f : -1f;
        {
            Transform t = thumb;
            Vector3 tdir = (thumb.childCount > 0 ? thumb.GetChild(0).position - thumb.position : fingers).normalized;
            Vector3 tax = Vector3.Cross(tdir, palm).normalized;
            if (tax.sqrMagnitude < 1e-4f) tax = fingers;
            for (int j = 0; j < 3 && t; j++)
            {
                a.thumb[j] = t; a.thumbRest[j] = t.localRotation;
                a.thumbAxis[j] = t.InverseTransformDirection(tax);
                t = t.childCount > 0 ? t.GetChild(0) : null;
            }
            // curl the thumb across the palm toward the index side
            Vector3 toIdx = (chains[0].position - thumb.position).normalized;
            a.thumbSign = Vector3.Dot(Vector3.Cross(tax, tdir), toIdx) + Vector3.Dot(Vector3.Cross(tax, tdir), palm) > 0f ? 1f : -1f;
        }
        return a;
    }

    Arm BuildArm(float s)
    {
        var a = new Arm { s = s };
        a.shoulder = new GameObject(s > 0 ? "Shoulder_R" : "Shoulder_L").transform;
        a.shoulder.SetParent(transform, false);
        a.shoulder.localPosition = new Vector3(0.2f * s, -0.30f, -0.08f);
        a.upper = Part("UpperArm", a.shoulder, Vector3.zero, Loft(new[] {
            new Vector4(-0.03f, 0.060f, 0.057f, 0f), new Vector4(0.12f, 0.055f, 0.052f, 0f), new Vector4(0.29f, 0.047f, 0.045f, 0f) }, 8, s, 3f), sleeveMat);
        a.fore = Part("Forearm", a.upper, new Vector3(0f, 0f, A), Loft(new[] {
            new Vector4(-0.02f, 0.047f, 0.045f, 0f), new Vector4(0.10f, 0.043f, 0.040f, 0f), new Vector4(0.200f, 0.038f, 0.035f, 0f),
            new Vector4(0.212f, 0.042f, 0.039f, 0f), new Vector4(0.238f, 0.043f, 0.040f, 0f), new Vector4(0.242f, 0.030f, 0.027f, 0f) }, 8, s, 3f), sleeveMat);
        UnityEngine.Mesh hm = s > 0 ? handMeshR : handMeshL;
        if (hm)
        {
            a.hand = new GameObject("Hand").transform;
            a.hand.SetParent(a.fore, false); a.hand.localPosition = new Vector3(0f, 0f, B);
            var hg = new GameObject("HandMesh"); hg.transform.SetParent(a.hand, false); hg.transform.localPosition = new Vector3(handMeshOffset.x * s, handMeshOffset.y, handMeshOffset.z);
            a.smr = hg.AddComponent<SkinnedMeshRenderer>();
            a.smr.sharedMesh = hm; a.smr.sharedMaterial = handMaterial ? handMaterial : skinMat;
            a.smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            a.smr.updateWhenOffscreen = true;
            return a;
        }
        a.hand = Part("Hand", a.fore, new Vector3(0f, 0f, B), Loft(new[] {
            new Vector4(-0.035f, 0.023f, 0.018f, 0f), new Vector4(0.0f, 0.029f, 0.016f, 0f), new Vector4(0.045f, 0.041f, 0.015f, -0.001f),
            new Vector4(0.080f, 0.043f, 0.013f, 0f), new Vector4(0.089f, 0.037f, 0.010f, 0f) }, 8, s, 6f), skinMat);

        // fingers: index, middle, ring, little (right hand: index on -x)
        float[] fx = { -0.029f, -0.0095f, 0.0095f, 0.027f };
        float[] fz = { 0.082f, 0.086f, 0.083f, 0.075f };
        float[] yaw = { -4f, 0f, 3f, 8f };
        float[,] len = { { 0.040f, 0.025f, 0.020f }, { 0.045f, 0.028f, 0.022f }, { 0.042f, 0.026f, 0.020f }, { 0.032f, 0.020f, 0.018f } };
        float[] rad = { 0.0088f, 0.0092f, 0.0086f, 0.0075f };
        for (int f = 0; f < 4; f++)
        {
            Transform parent = a.hand; Vector3 at = new Vector3(fx[f] * s, -0.001f, fz[f]);
            for (int j = 0; j < 3; j++)
            {
                float l = len[f, j], r0 = rad[f] * (1f - j * 0.1f), r1 = r0 * 0.88f;
                var rings = j < 2
                    ? new[] { new Vector4(-0.005f, r0, r0 * 0.88f, 0f), new Vector4(l + 0.002f, r1, r1 * 0.88f, 0f) }
                    : new[] { new Vector4(-0.005f, r0, r0 * 0.88f, 0f), new Vector4(l * 0.75f, r1, r1 * 0.85f, 0f), new Vector4(l + 0.002f, r1 * 0.55f, r1 * 0.45f, -0.001f) };
                var tj = Part("F" + f + j, parent, at, Loft(rings, 5, s, 20f), skinMat);
                Quaternion rest = j == 0 ? Quaternion.Euler(0f, yaw[f] * s, 0f) : Quaternion.identity;
                tj.localRotation = rest;
                a.joints[f * 3 + j] = tj; a.jointRest[f * 3 + j] = rest;
                parent = tj; at = new Vector3(0f, 0f, l);
            }
        }
        // thumb
        {
            Vector3 dir = new Vector3(-0.55f * s, -0.25f, 0.8f), up = new Vector3(-1f * s, 0.5f, 0f);
            Quaternion rest0 = Quaternion.LookRotation(dir, up);
            float[] tl = { 0.040f, 0.032f, 0.026f }; float[] tr = { 0.0135f, 0.0105f, 0.0095f };
            Transform parent = a.hand; Vector3 at = new Vector3(-0.024f * s, -0.010f, 0.010f);
            for (int j = 0; j < 3; j++)
            {
                float l = tl[j], r0 = tr[j];
                var rings = j < 2
                    ? new[] { new Vector4(-0.006f, r0, r0 * 0.85f, 0f), new Vector4(l + 0.002f, r0 * 0.85f, r0 * 0.75f, 0f) }
                    : new[] { new Vector4(-0.005f, r0, r0 * 0.85f, 0f), new Vector4(l * 0.7f, r0 * 0.85f, r0 * 0.75f, 0f), new Vector4(l + 0.002f, r0 * 0.5f, r0 * 0.4f, -0.001f) };
                var tj = Part("T" + j, parent, at, Loft(rings, 5, s, 20f), skinMat);
                Quaternion rest = j == 0 ? rest0 : Quaternion.Euler(0f, -6f * s, 0f);
                tj.localRotation = rest;
                a.thumb[j] = tj; a.thumbRest[j] = rest;
                parent = tj; at = new Vector3(0f, 0f, l);
            }
        }
        return a;
    }

    static Transform Part(string name, Transform parent, Vector3 localPos, UnityEngine.Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    // rings: (z, radiusX, radiusY, centreY) along local +Z; s = -1 mirrors to the left side
    static UnityEngine.Mesh Loft(Vector4[] rings, int n, float s, float vScale)
    {
        var v = new System.Collections.Generic.List<Vector3>(); var nr = new System.Collections.Generic.List<Vector3>();
        var uv = new System.Collections.Generic.List<Vector2>(); var tri = new System.Collections.Generic.List<int>();
        for (int i = 0; i < rings.Length; i++)
            for (int j = 0; j <= n; j++)
            {
                float ang = (j / (float)n) * Mathf.PI * 2f + Mathf.PI * 0.5f;
                float cx = Mathf.Cos(ang), sy = Mathf.Sin(ang);
                v.Add(new Vector3(cx * rings[i].y * s, rings[i].w + sy * rings[i].z, rings[i].x));
                nr.Add(new Vector3(cx / Mathf.Max(rings[i].y, 1e-4f) * s, sy / Mathf.Max(rings[i].z, 1e-4f), 0f).normalized);
                uv.Add(new Vector2(j / (float)n, rings[i].x * vScale));
            }
        int row = n + 1;
        for (int i = 0; i < rings.Length - 1; i++)
            for (int j = 0; j < n; j++)
            {
                int a0 = i * row + j, a1 = a0 + 1, b0 = a0 + row, b1 = b0 + 1;
                AddTri(tri, v, a0, b0, b1, (v[a0] + v[b1]) * 0.5f - new Vector3(0f, rings[i].w, (v[a0].z + v[b1].z) * 0.5f));
                AddTri(tri, v, a0, b1, a1, (v[a0] + v[b1]) * 0.5f - new Vector3(0f, rings[i].w, (v[a0].z + v[b1].z) * 0.5f));
            }
        // caps
        for (int e = 0; e < 2; e++)
        {
            int ri = e == 0 ? 0 : rings.Length - 1;
            Vector3 axisN = e == 0 ? Vector3.back : Vector3.forward;
            int c = v.Count;
            v.Add(new Vector3(0f, rings[ri].w, rings[ri].x)); nr.Add(axisN); uv.Add(new Vector2(0.5f, rings[ri].x * vScale));
            for (int j = 0; j < n; j++) { v.Add(v[ri * row + j]); nr.Add(axisN); uv.Add(uv[ri * row + j]); }
            for (int j = 0; j < n; j++) AddTri(tri, v, c, c + 1 + j, c + 1 + (j + 1) % n, axisN);
        }
        var m = new UnityEngine.Mesh();
        m.SetVertices(v); m.SetNormals(nr); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
        m.RecalculateBounds();
        return m;
    }

    static void AddTri(System.Collections.Generic.List<int> tri, System.Collections.Generic.List<Vector3> v, int a, int b, int c, Vector3 outward)
    {
        Vector3 nrm = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
        if (Vector3.Dot(nrm, outward) < 0f) { int t = b; b = c; c = t; }
        tri.Add(a); tri.Add(b); tri.Add(c);
    }

    static Texture2D MakeTex(Color baseCol, int kind)
    {
        var t = new Texture2D(32, 32, TextureFormat.RGB24, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        var rnd = new System.Random(kind * 7919);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float n = 0.9f + (float)rnd.NextDouble() * 0.16f;
                if (kind == 2) n *= (x % 4 < 2 ? 1.0f : 0.82f) * (y % 8 == 0 ? 0.9f : 1f);          // knit ribs
                else n *= 1f - 0.08f * Mathf.Clamp01(Mathf.Sin(y * 0.6f) - 0.6f);                    // faint skin creases
                t.SetPixel(x, y, baseCol * n);
            }
        t.Apply();
        return t;
    }

    static Material MakeMat(Texture2D tex, float smooth)
    {
        var sh = Shader.Find("Mandela/PSX Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(sh);
        m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
        return m;
    }
}
