using UnityEngine;

// Sectional roll-up garage door. Panels run up the track and then lie flat under the ceiling.
// The root sits at the bottom centre of the opening; local -Z points into the garage.
public class GarageGate : MonoBehaviour
{
    public Transform[] panels;          // index 0 = bottom panel
    [Range(0f, 1f)] public float open;  // raised by the chain
    public float yank;                  // extra lift from outside (metres), set by the director
    public float height = 2.2f;
    public float panelH = 0.44f;
    public float trackDepth = 0.12f;    // how far behind the opening the horizontal run starts

    public float Gap => open * height + yank;   // height of the gap under the door

    void LateUpdate()
    {
        if (panels == null) return;
        float lift = Gap;
        for (int i = 0; i < panels.Length; i++)
        {
            var p = panels[i]; if (!p) continue;
            float s = i * panelH + lift + panelH * 0.5f;     // distance of the panel centre along the track
            if (s <= height)
            {
                p.localPosition = new Vector3(0f, s, 0f);
                p.localRotation = Quaternion.identity;
            }
            else
            {
                float along = s - height;
                // a short bend, then flat under the ceiling
                float bend = Mathf.Clamp01(along / 0.3f);
                p.localPosition = new Vector3(0f, height + Mathf.Sin(bend * Mathf.PI * 0.5f) * 0.18f, -trackDepth - along);
                p.localRotation = Quaternion.Euler(-90f * bend, 0f, 0f);
            }
        }
    }
}
