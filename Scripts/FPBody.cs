using UnityEngine;

// Mixamo body seen from the eyes: head hidden, body glued under the camera,
// clips cross-faded from PanicPlayer, hands placed with IK for the flashlight / covering ears.
[RequireComponent(typeof(Animator))]
public class FPBody : MonoBehaviour
{
    public Transform cam;
    public Transform flashlightProp;          // parented to the left hand
    public Vector3 headOffset = new Vector3(0f, -0.07f, -0.11f);   // where the head bone sits relative to the camera
    [Header("Flashlight grip (camera space)")]
    public Vector3 flashHandPos = new Vector3(-0.16f, -0.12f, 0.34f);   // wrist target: left, below, ahead of the eyes
    public Vector3 flashElbowHint = new Vector3(-0.35f, -0.45f, 0.1f);
    public Vector3 gripPalm = new Vector3(0.1f, -0.95f, 0.2f);          // camera space: palm faces down onto the barrel
    public Vector3 gripSide = new Vector3(-0.35f, 0f, -0.94f);          // camera space: little finger -> index finger
    public float gripCurl = 1f;                                          // 0 = open hand, 1 = fist around the barrel
    public Vector3 propGrip = Vector3.zero;                              // fine offset of the barrel inside the fist (camera space)
    public float lensOut = 0.08f;                                        // how far the lens pokes out of the fist
    public float lensFromCentre = 0.115f;                                // lens distance from the prop pivot (unscaled)
    public float propScale = 1.1f;
    [HideInInspector] public bool flashIK, earsIK, handsIK;
    [HideInInspector] public Vector3 handLTarget, handRTarget;

    Animator anim;
    Transform head, handL;
    float flashW, earW, handsW;

    void Awake()
    {
        anim = GetComponent<Animator>();
        anim.applyRootMotion = false;
        head = anim.GetBoneTransform(HumanBodyBones.Head);
        handL = anim.GetBoneTransform(HumanBodyBones.LeftHand);
    }

    public void Play(string state, float fade = 0.12f, float startTime = 0f, float speed = 1f)
    {
        if (!anim) return;
        anim.speed = speed;
        anim.CrossFadeInFixedTime(state, fade, 0, startTime);
    }

