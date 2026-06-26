using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HairRenderExtension : DefModExtension
    {
        public string? backTexPath;

        public Vector2? meshSize;

        public Vector2 drawSize = Vector2.one;

        public Vector3 offsetNorth = Vector3.zero;
        public Vector3 offsetEast = Vector3.zero;
        public Vector3 offsetSouth = Vector3.zero;
        public Vector3 offsetWest = Vector3.zero;

        public Vector3 OffsetFor(Rot4 rot)
        {
            return rot.AsInt switch
            {
                0 => offsetNorth,
                1 => offsetEast,
                2 => offsetSouth,
                3 => offsetWest,
                _ => Vector3.zero
            };
        }
    }
}
