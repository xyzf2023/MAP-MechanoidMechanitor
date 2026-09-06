using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 备用机体就绪信件。行为参考原版 ChoiceLetter_GameEnded，但不修改原版类。
    ///
    /// - 保持观察：只结束对话树，不移除信件、不投放、不写冷却。
    /// - 投放备用机体：每次构建选择项时实时重新计算可用性与禁用理由。
    /// - 返回主菜单：复用原版 GenScene.GoToMainMenu。
    /// </summary>
    public class ChoiceLetter_OrbitalBackupReady : ChoiceLetter
    {
        public const string TitleKey =
            "MAP_MechanoidMechanitor.OrbitalBackup.Letter.Title";
        public const string TextKey =
            "MAP_MechanoidMechanitor.OrbitalBackup.Letter.Text";
        public const string KeepWatchingKey =
            "MAP_MechanoidMechanitor.OrbitalBackup.Letter.KeepWatching";
        public const string DeployKey =
            "MAP_MechanoidMechanitor.OrbitalBackup.Letter.Deploy";
        public const string MainMenuKey =
            "MAP_MechanoidMechanitor.OrbitalBackup.Letter.MainMenu";

        public override bool CanDismissWithRightClick => false;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                if (ArchivedOnly)
                {
                    yield return Option_Close;
                    yield break;
                }

                yield return new DiaOption(KeepWatchingKey.Translate())
                {
                    resolveTree = true
                };

                DiaOption deploy = new DiaOption(DeployKey.Translate())
                {
                    action = () => OrbitalBackupJusticeUtility.TryDeployBackupJustice(),
                    resolveTree = true
                };

                // 每次都实时计算：不缓存剩余小时，也不允许绕过冷却或地图检查。
                string? disabledReason = OrbitalBackupJusticeUtility.GetDisabledReason();
                if (disabledReason != null)
                {
                    deploy.Disable(disabledReason);
                }

                yield return deploy;

                yield return new DiaOption(MainMenuKey.Translate())
                {
                    action = GenScene.GoToMainMenu,
                    resolveTree = true
                };
            }
        }
    }
}
