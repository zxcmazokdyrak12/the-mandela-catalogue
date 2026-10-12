using UnityEngine;

// Lets an alternate reach for the player's face and tilt its head, on top of whatever clip is playing.
// Used by DeathSequence. Needs IK Pass on the animator layer.
[RequireComponent(typeof(Animator))]
public class AltReach : MonoBehaviour
{
    public Transform target;            // the player's camera
    [Range(0f, 1f)] public float weight;
    public float headTilt;              // degrees, roll of the head
    public float headTwitch;            // degrees of random jitter
    Animator anim; Transform head;

    void Awake() { anim = GetComponent<Animator>(); head = anim.GetBoneTransform(HumanBodyBones.Head); }

    void OnAnimatorIK(int layer)
    {
        if (!target || weight <= 0f) { anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f); anim.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f); anim.SetLookAtWeight(0f); return; }
        Vector3 c = target.position, r = target.right, u = target.up, f = target.forward;
        anim.SetLookAtWeight(weight, 0.25f, 1f, 1f, 0.5f);
        anim.SetLookAtPosition(c);
        anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, weight);
        anim.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
        // hands come at your face from both sides, fingers almost touching the lens
        anim.SetIKPosition(AvatarIKGoal.RightHand, c - r * 0.3f - u * 0.2f + f * 0.32f);
        anim.SetIKPosition(AvatarIKGoal.LeftHand, c + r * 0.3f - u * 0.2f + f * 0.32f);
        anim.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, weight * 0.7f);
        anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, weight * 0.7f);
        anim.SetIKHintPosition(AvatarIKHint.RightElbow, transform.position + transform.right * 0.45f + Vector3.up * 1.25f);
        anim.SetIKHintPosition(AvatarIKHint.LeftElbow, transform.position - transform.right * 0.45f + Vector3.up * 1.25f);
    }

    void LateUpdate()
    {
        if (!head) return;
        float tw = headTwitch > 0f ? (Mathf.PerlinNoise(Time.time * 23f, 1.3f) - 0.5f) * 2f * headTwitch : 0f;
        if (Mathf.Abs(headTilt) > 0.01f || tw != 0f)
            head.rotation = Quaternion.AngleAxis(headTilt + tw, transform.forward) * head.rotation;
    }
}
