using UnityEngine;

namespace GearboxDemo
{
    /// <summary>CAD-derived simulation approximation, not manufacturer-calibrated kinematics.</summary>
    public sealed class YaskawaSimulationRig : MonoBehaviour
    {
        public string configuration="CAD-estimated pivots; approximate limits, masses and collision envelopes. Simulation only.";
    }
}
