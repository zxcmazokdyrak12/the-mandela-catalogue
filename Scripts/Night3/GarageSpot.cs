using UnityEngine;

public enum GarageSpotType { Search, Jerrycan, FuelCap, Chain, HouseDoor, Car, Ignition }

// Something in the garage you can look at and click (night 3).
public class GarageSpot : MonoBehaviour
{
    public GarageSpotType type;
    public string label;
    [Tooltip("Where Mark stands to use it (index into GaragePlayer.stations).")]
    public int station;
    [Tooltip("Search spots only: id used to hide the car keys.")]
    public int searchId;
    [HideInInspector] public bool searched;
}
