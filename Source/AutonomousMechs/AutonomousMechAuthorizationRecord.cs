using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [Flags]
    public enum AutonomousMechAuthorizationSource
    {
        None = 0,
        Independent = 1,
        MechanitorIdentity = 2,
        LegacyNode = 4,
        InnateComp = 8
    }

    /// <summary>自律资格、个人充电设置与两档行为模式；不保存机械师身份或控制组。</summary>
    public sealed class AutonomousMechAuthorizationRecord : IExposable
    {
        private Pawn? pawn;
        private AutonomousMechAuthorizationSource sources;
        private bool selfShutdown;
        private FloatRange rechargeThresholds = MechanitorControlGroup.DefaultMechRechargeThresholds;

        public Pawn? Pawn => pawn;
        public AutonomousMechAuthorizationSource Sources => sources;
        public FloatRange RechargeThresholds => rechargeThresholds;
        public bool SelfShutdown => selfShutdown;
        public bool HasIndependentAuthorization =>
            (sources & AutonomousMechAuthorizationSource.Independent) != 0;

        public AutonomousMechAuthorizationRecord() { }

        internal AutonomousMechAuthorizationRecord(Pawn pawn, FloatRange thresholds)
        {
            this.pawn = pawn;
            SetRechargeThresholds(thresholds);
        }

        internal void SetSources(AutonomousMechAuthorizationSource value) => sources = value;
        internal void SetSelfShutdown(bool value) => selfShutdown = value;

        internal void SetRechargeThresholds(FloatRange value) =>
            rechargeThresholds = MechanoidMechanitorRechargeUtility.SanitizeRechargeThresholds(value);

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref sources, "sources");
            Scribe_Values.Look(ref selfShutdown, "selfShutdown", false);
            Scribe_Values.Look(ref rechargeThresholds, "rechargeThresholds",
                MechanitorControlGroup.DefaultMechRechargeThresholds);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                sources &= AutonomousMechAuthorizationSource.Independent
                    | AutonomousMechAuthorizationSource.MechanitorIdentity
                    | AutonomousMechAuthorizationSource.LegacyNode
                    | AutonomousMechAuthorizationSource.InnateComp;
                SetRechargeThresholds(rechargeThresholds);
            }
        }
    }
}
