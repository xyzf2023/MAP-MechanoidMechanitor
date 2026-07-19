using System;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidOvermindThingSpec : IEquatable<MechanoidOvermindThingSpec>
    {
        public ThingDef Def { get; }

        public ThingDef? Stuff { get; }

        public bool HasQuality { get; }

        public QualityCategory Quality { get; }

        public MechanoidOvermindThingSpec(
            ThingDef def,
            ThingDef? stuff,
            bool hasQuality,
            QualityCategory quality)
        {
            Def = def ?? throw new ArgumentNullException(nameof(def));
            Stuff = stuff;
            HasQuality = hasQuality;
            Quality = hasQuality ? quality : QualityCategory.Normal;
        }

        public static MechanoidOvermindThingSpec CreateDefault(ThingDef def)
        {
            if (def == null)
            {
                throw new ArgumentNullException(nameof(def));
            }

            ThingDef? stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            bool hasQuality = def.HasComp(typeof(CompQuality));
            return new MechanoidOvermindThingSpec(
                def,
                stuff,
                hasQuality,
                QualityCategory.Normal);
        }

        public bool Equals(MechanoidOvermindThingSpec? other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (Def != other.Def || Stuff != other.Stuff || HasQuality != other.HasQuality)
            {
                return false;
            }

            if (!HasQuality)
            {
                return true;
            }

            return Quality == other.Quality;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as MechanoidOvermindThingSpec);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Def != null ? Def.shortHash : 0;
                hash = (hash * 397) ^ (Stuff != null ? Stuff.shortHash : 0);
                hash = (hash * 397) ^ HasQuality.GetHashCode();
                if (HasQuality)
                {
                    hash = (hash * 397) ^ (int)Quality;
                }

                return hash;
            }
        }

        public static bool operator ==(
            MechanoidOvermindThingSpec? left,
            MechanoidOvermindThingSpec? right)
        {
            if (left is null)
            {
                return right is null;
            }

            return left.Equals(right);
        }

        public static bool operator !=(
            MechanoidOvermindThingSpec? left,
            MechanoidOvermindThingSpec? right)
        {
            return !(left == right);
        }
    }
}
