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
                Widgets.Label(new Rect(0f, 0f, inRect.width - 30f, 35f), "机体整备台策略");
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(0f, 43f, inRect.width, 28f), "选择使用模式，修改立即生效。");
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
                    Widgets.Label(new Rect(0f, y, inRect.width, 28f), "当前沿用旧策略：指定机械族优先");
                    y += 34f;
                }
                y += 10f;
                if (station.Mode == MechServiceStationMode.AssignedOnly
                    || station.Mode == MechServiceStationMode.AssignedPriority)
                {
                    if (Widgets.ButtonText(new Rect(0f, y, inRect.width, 32f),
                        "指定机械族：" + (station.AssignedPawn?.LabelShortCap.ToString() ?? "未指定")))
                        station.OpenAssignmentMenu();
                    y += 40f;
                }
                if (MechServicePolicyUtility.SupportsMechanitorPriority(station.Mode))
                {
                    bool priority = station.MechanitorPriority;
                    Widgets.CheckboxLabeled(new Rect(0f, y, inRect.width, 30f), "机械族机械师优先", ref priority);
                    station.SetMechanitorPriority(priority);
                    y += 36f;
                    Widgets.Label(new Rect(0f, y, inRect.width, 55f), "有对应整备需求的机械族机械师可优先使用，并抢占普通自动使用者；手动命令优先。");
                    y += 63f;
                }
                bool standby = station.StandbyAfterService;
                Rect standbyRect = new Rect(0f, y, inRect.width, 30f);
                Widgets.CheckboxLabeled(standbyRect, "完成后待命", ref standby);
                TooltipHandler.TipRegion(standbyRect, "开启后，使用该整备台的机械族将在整备完成后待命。");
                station.SetStandbyAfterService(standby);
                y += 36f;
                bool repair = station.RepairEnabled;
                bool charge = station.ChargingEnabled;
                Widgets.CheckboxLabeled(new Rect(0f, y, inRect.width, 30f), "启用修复功能", ref repair);
                y += 36f;
                Widgets.CheckboxLabeled(new Rect(0f, y, inRect.width, 30f), "启用充电功能", ref charge);
                station.SetServiceFunctions(repair, charge);
            }
            finally { Text.Font = oldFont; }
        }
    }
}
