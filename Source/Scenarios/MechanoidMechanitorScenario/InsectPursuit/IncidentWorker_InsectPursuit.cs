using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 虫族追猎（MAP_InsectPursuit）的正式 IncidentWorker。
    /// 极薄：只负责把原版 Do incident 或外部强制触发转发到
    /// GameComponent_MechanoidMechanitorInsectPursuitManager 的统一业务入口。
    /// 不实现任何 Pursuit 调度逻辑（每日概率/保护期/隐藏预约/共享冷却/全局互斥
    /// 等仍完全由 Manager 负责）。
    /// </summary>
    public sealed class IncidentWorker_InsectPursuit : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!(parms.target is Map map))
            {
                return false;
            }

            GameComponent_MechanoidMechanitorInsectPursuitManager? manager =
                GameComponent_MechanoidMechanitorInsectPursuitManager.GetManager();

            return manager != null && manager.CanStartHuntOn(map);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(parms.target is Map map))
            {
                return false;
            }

            GameComponent_MechanoidMechanitorInsectPursuitManager? manager =
                GameComponent_MechanoidMechanitorInsectPursuitManager.GetManager();

            if (manager == null)
            {
                return false;
            }

            // 不依赖 CanFireNowSub 是否被提前调用：自身再次通过 Manager 统一业务入口校验。
            // 原版 IncidentWorker.TryExecute 成功后才会 RecordIncidentFired，
            // 让“虫族追猎”成为正式 RimWorld Incident。
            return manager.TryStartHuntOn(map, refreshCooldown: true);
        }
    }
}
