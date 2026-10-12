using UnityEngine;

public enum HotspotType { Door, Window, TV, Stairs, Wardrobe, Plug, SofaReturn }

// Invisible trigger box the player can click on.
public class Hotspot : MonoBehaviour
{
    public HotspotType type;
    public string label;
}
