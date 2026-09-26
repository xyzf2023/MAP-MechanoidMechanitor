using System;
using System.Reflection;
using GD3;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    /// <summary>
    /// 短波接收器运行时兼容：
    /// 肃清指令开启时禁止扫描；机械巢实际为盟友时阻止敌对集群任务并改为对应扫描结果。
    /// </summary>
    internal static class ClusterReceiverCompatibilityPatch
    {
        private const int TicksPerDay = 60000;

        private static FieldInfo? delayTicksField;
        private static ResearchProjectDef? mediumResearch;
        private static ResearchProjectDef? largeResearch;
        private static ResearchProjectDef? ultraResearch;

        internal static void Configure(
            FieldInfo resolvedDelayTicksField,
            ResearchProjectDef medium,
            ResearchProjectDef large,
            ResearchProjectDef ultra)
        {
            delayTicksField = resolvedDelayTicksField;
            mediumResearch = medium;
            largeResearch = large;
            ultraResearch = ultra;
        }

        internal static bool CanBeUsedByPrefix(ref AcceptanceReport __result)
        {
            if (!GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive)
            {
                return true;
            }

            __result = new AcceptanceReport(
                "MAP_GD5.ClusterReceiver.PurgeDisabled".Translate());
            return false;
        }

        internal static bool DoEffectPrefix(CompUseEffect_Detection __instance)
        {
            // 读条过程中可能切换剧情路线，因此完成时必须再次按当前状态判断。
            if (GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive)
            {
                return false;
            }

            if (!IsMechHiveCurrentlyAllied())
            {
                return true;
            }

            try
            {
                if (!TryGetSelectedMark(__instance, out int mark))
                {
                    Log.Error(
                        "[MAP-机械族机械师] 闪耀世界毁灭者5巨型集群接收器兼容失败："
                        + "无法读取当前扫描等级。为避免生成盟友机械巢的敌对集群任务，本次扫描已取消。");
                    return false;
                }

                bool handled;
                switch (mark)
                {
                    case 0:
                        SendNoResponseLetter();
                        handled = true;
                        break;

                    case 1:
                        handled =
                            GlitterworldDestroyer5ResearchSupportUtility.EnsureResearchCompleted(
                                mediumResearch,
                                "机械巢盟友中型集群扫描");
                        break;

                    case 2:
                        handled =
                            GlitterworldDestroyer5ResearchSupportUtility.EnsureResearchCompleted(
                                largeResearch,
                                "机械巢盟友大型集群扫描");
                        break;

                    case 3:
                        handled =
                            GlitterworldDestroyer5ResearchSupportUtility.EnsureResearchCompleted(
                                ultraResearch,
                                "机械巢盟友超大型集群扫描");
                        break;

                    default:
                        Log.Error(
                            "[MAP-机械族机械师] 闪耀世界毁灭者5巨型集群接收器兼容失败："
                            + "扫描等级Mark="
                            + mark
                            + " 超出预期范围0~3。为避免生成盟友机械巢的敌对集群任务，本次扫描已取消。");
                        return false;
                }

                if (handled)
                {
                    ApplyOriginalCooldown(__instance);
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 闪耀世界毁灭者5巨型集群接收器兼容运行时异常。"
                    + "为避免生成盟友机械巢的敌对集群任务，本次扫描已取消。\n"
                    + ex);
                return false;
            }
        }

        private static bool IsMechHiveCurrentlyAllied()
        {
            Faction? player = Faction.OfPlayerSilentFail;
            Faction? mechHive =
                MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive();
            if (player == null || mechHive == null || mechHive == player)
            {
                return false;
            }

            return mechHive.RelationKindWith(player) == FactionRelationKind.Ally;
        }

        private static bool TryGetSelectedMark(CompUseEffect_Detection instance, out int mark)
        {
            mark = -1;
            if (instance == null)
            {
                return false;
            }

            CompReceiverSelect? compSelect = instance.CompSelect;
            if (compSelect == null)
            {
                return false;
            }

            mark = compSelect.Mark;
            return true;
        }

        private static void ApplyOriginalCooldown(CompUseEffect_Detection instance)
        {
            if (instance == null
                || delayTicksField == null)
            {
                throw new InvalidOperationException(
                    "短波接收器冷却字段尚未正确配置。");
            }

            int cooldownDays = GDSettings.DetectCooldown;
            if (cooldownDays < 0)
            {
                throw new InvalidOperationException(
                    "GDSettings.DetectCooldown不是有效的非负整数。");
            }

            long cooldownTicks = (long)TicksPerDay * cooldownDays;
            if (cooldownTicks > int.MaxValue)
            {
                throw new OverflowException(
                    "GDSettings.DetectCooldown换算后的tick数超出Int32范围。");
            }

            delayTicksField.SetValue(instance, (int)cooldownTicks);
        }

        private static void SendNoResponseLetter()
        {
            Find.LetterStack.ReceiveLetter(
                "MAP_GD5.ClusterReceiver.NoResponse.Title".Translate(),
                "MAP_GD5.ClusterReceiver.NoResponse.Text".Translate(),
                LetterDefOf.NeutralEvent);
        }
    }
}
