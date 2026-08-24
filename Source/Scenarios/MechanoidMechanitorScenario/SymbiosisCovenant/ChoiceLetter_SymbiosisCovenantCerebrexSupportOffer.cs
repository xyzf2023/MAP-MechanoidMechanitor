using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 两小时限时的「是否接受盟约援军」询问信件。
    /// 信件保存 offerId 与所属于的 Quest 引用；所有业务状态（已发送/已解决/超时）由 QuestPart 保存。
    /// 接受与婉拒都通过 offerId 精确定位对应 QuestPart，绝不依赖未保存的 delegate 作为唯一状态。
    /// </summary>
    public sealed class ChoiceLetter_SymbiosisCovenantCerebrexSupportOffer : ChoiceLetter
    {
        public string? offerId;
        public Site? targetSite;

        public ChoiceLetter_SymbiosisCovenantCerebrexSupportOffer()
        {
        }

        public QuestPart_SymbiosisCovenantCerebrexSupport? FindPart()
        {
            if (quest == null)
            {
                return null;
            }

            return quest.PartsListForReading
                .OfType<QuestPart_SymbiosisCovenantCerebrexSupport>()
                .FirstOrDefault(p => p.offerId == offerId);
        }

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                yield return new DiaOption(
                    "MAP_SymbiosisCovenant_CerebrexSupport_Accept".Translate())
                {
                    action = () => Resolve(true),
                    resolveTree = true
                };

                yield return new DiaOption(
                    "MAP_SymbiosisCovenant_CerebrexSupport_Decline".Translate())
                {
                    action = () => Resolve(false),
                    resolveTree = true
                };
            }
        }

        private void Resolve(bool accept)
        {
            QuestPart_SymbiosisCovenantCerebrexSupport? part = FindPart();
            if (part == null)
            {
                Messages.Message(
                    "MAP_SymbiosisCovenant_CerebrexSupport_AcceptFailInvalid".Translate(),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            // 超时后再点击接受必须通过 QuestPart 再次校验状态与 expire tick。
            if (accept)
            {
                part.TryAcceptOffer();
            }
            else
            {
                part.ResolveDecline();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref offerId, "offerId");
            Scribe_References.Look(ref targetSite, "targetSite");
        }
    }
}
