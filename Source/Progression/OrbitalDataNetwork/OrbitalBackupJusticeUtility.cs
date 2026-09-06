using System;
using System.Collections.Generic;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 轨道设施备用机体（MAP_Mech_Justice）投放。
    ///
    /// 仅在“机械族机械师”剧本且已研究轨道数据网络时启用；普通存档即使研究该科技
    /// 也只获得全体机械意识、狂热兴趣与技能同步，不提供备用机体投放。
    /// </summary>
    public static class OrbitalBackupJusticeUtility
    {
        internal const string BackupLetterDefName = "MAP_OrbitalBackupReady";

        private const string WaitHoursKey =
            "MAP_MechanoidMechanitor.OrbitalBackup.WaitHours";
        private const string NotAvailableKey =
            "MAP_MechanoidMechanitor.OrbitalBackup.NotAvailable";
        private const string NoDropSpotKey =
            "MAP_MechanoidMechanitor.OrbitalBackup.NoDropSpot";

        public static LetterDef? BackupLetterDef =>
            DefDatabase<LetterDef>.GetNamedSilentFail(BackupLetterDefName);

        /// <summary>备用机体功能是否启用：机械族机械师剧本 + 轨道数据网络已研究。</summary>
        public static bool IsFeatureActive =>
            MechanoidMechanitorScenarioUtility.IsScenarioActive
            && ResearchFeatureUnlockUtility.IsOrbitalDataNetworkUnlocked();

        /// <summary>
        /// 每次构建按钮时实时重新计算是否可用；不缓存剩余小时。
        /// 返回 null 表示当前可以投放；否则返回禁用理由。
        /// </summary>
        public static string? GetDisabledReason()
        {
            if (!IsFeatureActive)
            {
                return NotAvailableKey.Translate().Resolve();
            }

            int remainingHours =
                GameComponent_OrbitalDataNetworkState.RemainingCooldownHours;
            if (remainingHours > 0)
            {
                return WaitHoursKey.Translate(remainingHours).Resolve();
            }

            if (!TryFindDropTarget(out Map? targetMap, out string? targetReason)
                || targetMap == null)
            {
                return targetReason ?? NotAvailableKey.Translate().Resolve();
            }

            if (!TryFindDropCell(targetMap, out IntVec3 _))
            {
                return NoDropSpotKey.Translate().Resolve();
            }

            return null;
        }

        /// <summary>
        /// 投放一台备用机体。任一阶段失败都不会消耗冷却、不会移除信件，
        /// 并尽可能回滚本次刚创建的对象。
        /// </summary>
        public static bool TryDeployBackupJustice()
        {
            Pawn? created = null;
            Map? targetMap = null;
            bool succeeded = false;

            try
            {
                if (!IsFeatureActive)
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 备用机体投放被拒绝：当前不是机械族机械师剧本，或尚未研究轨道数据网络。");
                    return false;
                }

                string? disabledReason = GetDisabledReason();
                if (disabledReason != null)
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 备用机体投放被拒绝：" + disabledReason);
                    return false;
                }

                if (!TryFindDropTarget(out targetMap, out string? mapReason)
                    || targetMap == null)
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 备用机体投放失败：找不到合法投放地图，" +
                        $"reason={mapReason ?? "未知"}。");
                    return false;
                }

                if (!TryFindDropCell(targetMap, out IntVec3 dropCell))
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 备用机体投放失败：找不到合法空投点，" +
                        $"map={targetMap.ToString() ?? "null"}。");
                    return false;
                }

                PawnKindDef? kind = JusticePawnUtility.JusticePawnKind;
                if (kind == null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 备用机体投放失败：缺少 PawnKindDef " +
                        $"{JusticePawnUtility.JusticeDefName}。");
                    return false;
                }

                created = GenerateBackupJustice(kind);
                if (created == null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 备用机体投放失败：阶段=GeneratePawn，" +
                        $"pawnKind={kind.defName}。");
                    return false;
                }

                if (!FinalizeMechanitorIdentity(created))
                {
                    Log.Error(
                        "[MAP-机械族机械师] 备用机体投放失败：阶段=FinalizeIdentity，" +
                        $"pawn={created.LabelShort}（{created.ThingID}）。");
                    return false;
                }

                // 全部身份组件完成后再应用轨道技能备份与兴趣，并同步一个“机械意识”。
                if (!OrbitalDataNetworkSkillSyncUtility.RefreshBackupBeforeDeployment()
                    || !OrbitalDataNetworkSkillSyncUtility.ApplyBackupToPawn(created))
                {
                    Log.Error(
                        "[MAP-机械族机械师] 备用机体投放失败：阶段=ApplyBackup，" +
                        $"pawn={created.LabelShort}（{created.ThingID}）。");
                    return false;
                }

                GameComponent_MechanoidMechanitorRegistry
                    .RequestMechanicalConsciousnessHediffSync();

                ActiveTransporterInfo info = new ActiveTransporterInfo();
                info.openDelay = ActiveTransporterInfo.DefaultOpenDelay;

                if (created.Spawned)
                {
                    created.DeSpawn();
                }

                if (!info.innerContainer.TryAdd(created))
                {
                    Log.Error(
                        "[MAP-机械族机械师] 备用机体投放失败：阶段=FillTransporter，" +
                        $"pawn={created.LabelShort}（{created.ThingID}）。");
                    return false;
                }

                DropPodUtility.MakeDropPodAt(dropCell, targetMap, info);

                if (!IsPawnInWorld(created))
                {
                    Log.Error(
                        "[MAP-机械族机械师] 备用机体投放失败：阶段=VerifyDropPod，" +
                        $"pawn={created.LabelShort}（{created.ThingID}），" +
                        $"map={targetMap.ToString() ?? "null"}。");
                    return false;
                }

                TickManager? tickManager = Find.TickManager;
                if (tickManager == null
                    || !GameComponent_OrbitalDataNetworkState.TryRecordDeployment(
                        tickManager.TicksGame))
                {
                    Log.Error(
                        "[MAP-机械族机械师] 备用机体投放失败：阶段=CommitCooldown，" +
                        $"pawn={created.LabelShort}（{created.ThingID}）。");
                    return false;
                }

                OrbitalBackupGameEndUtility.RemoveBackupLetter();
                GameEnder? gameEnder = Find.GameEnder;
                if (gameEnder != null)
                {
                    gameEnder.gameEnding = false;
                }

                succeeded = true;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 备用机体投放异常：" +
                    DescribeFailure(created, targetMap) + "：" + ex);
                return false;
            }
            finally
            {
                if (!succeeded)
                {
                    RollbackCreatedPawn(created);
                }
            }
        }

        /// <summary>
        /// 生成一台备用正义。参数沿用剧本开局的角色生成参数，但不复用 ScenPart 本身。
        /// </summary>
        private static Pawn? GenerateBackupJustice(PawnKindDef kind)
        {
            Faction? playerFaction = Faction.OfPlayerSilentFail;
            if (playerFaction == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 备用机体生成失败：玩家阵营不可用。");
                return null;
            }

            PawnGenerationRequest request = new PawnGenerationRequest(
                kind,
                playerFaction,
                PawnGenerationContext.NonPlayer,
                null,
                forceGenerateNewPawn: true,
                allowDead: false,
                allowDowned: false,
                canGeneratePawnRelations: true,
                mustBeCapableOfViolence: false,
                1f,
                forceAddFreeWarmLayerIfNeeded: false,
                allowGay: true,
                allowPregnant: false,
                allowFood: true,
                allowAddictions: true,
                inhabitant: false,
                certainlyBeenInCryptosleep: false,
                forceRedressWorldPawnIfFormerColonist: false,
                worldPawnFactionDoesntMatter: false,
                0f,
                0f,
                null,
                1f,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                forceNoIdeo: false,
                forceNoBackstory: false,
                forbidAnyTitle: false,
                forceDead: false,
                null,
                null,
                null,
                null,
                null,
                0f,
                developmentalStages: DevelopmentalStage.Adult);

            return PawnGenerator.GeneratePawn(request);
        }

        /// <summary>设置玩家阵营、升格或确认为机械族机械师，并补全角色状态。</summary>
        private static bool FinalizeMechanitorIdentity(Pawn pawn)
        {
            Faction? playerFaction = Faction.OfPlayerSilentFail;
            if (playerFaction == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 备用机体身份确认失败：玩家阵营不可用。");
                return false;
            }

            if (pawn.Faction != playerFaction)
            {
                pawn.SetFaction(playerFaction);
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)
                && !MechanoidMechanitorRoleUtility.PromoteToAcquiredMechanoidMechanitor(pawn))
            {
                Log.Error(
                    "[MAP-机械族机械师] 备用机体升格为机械族机械师失败：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）。");
                return false;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                Log.Error(
                    "[MAP-机械族机械师] 备用机体未能取得机械族机械师身份：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）。");
                return false;
            }

            MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
            return true;
        }

        /// <summary>
        /// 选择投放目标地图：优先当前正在查看的合法地表玩家基地，
        /// 否则取第一个合法的地表玩家基地。不会为了投放而自动生成新地图。
        /// </summary>
        private static bool TryFindDropTarget(out Map? map, out string? reason)
        {
            map = null;
            reason = null;

            if (Current.Game == null || Find.Maps == null)
            {
                reason = NotAvailableKey.Translate().Resolve();
                return false;
            }

            bool anyPlayerHome = false;
            Map? fallback = null;

            IReadOnlyList<Map> homes = Current.Game.PlayerHomeMaps;
            for (int i = 0; i < homes.Count; i++)
            {
                Map candidate = homes[i];
                if (candidate == null || !candidate.IsPlayerHome)
                {
                    continue;
                }

                anyPlayerHome = true;
                if (IsRootSurfacePlayerHome(candidate) && fallback == null)
                {
                    fallback = candidate;
                }
            }

            // 优先当前正在查看的合法地表玩家基地；否则取第一个合法的地表玩家基地。
            Map? current = Find.CurrentMap;
            if (current != null && IsRootSurfacePlayerHome(current))
            {
                map = current;
                return true;
            }

            if (fallback != null)
            {
                map = fallback;
                return true;
            }

            // 存在玩家基地但全部位于无法空投的非地表层：使用原版“没有合适目的地”。
            reason = anyPlayerHome
                ? "NoWandererDestination".Translate().Resolve()
                : "NoColony".Translate().Resolve();
            return false;
        }

        private static bool IsRootSurfacePlayerHome(Map? map)
        {
            if (map == null || !map.IsPlayerHome)
            {
                return false;
            }

            try
            {
                return map.Tile.Layer != null && map.Tile.Layer.IsRootSurface;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryFindDropCell(Map map, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;

            if (DropCellFinder.TryFindDropSpotNear(
                    map.Center,
                    map,
                    out IntVec3 nearCenter,
                    allowFogged: true,
                    canRoofPunch: true))
            {
                cell = nearCenter;
                return true;
            }

            if (DropCellFinder.TryFindDropSpotNear(
                    map.Center,
                    map,
                    out IntVec3 loose,
                    allowFogged: true,
                    canRoofPunch: true,
                    mustBeReachableFromCenter: false))
            {
                cell = loose;
                return true;
            }

            IntVec3 random = DropCellFinder.RandomDropSpot(map);
            if (random.IsValid)
            {
                cell = random;
                return true;
            }

            return false;
        }

        private static bool IsPawnInWorld(Pawn? pawn)
        {
            return pawn != null
                && !pawn.Destroyed
                && !pawn.Discarded
                && (pawn.Spawned || pawn.ParentHolder != null);
        }

        /// <summary>
        /// 回滚本次刚创建的临时 Pawn。已经进入世界的 Pawn 不会回滚，也不会删除任何已有 Pawn。
        /// </summary>
        private static void RollbackCreatedPawn(Pawn? pawn)
        {
            if (pawn == null)
            {
                return;
            }

            try
            {
                if (IsPawnInWorld(pawn))
                {
                    return;
                }

                if (pawn.IsWorldPawn())
                {
                    Find.WorldPawns?.RemovePawn(pawn);
                }

                GameComponent_MechanoidMechanitorRegistry.RemoveFailedGeneratedMechanitor(pawn);

                if (pawn.Spawned)
                {
                    pawn.DeSpawn();
                }

                if (!pawn.Destroyed)
                {
                    pawn.Destroy(DestroyMode.Vanish);
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 备用机体投放失败后回滚临时 Pawn 异常：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
            }
        }

        private static string DescribeFailure(Pawn? pawn, Map? map)
        {
            return $"pawn={pawn?.LabelShort ?? "null"}（{pawn?.ThingID ?? "null"}），"
                + $"map={map?.ToString() ?? "null"}";
        }
    }
}