    void OnAnimatorIK(int layer)
    {
        if (!cam) return;
        float dt = Time.deltaTime;
        flashW = Mathf.MoveTowards(flashW, flashIK && !earsIK ? 1f : 0f, dt * 6f);
        earW = Mathf.MoveTowards(earW, earsIK ? 1f : 0f, dt * 7f);
        handsW = Mathf.MoveTowards(handsW, handsIK ? 1f : 0f, dt * 12f);
        Vector3 f = cam.forward, r = cam.right, u = cam.up, c = cam.position;
        if (handsW > 0f && earW <= 0f)
        {
            anim.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            anim.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
            anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, handsW);
            anim.SetIKPositionWeight(AvatarIKGoal.RightHand, handsW);
            anim.SetIKPosition(AvatarIKGoal.LeftHand, handLTarget);
            anim.SetIKPosition(AvatarIKGoal.RightHand, handRTarget);
            return;
        }
        if (earW > 0f)
        {
            anim.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            anim.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
            anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, earW);
            anim.SetIKPositionWeight(AvatarIKGoal.RightHand, earW);
            anim.SetIKPosition(AvatarIKGoal.LeftHand, c - r * 0.16f + f * 0.12f - u * 0.02f);
            anim.SetIKPosition(AvatarIKGoal.RightHand, c + r * 0.16f + f * 0.12f - u * 0.02f);
        }
        else
        {
            anim.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, flashW);
            anim.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            anim.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, flashW * 0.8f);
            anim.SetIKPosition(AvatarIKGoal.LeftHand, c + r * flashHandPos.x + u * flashHandPos.y + f * flashHandPos.z);
            anim.SetIKHintPosition(AvatarIKHint.LeftElbow, c + r * flashElbowHint.x + u * flashElbowHint.y + f * flashElbowHint.z);
        }
    }

    // called by PanicPlayer at the very end of its LateUpdate (after the camera has moved)
    public void Align()
    {
        if (!cam) return;
        Vector3 fw = cam.forward; fw.y = 0f;
        if (fw.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(fw);
        head.localScale = Vector3.one * 0.0001f;
        Vector3 want = cam.position + transform.rotation * headOffset;
        transform.position += want - head.position;
        if (flashlightProp)
        {
            bool show = flashW > 0.3f;
            if (flashlightProp.gameObject.activeSelf != show) flashlightProp.gameObject.SetActive(show);
            if (flashW > 0.01f) { CurlFingers(flashW); OrientLeftHand(flashW); }
            flashlightProp.localScale = Vector3.one * propScale;
            float front; Vector3 hollow = FistHollow(out front);
            // lens sticks out lensOut metres in front of the foremost knuckle
            float along = Vector3.Dot(hollow - cam.position, cam.forward);
            Vector3 centre = hollow + cam.forward * (front + lensOut - lensFromCentre * propScale - along);
            centre += cam.right * propGrip.x + cam.up * propGrip.y + cam.forward * propGrip.z;
            flashlightProp.SetPositionAndRotation(centre, Quaternion.LookRotation(cam.forward, cam.up));
        }
    }

    // ---- left-hand grip ----
    // Fingers are curled in humanoid muscle space (anatomically correct for any rig),
    // then the whole hand is turned in world space so the fist wraps the barrel, and the
    // flashlight is placed in the hollow of the fist with its lens sticking out.
    HumanPoseHandler poseHandler;
    HumanPose pose;
    int[] gripMuscles; float[] gripTargets;

    void InitGrip()
    {
        if (gripMuscles != null || !anim.avatar || !anim.avatar.isHuman) return;
        poseHandler = new HumanPoseHandler(anim.avatar, transform);
        var names = new System.Collections.Generic.List<string>(HumanTrait.MuscleName);
        var ids = new System.Collections.Generic.List<int>(); var tg = new System.Collections.Generic.List<float>();
        foreach (var f in new[] { "Index", "Middle", "Ring", "Little" })
        {
            Add(names, ids, tg, "Left " + f + " 1 Stretched", -0.55f);
            Add(names, ids, tg, "Left " + f + " 2 Stretched", -0.75f);
            Add(names, ids, tg, "Left " + f + " 3 Stretched", -0.6f);
            Add(names, ids, tg, "Left " + f + " Spread", 0f);
        }
        Add(names, ids, tg, "Left Thumb 1 Stretched", -0.3f);
        Add(names, ids, tg, "Left Thumb Spread", -0.2f);
        Add(names, ids, tg, "Left Thumb 2 Stretched", -0.4f);
        Add(names, ids, tg, "Left Thumb 3 Stretched", -0.5f);
        gripMuscles = ids.ToArray(); gripTargets = tg.ToArray();
    }

    static void Add(System.Collections.Generic.List<string> names, System.Collections.Generic.List<int> ids, System.Collections.Generic.List<float> tg, string n, float v)
    {
        int i = names.IndexOf(n);
        if (i >= 0) { ids.Add(i); tg.Add(v); }
    }

    void CurlFingers(float w)
    {
        InitGrip();
        if (poseHandler == null) return;
        // SetHumanPose re-roots the skeleton; remember where the body is and put it back afterwards
        Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        Vector3 rootPos = transform.position, hipsPos = hips.position; Quaternion rootRot = transform.rotation, hipsRot = hips.rotation;
        poseHandler.GetHumanPose(ref pose);
        for (int i = 0; i < gripMuscles.Length; i++)
            pose.muscles[gripMuscles[i]] = Mathf.Lerp(pose.muscles[gripMuscles[i]], gripTargets[i] * gripCurl, w);
        poseHandler.SetHumanPose(ref pose);
        transform.SetPositionAndRotation(rootPos, rootRot);
        hips.SetPositionAndRotation(hipsPos, hipsRot);
    }

    void OrientLeftHand(float w)
    {
        Transform mid = anim.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
        Transform idx = anim.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
        Transform lit = anim.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
        if (!mid || !idx || !lit) return;
        Vector3 fwd = (mid.position - handL.position).normalized;
        Vector3 side = (idx.position - lit.position).normalized;      // little -> index
        Vector3 palm = -Vector3.Cross(fwd, side).normalized;          // left hand: direction the palm faces
        Vector3 wPalm = cam.TransformDirection(gripPalm).normalized;
        Vector3 wSide = cam.TransformDirection(gripSide).normalized;
        Vector3 wFwd = Vector3.Cross(wPalm, wSide).normalized;
        Quaternion delta = Quaternion.LookRotation(wFwd, wPalm) * Quaternion.Inverse(Quaternion.LookRotation(fwd, palm));
        handL.rotation = Quaternion.Slerp(Quaternion.identity, delta, w) * handL.rotation;
    }

    // centre of the hollow inside the fist, in world space
    Vector3 FistHollow(out float frontmost)
    {
        Vector3 sum = Vector3.zero; int n = 0; frontmost = -1e9f;
        foreach (var b in new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftLittleProximal,
                                  HumanBodyBones.LeftIndexDistal, HumanBodyBones.LeftMiddleDistal, HumanBodyBones.LeftRingDistal, HumanBodyBones.LeftLittleDistal })
        {
            Transform t = anim.GetBoneTransform(b); if (!t) continue;
            sum += t.position; n++;
            frontmost = Mathf.Max(frontmost, Vector3.Dot(t.position - cam.position, cam.forward));
        }
        Vector3 c = n > 0 ? sum / n : handL.position;
        // pull toward the palm a little
        return Vector3.Lerp(c, handL.position, 0.15f);
    }
}
