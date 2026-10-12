using UnityEngine;

public enum BedSpotType { Laptop, Desk, Papers }

// Clickable trigger in the bedroom (night 2).
public class BedSpot : MonoBehaviour
{
    public BedSpotType type;
    public string label;
}
