using System;
using System.Collections;
using UnityEngine;

// The hooded one on the stairs, walked down tread by tread by hand (no clip can walk real stairs):
// each foot is IK-planted on a tread, the swinging foot arcs down to the next one, the body sinks with it,
// then it stops... and stands there, looking at you, before the next step.
// While the light is on it it doesn't move at all. When it gives up it turns round and climbs back up into the dark.
// The stairs end on a small landing against the front wall, so it comes out of the upstairs hall along the wall,
// turns onto the stairs at the top, and on the way back it turns off the landing into the hall again.
public class StairsStalker : MonoBehaviour
{
    public GameObject figure;               // Alt_Hooded
    public GameObject alternatesGroup;
    public GameObject[] hideWhileWalking;   // the other alternates (groom)

    [Header("Stairs (treads rise 0.22 m per 0.2 m, 12 treads, landing on top)")]
    public float stairX = -1.1f;
    public float firstTreadZ = 2.435f, treadDepth = 0.2f, firstTreadY = 0.22f, riser = 0.22f;
    public int treads = 12;
    public float landingY = 2.85f;
    public float footSpread = 0.11f;
    public float ankleHeight = 0.085f;

    [Header("Upstairs hall (it walks along the front wall to / from the landing)")]
    public float hallZ = 4.87f;              // walking line on the landing, just off the front wall
    public float hallEndX = -0.15f;         // where it appears from / vanishes to (round the corner, out of sight from below)
    public float flatStride = 0.36f;
    public float flatStepTime = 0.5f;

    [Header("Timing")]
    public float stepTime = 0.75f;          // one step down
    public float retreatStepTime = 0.42f;   // climbing back up
    public float lookWeight = 0.75f;

    public Action<Vector3, float> OnStep;   // foot position, loudness 0..1

    Animator anim; AltReach reach; StairFeetIK ik;
    bool active, retreating, lit;
    int lIdx, rIdx;                 // tread index under each foot (-1, -2... = floor in front, treads = landing)
    Vector3 lPos, rPos;             // where the feet actually are
    float progress, target;
    Coroutine walker;

    public bool Active => active;
    public float Progress => progress;
    public Vector3 Chest => figure ? figure.transform.position + Vector3.up * 1.25f : Vector3.zero;

    int LowestIndex => -3;
    float TreadY(int k) => k < 0 ? 0f : k >= treads ? landingY : firstTreadY + riser * k;
    float TreadZ(int k) => k >= treads ? hallZ : firstTreadZ + treadDepth * k;
    Vector3 Side(bool left) => (left ? -1f : 1f) * figure.transform.right * footSpread;
    Vector3 FootAt(int k, bool left) => new Vector3(stairX, TreadY(k), TreadZ(k)) + Side(left);
    Vector3 FlatFoot(float x, bool left) => new Vector3(x, landingY, hallZ) + Side(left) * 0.8f;

    void Init()
    {
        if (anim || !figure) return;
        anim = figure.GetComponent<Animator>();
        reach = figure.GetComponent<AltReach>();
        ik = figure.GetComponent<StairFeetIK>();
        if (!ik) ik = figure.AddComponent<StairFeetIK>();
    }

    public void Begin()
    {
        Init();
        if (!figure) return;
        StopAllCoroutines();
        if (alternatesGroup) alternatesGroup.SetActive(true);
        if (hideWhileWalking != null) foreach (var g in hideWhileWalking) if (g) g.SetActive(false);
        figure.SetActive(true);
        foreach (var r in figure.GetComponentsInChildren<Renderer>()) r.enabled = true;
        reach = figure.GetComponent<AltReach>();
        if (reach) reach.enabled = false;                   // feet + head are driven by StairFeetIK now
        anim.applyRootMotion = false; anim.speed = 1f;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        anim.Play("StairIdle", 0, UnityEngine.Random.value);
        var cam = Camera.main;
        ik.enabled = true; ik.weight = 1f; ik.lookTarget = cam ? cam.transform : null; ik.lookWeight = lookWeight; ik.headTilt = 6f;
        // it comes out of the hall, along the front wall, towards the top of the stairs
        figure.transform.rotation = Quaternion.LookRotation(Vector3.left);
        lIdx = rIdx = treads; progress = 0f; target = treads;
        active = true; retreating = false; lit = false;
        lPos = FlatFoot(hallEndX, true); rPos = FlatFoot(hallEndX, false);
        Pose(lPos, rPos);
        walker = StartCoroutine(Arrive());
    }

