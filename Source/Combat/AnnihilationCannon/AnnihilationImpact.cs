using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    /// <summary>保存落点事务：正常 Kill 优先，爆炸结束后统一强制清理非 Pawn 残留。</summary>
    public sealed class AnnihilationImpact : ThingWithComps
    {
        private Pawn? launcher;
        private AnnihilationSettings settings = null!;
        private int phase;
        private int nextPhaseTick;
        private int visualStartTick = -1;
        private bool explosionEnded;
        private bool finalCleanupDone;
        private bool aftermathCreated;
        private bool visualFailed;
        private List<IntVec3> innerCells = new List<IntVec3>();
        private List<IntVec3> outerCells = new List<IntVec3>();
        private List<Pawn> releasedPawns = new List<Pawn>();
        private List<AnnihilationResidue> residues = new List<AnnihilationResidue>();
        // 使用 ID 而非对象引用：正常 Kill 后对象可能已被丢弃，读档仍不能重复主动 Kill。
        private List<string> attemptedKillIds = new List<string>();
        private readonly HashSet<string> attemptedKills = new HashSet<string>();

        internal void Initialize(Pawn? actor, AnnihilationSettings snapshot)
        {
            launcher = actor;
            settings = snapshot;
        }

        internal void Begin()
        {
            if (phase != 0 || !Spawned) return;
            phase = 1;
            nextPhaseTick = Find.TickManager.TicksGame + settings.delayTicks;
            RefreshInnerCells();
            KillInnerThings();
            KillInnerPawns();
        }

        private void RefreshInnerCells()
        {
            // 原版 Worker 返回共享临时集合，必须立即复制，随后固定本阶段的遮挡快照。
            innerCells = DamageDefOf.Bomb.Worker.ExplosionCellsToHit(Position, Map, settings.innerRadius).ToList();
            innerCells.Sort((a, b) => a.DistanceToSquared(Position).CompareTo(b.DistanceToSquared(Position)));
        }

        private List<Thing> ThingsInInnerCells(bool includeContents)
        {
            List<Thing> result = new List<Thing>();
            HashSet<Thing> seen = new HashSet<Thing>();
            foreach (IntVec3 cell in innerCells)
            {
                if (!cell.InBounds(Map)) continue;
                foreach (Thing thing in cell.GetThingList(Map).ToArray())
                {
                    if (!seen.Add(thing) || !IsPhysicalThing(thing)) continue;
                    result.Add(thing);
                    if (includeContents && thing is IThingHolder holder)
                    {
                        foreach (Thing content in ThingOwnerUtility.GetAllThingsRecursively(holder, allowUnreal: false))
                            if (seen.Add(content) && IsPhysicalThing(content)) result.Add(content);
                    }
                }
            }
            return result;
        }

        internal static bool IsPhysicalThing(Thing thing) => thing != null && !thing.Destroyed && !thing.Discarded
            && thing.def.category != ThingCategory.Ethereal && thing.def.category != ThingCategory.Mote
            && thing.def.category != ThingCategory.Projectile;

        private void KillInnerThings()
        {
            // 先固定目标，不因逐个 Kill 拆墙而在同一阶段继续向外扩张。
            List<Thing> targets = ThingsInInnerCells(includeContents: false);
            HashSet<Thing> seen = new HashSet<Thing>(targets);
            foreach (Thing thing in targets.ToArray())
            {
                if (thing is Pawn || !(thing is IThingHolder holder)) continue;
                try
                {
                    List<Thing> contents = new List<Thing>();
                    ThingOwnerUtility.GetAllThingsRecursively(holder, contents, allowUnreal: false,
                        passCheck: child => !(child is Pawn pawn) || pawn.Dead);
                    foreach (Thing content in contents)
                        if (IsPhysicalThing(content) && seen.Add(content)) targets.Add(content);
                }
                catch (Exception ex)
                {
                    Log.Error("[MAP-机械族机械师] 湮灭炮收集 " + thing + " 的容器内容时发生异常：" + ex);
                }
            }
            // 持有者先走正常 Kill；其后已掉落的内容也有一次正常 Kill 的机会。
            foreach (Thing thing in targets) TryKillThing(thing);
        }

        private void TryKillThing(Thing thing)
        {
            AnnihilationDeathCapture.Capture? capture = null;
            try
            {
                if (!IsPhysicalThing(thing) || thing == this || thing.MapHeld != Map
                    || AnnihilationWhitelistUtility.IsProtected(thing)
                    || attemptedKills.Contains(thing.ThingID)) return;
                if (thing is Pawn pawn)
                {
                    if (pawn.Dead || pawn.health == null || pawn.health.isBeingKilled
                        || pawn == launcher) return;
                }
                else
                {
                    // 不可摧毁对象（如地热喷口）只登记最终兜底，不触发原版错误日志。
                    RecordResidue(thing, 0);
                    if (!thing.def.destroyable || !thing.Spawned) return;
                }
                if (!TryReleaseProtectedContents(thing) || !TryReleaseLivingContents(thing)) return;
                // 释放容器内容可能触发回调，临近调用再次检查状态。
                if (!IsPhysicalThing(thing) || thing.MapHeld != Map
                    || AnnihilationWhitelistUtility.IsProtected(thing)) return;
                if (thing is Pawn living)
                {
                    if (living.Dead || living.health == null || living.health.isBeingKilled) return;
                }
                else if (!thing.Spawned || !thing.def.destroyable) return;
                // 非 Pawn 的正常击毁也可能掉落到范围外或并入既有堆叠。
                // Pawn 由现有死亡观察补丁统一捕获，避免重复创建整图快照。
                if (!(thing is Pawn))
                    capture = AnnihilationDeathCapture.BeginCapture(Map,
                        new List<AnnihilationImpact> { this }, thing);
                if (!attemptedKills.Add(thing.ThingID)) return;
                attemptedKillIds.Add(thing.ThingID);
                thing.Kill(new DamageInfo(AnnihilationCannonDefOf.MAP_AnnihilationInner, settings.damage,
                    settings.armorPenetration, -1f, launcher));
            }
            catch (Exception ex)
            {
                Log.Error("[MAP-机械族机械师] 湮灭炮尝试正常击毁 " + thing + " 时发生异常：" + ex);
            }
            finally
            {
                // Kill 可能在生成产物后抛错；仍收集残留，不重复 Kill 或补发通知。
                AnnihilationDeathCapture.FinishCapture(capture);
            }
        }

        private bool TryReleaseLivingContents(Thing thing)
        {
            // 活 Pawn 自己的装备和库存留给其正常死亡流程。
            if (thing is Pawn || !(thing is IThingHolder holder)) return true;
            List<Thing> contents = new List<Thing>();
            ThingOwnerUtility.GetAllThingsRecursively(holder, contents, allowUnreal: false,
                passCheck: child => !(child is Pawn pawn) || pawn.Dead);
            IntVec3 cell = thing.PositionHeld;
            if (!cell.InBounds(Map)) cell = Position;
            foreach (Pawn pawn in contents.OfType<Pawn>().Where(p => !p.Dead && !p.Destroyed).ToArray())
            {
                if (!releasedPawns.Contains(pawn)) releasedPawns.Add(pawn);
                if (pawn.def.destroyOnDrop || (pawn.holdingOwner != null
                    && !pawn.holdingOwner.TryDrop(pawn, cell, Map, ThingPlaceMode.Near,
                        out _, playDropSound: false))) return false;
            }
            return true;
        }

        private void ClearNonPawns(HashSet<Thing> handled)
        {
            foreach (Thing thing in ThingsInInnerCells(includeContents: false))
                TryDestroyNonPawn(thing, handled);
        }

        private void TryDestroyNonPawn(Thing thing, HashSet<Thing> handled)
        {
            try { DestroyNonPawn(thing, handled); }
            catch (Exception ex)
            {
                // 单个对象的销毁回调异常不能中止其余格子的结算。
                Log.Error("[MAP-机械族机械师] 湮灭炮销毁 " + thing + " 时发生异常：" + ex);
            }
        }

        private void DestroyNonPawn(Thing thing, HashSet<Thing> handled)
        {
            if (!IsPhysicalThing(thing) || thing is Pawn || AnnihilationWhitelistUtility.IsProtected(thing)
                || !handled.Add(thing)) return;
            if (!TryReleaseProtectedContents(thing) || !TryReleaseLivingContents(thing)) return;
            if (thing is IThingHolder holder)
            {
                List<Thing> contents = new List<Thing>();
                // 活 Pawn 的装备留给死亡阶段；尸体内的装备则随尸体一起清除。
                ThingOwnerUtility.GetAllThingsRecursively(holder, contents, allowUnreal: false,
                    passCheck: child => !(child is Pawn pawn) || pawn.Dead);
                foreach (Thing content in contents) TryDestroyNonPawn(content, handled);
            }
            if (!IsPhysicalThing(thing) || AnnihilationWhitelistUtility.IsProtected(thing)
                || !TryReleaseProtectedContents(thing)) return;
            bool previous = Thing.allowDestroyNonDestroyable;
            try
            {
                Thing.allowDestroyNonDestroyable = true;
                thing.Destroy(DestroyMode.Vanish);
            }
            finally { Thing.allowDestroyNonDestroyable = previous; }
        }

        private bool TryReleaseProtectedContents(Thing thing)
        {
            if (!AnnihilationWhitelistUtility.HasEntries || !(thing is IThingHolder holder)) return true;
            // 先完成原版遍历再操作容器，避免掉落回调重入其共享遍历缓存。
            List<Thing> contents = ThingOwnerUtility.GetAllThingsRecursively(holder, allowUnreal: false);
            IntVec3 cell = thing.PositionHeld;
            if (!cell.InBounds(Map)) cell = Position;
            foreach (Thing content in contents)
            {
                if (content.Destroyed || !AnnihilationWhitelistUtility.IsProtected(content)
                    || AnnihilationWhitelistUtility.IsProtectedByHolder(content)) continue;
                // 整体释放最外层白名单容器，其内部对象随容器保留；不逐件拆出。
                // 原版 TryDrop 对 destroyOnDrop 对象会返回成功却直接销毁，必须提前排除。
                if (content.def.destroyOnDrop || content.holdingOwner == null
                    || !content.holdingOwner.TryDrop(content, cell, Map, ThingPlaceMode.Near,
                        out _, playDropSound: false)) return false;
            }
            // 无法安全释放时，调用方必须保留原容器或 Pawn，不能连带清空白名单内容。
            return true;
        }

        private void StartOuterExplosion()
        {
            // 正常 Kill 成功的墙已移除；未被击毁的遮挡物保留到最终清场。
            RefreshInnerCells();
            visualStartTick = Find.TickManager.TicksGame;
            // 立即复制爆炸前的遮挡快照；不使用被爆炸改变后的地图补算余波范围。
            outerCells = DamageDefOf.Bomb.Worker
                .ExplosionCellsToHit(Position, Map, settings.outerRadius).ToList();
            AnnihilationExplosion explosion = (AnnihilationExplosion)ThingMaker.MakeThing(
                AnnihilationCannonDefOf.MAP_AnnihilationExplosion);
            explosion.owner = this;
            GenSpawn.Spawn(explosion, Position, Map);
            explosion.radius = settings.outerRadius;
            explosion.damType = AnnihilationCannonDefOf.MAP_AnnihilationOuter;
            explosion.damAmount = settings.damage;
            explosion.armorPenetration = settings.armorPenetration;
            explosion.instigator = launcher;
            explosion.projectile = AnnihilationCannonDefOf.MAP_AnnihilationShot;
            explosion.propagationSpeed = 1f;
            explosion.doVisualEffects = false;
            explosion.doSoundEffects = false;
            explosion.damageFalloff = false;
            explosion.chanceToStartFire = 0f;
            // 本次发射者免疫自己的爆炸；原版会保存 ignoredThings，读档后仍有效。
            explosion.StartExplosion(null, launcher != null
                ? new List<Thing> { launcher } : null);
            AnnihilationHitEffect.StartBlackout(this, settings.VisualDurationTicks);
            DefDatabase<SoundDef>.GetNamedSilentFail("MAP_AnnihilationCannon_Expand")
                ?.PlayOneShot(new TargetInfo(Position, Map));
        }

        private void KillInnerPawns()
        {
            HashSet<Pawn> attempted = new HashSet<Pawn>();
            // 合体死亡可能同步恢复出新的机械体；在同一 Kill 阶段重新收集。
            // 有限轮次防止第三方死亡回调无限生成 Pawn。
            for (int pass = 0; pass < 8; pass++)
            {
                List<Pawn> targets = ThingsInInnerCells(includeContents: true).OfType<Pawn>()
                    .Concat(releasedPawns).Where(p => p != null && !p.Dead && !p.Destroyed
                        && p.MapHeld == Map && !attempted.Contains(p) && !attemptedKills.Contains(p.ThingID)
                        && !AnnihilationWhitelistUtility.IsProtected(p)
                        && p != launcher).Distinct().ToList();
                if (targets.Count == 0) break;
                foreach (Pawn pawn in targets)
                {
                    attempted.Add(pawn);
                    TryKillThing(pawn);
                    if (pawn.Corpse != null) RecordResidue(pawn.Corpse, 0);
                }
            }
        }

        internal bool WatchesDeath(Pawn pawn)
        {
            if (AnnihilationWhitelistUtility.IsProtected(pawn)) return false;
            if (pawn == launcher) return false;
            if (!Spawned || phase < 1 || finalCleanupDone || pawn.MapHeld != Map) return false;
            return releasedPawns.Contains(pawn) || innerCells.Contains(pawn.PositionHeld);
        }

        internal void RecordResidue(Thing thing, int originalCount)
        {
            if (!IsPhysicalThing(thing) || thing is Pawn || AnnihilationWhitelistUtility.IsProtected(thing)) return;
            AnnihilationResidue? existing = residues.FirstOrDefault(r => r.thing == thing);
            if (existing != null) existing.originalCount = Math.Min(existing.originalCount, originalCount);
            else residues.Add(new AnnihilationResidue { thing = thing, originalCount = originalCount });
        }

        private void CleanupResidues()
        {
            HashSet<Thing> handled = new HashSet<Thing>();
            for (int pass = 0; pass < 8; pass++)
            {
                AnnihilationResidue[] pending = residues.ToArray();
                residues.Clear();
                foreach (AnnihilationResidue residue in pending)
                {
                    Thing? thing = residue.thing;
                    // 读档前登记的残留也使用当前名单；在拆分堆叠前检查，避免先改动受保护对象。
                    if (thing == null || !IsPhysicalThing(thing) || AnnihilationWhitelistUtility.IsProtected(thing)) continue;
                    int removeCount = thing.stackCount - residue.originalCount;
                    if (removeCount <= 0) continue;
                    if (residue.originalCount > 0)
                    {
                        // 死亡掉落并入已有堆叠时，只删除新增数量。
                        Thing fragment = thing.SplitOff(removeCount);
                        TryDestroyNonPawn(fragment, handled);
                    }
                    else TryDestroyNonPawn(thing, handled);
                }
                if (pass == 0) ClearNonPawns(handled);
                // 兜底销毁容器时释放的活 Pawn 仍只走正常死亡，绝不直接抹除。
                KillInnerPawns();
                // 销毁回调可能再次触发死亡；保留并处理回调中新登记的残留。
                if (residues.Count == 0) break;
            }
        }

        private void CreateAftermath()
        {
            if (aftermathCreated || !Spawned) return;
            // 回调抛错也不重试已提交的地形破坏，不重复生成弹坑。
            aftermathCreated = true;
            try
            {
                AnnihilationAftermathUtility.Create(Map, Position, outerCells);
            }
            catch (Exception ex)
            {
                Log.Error("[MAP-机械族机械师] 湮灭炮余波生成失败：" + ex);
            }
        }

        internal void NotifyExplosionEnded() => explosionEnded = true;

        protected override void Tick()
        {
            base.Tick();
            if (settings == null) { Destroy(); return; }
            if (phase == 0) Begin();
            int now = Find.TickManager.TicksGame;
            if (phase < 4 && now >= nextPhaseTick)
            {
                int action = phase;
                phase++;
                nextPhaseTick = now + settings.delayTicks;
                if (action == 1) StartOuterExplosion();
                else if (action == 2)
                {
                    RefreshInnerCells();
                    KillInnerThings();
                    KillInnerPawns();
                }
                // phase 3 仅保留原有阶段间隔及存档编号，清场统一等待爆炸结束。
            }
            if (phase >= 4 && explosionEnded && !finalCleanupDone)
            {
                // 爆炸晚于 T+9 到达的格子仍可能产生残留，完成后统一清理。
                CleanupResidues();
                CreateAftermath();
                finalCleanupDone = true;
            }
            if (finalCleanupDone && visualStartTick >= 0
                && now - visualStartTick >= settings.VisualDurationTicks)
                Destroy(DestroyMode.Vanish);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (visualStartTick < 0 || settings == null) return;
            int age = Find.TickManager.TicksGame - visualStartTick;
            AnnihilationHitEffect.Draw(Position.ToVector3Shifted(), thingIDNumber,
                settings, age, ref visualFailed);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref launcher, "launcher");
            Scribe_Deep.Look(ref settings, "settings");
            Scribe_Values.Look(ref phase, "phase");
            Scribe_Values.Look(ref nextPhaseTick, "nextPhaseTick");
            Scribe_Values.Look(ref visualStartTick, "visualStartTick", -1);
            Scribe_Values.Look(ref explosionEnded, "explosionEnded");
            Scribe_Values.Look(ref finalCleanupDone, "finalCleanupDone");
            Scribe_Values.Look(ref aftermathCreated, "aftermathCreated");
            Scribe_Collections.Look(ref innerCells, "innerCells", LookMode.Value);
            Scribe_Collections.Look(ref outerCells, "outerCells", LookMode.Value);
            Scribe_Collections.Look(ref releasedPawns, "releasedPawns", LookMode.Reference);
            Scribe_Collections.Look(ref residues, "residues", LookMode.Deep);
            Scribe_Collections.Look(ref attemptedKillIds, "attemptedKillIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                innerCells ??= new List<IntVec3>();
                // 旧档已开始爆炸时缺少快照：仅留弹坑，不根据当前地形扩大破坏范围。
                // 尚未开始爆炸的旧攻击会在 StartOuterExplosion 中建立正常快照。
                outerCells ??= new List<IntVec3>();
                releasedPawns ??= new List<Pawn>();
                releasedPawns.RemoveAll(p => p == null);
                residues ??= new List<AnnihilationResidue>();
                residues.RemoveAll(r => r == null || r.thing == null);
                attemptedKillIds ??= new List<string>();
                // 旧档保持已有 phase，不补跑落地阶段；新增去重字段缺失时从空集合开始。
                attemptedKills.Clear();
                foreach (string id in attemptedKillIds)
                    if (!string.IsNullOrEmpty(id)) attemptedKills.Add(id);
            }
        }
    }

    public sealed class AnnihilationResidue : IExposable
    {
        public Thing? thing;
        public int originalCount;
        public void ExposeData()
        {
            Scribe_References.Look(ref thing, "thing");
            Scribe_Values.Look(ref originalCount, "originalCount");
        }
    }

    /// <summary>完整保留原版 Bomb 结算，唯一扩展是结束时通知本次攻击补清残留。</summary>
    public sealed class AnnihilationExplosion : Explosion
    {
        internal AnnihilationImpact? owner;
        protected override void ExplosionEnded()
        {
            base.ExplosionEnded();
            owner?.NotifyExplosionEnded();
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref owner, "annihilationOwner");
        }
    }
}
