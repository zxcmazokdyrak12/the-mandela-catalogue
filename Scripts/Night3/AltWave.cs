using UnityEngine;

// The ending of night 3: she stands in the garage door and slowly waves goodbye with her right hand,
// her head tilted too far. Needs IK Pass on the animator layer.
[RequireComponent(typeof(Animator))]
public class AltWave : MonoBehaviour
{
    public float waveSpeed = 1.6f;      // waves per second (slow and wrong)
    public float headTilt = 48f;
    public float headTwitch = 6f;
    public Transform lookAt;            // optional: the player's camera
    float w;
    Animator anim; Transform head;

    void Awake() { anim = GetComponent<Animator>(); head = anim.GetBoneTransform(HumanBodyBones.Head); }
    void OnEnable() { w = 0f; }

    void Update() { w = Mathf.MoveTowards(w, 1f, Time.deltaTime * 1.5f); }

    void OnAnimatorIK(int layer)
    {
        Vector3 up = Vector3.up, right = transform.right, fwd = transform.forward;
        Vector3 basePos = transform.position;
        float s = Mathf.Sin(Time.time * waveSpeed * Mathf.PI * 2f);
        Vector3 hand = basePos + up * 1.72f + right * (0.32f + s * 0.12f) + fwd * 0.18f;
        anim.SetIKPositionWeight(AvatarIKGoal.RightHand, w);
        anim.SetIKPosition(AvatarIKGoal.RightHand, hand);
        anim.SetIKRotationWeight(AvatarIKGoal.RightHand, w * 0.8f);
        anim.SetIKRotation(AvatarIKGoal.RightHand, Quaternion.LookRotation(fwd, up) * Quaternion.Euler(0f, 0f, s * 25f - 90f));
        anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, w * 0.8f);
        anim.SetIKHintPosition(AvatarIKHint.RightElbow, basePos + up * 1.3f + right * 0.55f);
        anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
        if (lookAt) { anim.SetLookAtWeight(w * 0.8f, 0.1f, 1f, 1f, 0.5f); anim.SetLookAtPosition(lookAt.position); }
        else anim.SetLookAtWeight(0f);
    }

    void LateUpdate()
    {
        if (!head) return;
        float tw = (Mathf.PerlinNoise(Time.time * 19f, 4.7f) - 0.5f) * 2f * headTwitch;
        head.rotation = Quaternion.AngleAxis((headTilt + tw) * w, transform.forward) * head.rotation;
    }
}
