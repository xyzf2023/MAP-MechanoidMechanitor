using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class Dialog_MechServiceStationPolicy : Window
    {
        private readonly CompMechServiceStation station;
        private static readonly MechServiceStationMode[] Modes =
        {
            MechServiceStationMode.AssignedOnly, MechServiceStationMode.MechanitorOnly,
            MechServiceStationMode.AsNeeded, MechServiceStationMode.ChargeFirst,
            MechServiceStationMode.RepairFirst
        };

        public override Vector2 InitialSize => new Vector2(520f, 650f);

        public Dialog_MechServiceStationPolicy(CompMechServiceStation station)
        {
            this.station = station;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            doCloseButton = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (!station.parent.Spawned || station.parent.Faction != Faction.OfPlayer)
            {
                Close();
                return;
            }
            GameFont oldFont = Text.Font;
            try
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0f, 0f, inRect.width - 30f, 35f), "MAP_MechServiceStation.Policy.Title".Translate());
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(0f, 43f, inRect.width, 28f), "MAP_MechServiceStation.Policy.Hint".Translate());
                float y = 80f;
                foreach (MechServiceStationMode mode in Modes)
                {
                    if (Widgets.RadioButtonLabeled(new Rect(0f, y, inRect.width, 30f),
                        MechServicePolicyUtility.Label(mode), station.Mode == mode)) station.SetMode(mode);
                    y += 34f;
                }

                // 保留旧存档的指定优先模式，不改变枚举值或强制迁移既有策略。
                if (station.Mode == MechServiceStationMode.AssignedPriority)
                {
                    Widgets.Label(new Rect(0f, y, inRect.width, 28f), "MAP_MechServiceStation.Policy.LegacyAssignedPriority".Translate());
                    y += 34f;
                }
                y += 10f;
                if (station.Mode == MechServiceStationMode.AssignedOnly
                    || station.Mode == MechServiceStationMode.AssignedPriority)
                {
                    if (Widgets.ButtonText(new Rect(0f, y, inRect.width, 32f),
                        "MAP_MechServiceStation.Policy.AssignedPawn".Translate(
                            station.AssignedPawn?.LabelShortCap.ToString() ?? "MAP_MechServiceStation.Policy.Unassigned".Translate().ToString())))
                        station.OpenAssignmentMenu();
                    y += 40f;
                }
                if (MechServicePolicyUtility.SupportsMechanitorPriority(station.Mode))
                {
                    bool priority = station.MechanitorPriority;
                    Widgets.CheckboxLabeled(new Rect(0f, y, inRect.width, 30f), "MAP_MechServiceStation.Policy.MechanitorPriority".Translate(), ref priority);
                    station.SetMechanitorPriority(priority);
                    y += 36f;
                    Widgets.Label(new Rect(0f, y, inRect.width, 55f), "MAP_MechServiceStation.Policy.MechanitorPriorityHint".Translate());
                    y += 63f;
                }
                bool standby = station.StandbyAfterService;
                Rect standbyRect = new Rect(0f, y, inRect.width, 30f);
                Widgets.CheckboxLabeled(standbyRect, "MAP_MechServiceStation.Policy.StandbyAfterService".Translate(), ref standby);
                TooltipHandler.TipRegion(standbyRect, "MAP_MechServiceStation.Policy.StandbyAfterServiceHint".Translate());
                station.SetStandbyAfterService(standby);
                y += 36f;
                bool repair = station.RepairEnabled;
                bool charge = station.ChargingEnabled;
                Widgets.CheckboxLabeled(new Rect(0f, y, inRect.width, 30f), "MAP_MechServiceStation.Policy.EnableRepair".Translate(), ref repair);
                y += 36f;
                Widgets.CheckboxLabeled(new Rect(0f, y, inRect.width, 30f), "MAP_MechServiceStation.Policy.EnableCharging".Translate(), ref charge);
                station.SetServiceFunctions(repair, charge);
            }
            finally { Text.Font = oldFont; }
        }
    }
}
