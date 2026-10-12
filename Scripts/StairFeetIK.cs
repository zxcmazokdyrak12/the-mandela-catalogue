using UnityEngine;

// Plants the feet where StairsStalker says, turns the head toward the player and tilts it.
[RequireComponent(typeof(Animator))]
public class StairFeetIK : MonoBehaviour
{
    public Vector3 left, right;
    [Range(0f, 1f)] public float weight = 1f;
    public Transform lookTarget;
    [Range(0f, 1f)] public float lookWeight = 0.7f;
    public float headTilt;
    Animator anim; Transform head;

    void Awake() { anim = GetComponent<Animator>(); head = anim.GetBoneTransform(HumanBodyBones.Head); }

    void OnAnimatorIK(int layer)
    {
        Quaternion fr = Quaternion.LookRotation(transform.forward, Vector3.up);
        anim.SetIKPositionWeight(AvatarIKGoal.LeftFoot, weight); anim.SetIKPosition(AvatarIKGoal.LeftFoot, left);
        anim.SetIKRotationWeight(AvatarIKGoal.LeftFoot, weight); anim.SetIKRotation(AvatarIKGoal.LeftFoot, fr);
        anim.SetIKPositionWeight(AvatarIKGoal.RightFoot, weight); anim.SetIKPosition(AvatarIKGoal.RightFoot, right);
        anim.SetIKRotationWeight(AvatarIKGoal.RightFoot, weight); anim.SetIKRotation(AvatarIKGoal.RightFoot, fr);
        // knees forward
        anim.SetIKHintPositionWeight(AvatarIKHint.LeftKnee, weight * 0.8f); anim.SetIKHintPosition(AvatarIKHint.LeftKnee, left + transform.forward * 0.5f + Vector3.up * 0.5f);
        anim.SetIKHintPositionWeight(AvatarIKHint.RightKnee, weight * 0.8f); anim.SetIKHintPosition(AvatarIKHint.RightKnee, right + transform.forward * 0.5f + Vector3.up * 0.5f);
        anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f); anim.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
        if (lookTarget && lookWeight > 0f) { anim.SetLookAtWeight(lookWeight, 0.15f, 1f, 1f, 0.6f); anim.SetLookAtPosition(lookTarget.position); }
        else anim.SetLookAtWeight(0f);
    }

    void LateUpdate()
    {
        if (head && Mathf.Abs(headTilt) > 0.01f) head.rotation = Quaternion.AngleAxis(headTilt, transform.forward) * head.rotation;
    }
}