    IEnumerator Arrive()
    {
        yield return WalkFlat(hallEndX, stairX, flatStepTime, true);
        // turn to face down the stairs
        yield return TurnOnLanding(Vector3.back, 0.7f, true);
        yield return Descend();
    }

    // turn on the spot at the top, shuffling the feet round with the body
    IEnumerator TurnOnLanding(Vector3 dir, float dur, bool freezeInLight)
    {
        Quaternion from = figure.transform.rotation, to = Quaternion.LookRotation(dir);
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur)
        {
            if (freezeInLight && lit) { yield return null; t -= Time.deltaTime / dur; continue; }
            float e = t * t * (3f - 2f * t);
            figure.transform.rotation = Quaternion.Slerp(from, to, e);
            Vector3 l = FootAt(treads, true), r = FootAt(treads, false);
            // lift one foot then the other while turning
            l += Vector3.up * Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 2f)) * 0.06f;
            r += Vector3.up * Mathf.Max(0f, -Mathf.Sin(t * Mathf.PI * 2f)) * 0.06f;
            Pose(l, r);
            yield return null;
        }
        figure.transform.rotation = to;
        lIdx = rIdx = treads;
        lPos = FootAt(treads, true); rPos = FootAt(treads, false);
        Pose(lPos, rPos);
        OnStep?.Invoke(lPos, 0.35f);
    }

    // walk along the landing / hall floor, foot over foot
    IEnumerator WalkFlat(float x0, float x1, float stepDur, bool freezeInLight)
    {
        float dir = Mathf.Sign(x1 - x0);
        float lx = x0, rx = x0; bool left = true;
        for (int guard = 0; guard < 40; guard++)
        {
            float cur = left ? lx : rx, other = left ? rx : lx;
            if (Mathf.Abs(cur - x1) < 0.001f && Mathf.Abs(other - x1) < 0.001f) break;
            float to = other + dir * flatStride;
            if (dir * (to - x1) > 0f) to = x1;
            yield return StepTo(left, FlatFoot(to, left), stepDur, 0.09f, freezeInLight);
            if (left) lx = to; else rx = to;
            left = !left;
        }
    }

    // body follows the feet: hips over the middle, height from the lower foot
    void Pose(Vector3 l, Vector3 r)
    {
        ik.left = l + Vector3.up * ankleHeight; ik.right = r + Vector3.up * ankleHeight;
        Vector3 mid = (l + r) * 0.5f;
        float y = Mathf.Min(l.y, r.y) + Mathf.Abs(l.y - r.y) * 0.25f;
        figure.transform.position = new Vector3(mid.x, y, mid.z);
    }

    // called every frame by the director
    public void Tick(float p, bool isLit)
    {
        if (!active || retreating) return;
        progress = p; lit = isLit;
        target = Mathf.Lerp(treads, LowestIndex, p);
        ik.headTilt = Mathf.MoveTowards(ik.headTilt, lit ? 26f : 6f, Time.deltaTime * (lit ? 45f : 12f));
    }

    IEnumerator Descend()
    {
        bool leftNext = true;
        while (active && !retreating)
        {
            int front = Mathf.Min(lIdx, rIdx);
            if (lit || front <= LowestIndex || front - 0.5f < target) { yield return null; continue; }
            // swing the back foot past the front one, one tread lower
            bool left = leftNext; leftNext = !leftNext;
            int to = front - 1;
            yield return Step(left, to, stepTime, 0.13f, true);
            // and wait. Longer near the top, shorter as it gets closer.
            float pause = Mathf.Lerp(0.6f, 0.15f, progress) * UnityEngine.Random.Range(0.6f, 1.5f);
            for (float t = 0f; t < pause; t += Time.deltaTime) yield return null;
        }
    }

    IEnumerator Step(bool left, int to, float dur, float lift, bool down)
    {
        yield return StepTo(left, FootAt(to, left), dur, lift, down);
        if (left) lIdx = to; else rIdx = to;
        lPos = FootAt(lIdx, true); rPos = FootAt(rIdx, false);
        Pose(lPos, rPos);
    }

    // move one foot from where it is to b, the other one stays planted
    IEnumerator StepTo(bool left, Vector3 b, float dur, float lift, bool freezeInLight)
    {
        Vector3 a = left ? lPos : rPos, stay = left ? rPos : lPos;
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur)
        {
            if (lit && freezeInLight) { yield return null; t -= Time.deltaTime / dur; continue; }   // frozen mid-step in the light
            float e = t * t * (3f - 2f * t);
            Vector3 f = Vector3.Lerp(a, b, e) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * lift + Mathf.Max(0f, a.y - b.y) * Mathf.Sin(t * Mathf.PI) * 0.5f);
            if (left) Pose(f, stay); else Pose(stay, f);
            yield return null;
        }
        if (left) lPos = b; else rPos = b;
        Pose(lPos, rPos);
        OnStep?.Invoke(b, Mathf.Clamp01(0.45f + progress * 0.55f));
    }

    public void Retreat() { if (active && !retreating) { if (walker != null) StopCoroutine(walker); StartCoroutine(Climb()); } }

    IEnumerator Climb()
    {
        retreating = true; lit = false;
        // finish the step it was in, stand still a moment, then turn its back on you
        bool onLanding = lIdx >= treads && rIdx >= treads && Mathf.Abs((lPos.x + rPos.x) * 0.5f - stairX) > 0.05f;
        if (onLanding)
        {
            // it never reached the stairs: just walk back into the hall
            figure.transform.rotation = Quaternion.LookRotation(Vector3.right);
            float x = (lPos.x + rPos.x) * 0.5f;
            lPos = FlatFoot(x, true); rPos = FlatFoot(x, false); Pose(lPos, rPos);
            ik.lookWeight = 0f;
            yield return WalkFlat(x, hallEndX, flatStepTime * 0.8f, false);
            Hide(); yield break;
        }
        lPos = FootAt(lIdx, true); rPos = FootAt(rIdx, false);
        Pose(lPos, rPos);
        for (float t = 0f; t < 0.6f; t += Time.deltaTime) { ik.headTilt = Mathf.MoveTowards(ik.headTilt, 0f, Time.deltaTime * 30f); yield return null; }
        Quaternion from = figure.transform.rotation, to = Quaternion.LookRotation(Vector3.forward);
        Vector3 l0 = FootAt(lIdx, true), r0 = FootAt(rIdx, false);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.8f)
        {
            figure.transform.rotation = Quaternion.Slerp(from, to, t * t * (3f - 2f * t));
            ik.lookWeight = Mathf.Lerp(lookWeight, 0f, t);
            // shuffle the feet round with the body
            lPos = FootAt(lIdx, true); rPos = FootAt(rIdx, false);
            Pose(lPos, rPos);
            yield return null;
        }
        // climb: the lower foot goes up past the higher one
        bool leftNext = lIdx <= rIdx;
        while (Mathf.Min(lIdx, rIdx) < treads)
        {
            int high = Mathf.Max(lIdx, rIdx);
            bool left = leftNext; leftNext = !leftNext;
            int dest = Mathf.Min(high + 1, treads);
            if ((left ? lIdx : rIdx) == dest) continue;
            yield return Step(left, dest, retreatStepTime, 0.1f, false);
        }
        // at the top: turn off the landing towards the hall and walk away along the wall
        yield return TurnOnLanding(Vector3.right, 0.55f, false);
        yield return WalkFlat(stairX, hallEndX, flatStepTime * 0.8f, false);
        Hide();
    }

    // stop driving it (the death sequence takes over)
    public void Release()
    {
        StopAllCoroutines(); active = false; retreating = false;
        if (ik) ik.enabled = false;
        if (reach) reach.enabled = true;
    }

    public void Hide()
    {
        StopAllCoroutines();
        active = false; retreating = false;
        if (ik) ik.enabled = false;
        if (reach) reach.enabled = true;
        if (figure) figure.SetActive(false);
    }
}
