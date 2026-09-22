using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    /// <summary>保存落点事务：非 Pawn 消失、爆炸、Kill、残留清理分别只提交一次。</summary>
    public sealed class AnnihilationImpact : Thing
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
            ClearNonPawns();
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

        internal static bool IsPhysicalThing(Thing thing) => thing != null && !thing.Destroyed
            && thing.def.category != ThingCategory.Ethereal && thing.def.category != ThingCategory.Mote
            && thing.def.category != ThingCategory.Projectile;

        private void ClearNonPawns()
        {
            HashSet<Thing> handled = new HashSet<Thing>();
            foreach (Thing thing in ThingsInInnerCells(includeContents: false))
                TryDestroyNonPawn(thing, handled);
        }

        private void TryDestroyNonPawn(Thing thing, HashSet<Thing> handled)
        {
            try { DestroyNonPawn(thing, handled); }
            catch (Exception ex)
            {
                // 单个对象的销毁回调异常不能中止其余格子的结算。
                Log.Error("[MAP] 湮灭炮销毁 " + thing + " 时发生异常：" + ex);
            }
        }

        private void DestroyNonPawn(Thing thing, HashSet<Thing> handled)
        {
            if (!IsPhysicalThing(thing) || thing is Pawn || !handled.Add(thing)) return;
            if (thing is IThingHolder holder)
            {
                List<Thing> contents = new List<Thing>();
                // 活 Pawn 的装备留给死亡阶段；尸体内的装备则随尸体一起清除。
                ThingOwnerUtility.GetAllThingsRecursively(holder, contents, allowUnreal: false,
                    passCheck: child => !(child is Pawn pawn) || pawn.Dead);
                foreach (Pawn pawn in contents.OfType<Pawn>().Where(p => !p.Dead).ToArray())
                {
                    if (!releasedPawns.Contains(pawn)) releasedPawns.Add(pawn);
                    IntVec3 cell = thing.PositionHeld;
                    if (!cell.InBounds(Map)) cell = Position;
                    // Vanish 会直接清空部分容器；先释放活 Pawn，确保之后走 Kill。
                    if (pawn.holdingOwner != null && !pawn.holdingOwner.TryDrop(
                            pawn, cell, Map, ThingPlaceMode.Near, out _, playDropSound: false))
                        return;
                }
                foreach (Thing content in contents) DestroyNonPawn(content, handled);
            }
            if (thing.Destroyed) return;
            bool previous = Thing.allowDestroyNonDestroyable;
            try
            {
                Thing.allowDestroyNonDestroyable = true;
                thing.Destroy(DestroyMode.Vanish);
            }
            finally { Thing.allowDestroyNonDestroyable = previous; }
        }

        private void StartOuterExplosion()
        {
            // 第一阶段移除了墙；爆炸和后续 Pawn 判定使用此时的地图。
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
            explosion.damType = DamageDefOf.Bomb;
            explosion.damAmount = settings.damage;
            explosion.armorPenetration = settings.armorPenetration;
            explosion.instigator = launcher;
            explosion.projectile = AnnihilationCannonDefOf.MAP_AnnihilationShot;
            explosion.propagationSpeed = 1f;
            explosion.doVisualEffects = false;
            explosion.doSoundEffects = false;
            explosion.damageFalloff = false;
            explosion.chanceToStartFire = 0f;
            explosion.StartExplosion(null, launcher?.GetComp<CompSunBossState>() != null
                ? new List<Thing> { launcher } : null);
            AnnihilationHitEffect.StartBlackout(Map, settings.VisualDurationTicks);
            DefDatabase<SoundDef>.GetNamedSilentFail("Psycast_Skip_Entry")
                ?.PlayOneShot(new TargetInfo(Position, Map));
        }

        private void KillInnerPawns()
        {
            RefreshInnerCells();
            HashSet<Pawn> attempted = new HashSet<Pawn>();
            // 合体死亡可能同步恢复出新的机械体；在同一 Kill 阶段重新收集。
            // 有限轮次防止第三方死亡回调无限生成 Pawn。
            for (int pass = 0; pass < 8; pass++)
            {
                List<Pawn> targets = ThingsInInnerCells(includeContents: true).OfType<Pawn>()
                    .Concat(releasedPawns).Where(p => p != null && !p.Dead && !p.Destroyed
                        && p.MapHeld == Map && !attempted.Contains(p)
                        && !(p == launcher && p.GetComp<CompSunBossState>() != null)).Distinct().ToList();
                if (targets.Count == 0) break;
                foreach (Pawn pawn in targets)
                {
                    attempted.Add(pawn);
                    // Kill 会调用本 MOD 的正常合体解除与原版死亡通知，不直接 Destroy 活 Pawn。
                    try
                    {
                        pawn.Kill(new DamageInfo(DamageDefOf.Bomb, settings.damage,
                            settings.armorPenetration, -1f, launcher));
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[MAP] 湮灭炮处理 " + pawn + " 的死亡时发生异常：" + ex);
                    }
                    if (pawn.Corpse != null) RecordResidue(pawn.Corpse, 0);
                }
            }
        }

        internal bool WatchesDeath(Pawn pawn)
        {
            if (pawn == launcher && pawn.GetComp<CompSunBossState>() != null) return false;
            if (!Spawned || phase < 1 || finalCleanupDone || pawn.MapHeld != Map) return false;
            return releasedPawns.Contains(pawn) || innerCells.Contains(pawn.PositionHeld);
        }

        internal void RecordResidue(Thing thing, int originalCount)
        {
            if (!IsPhysicalThing(thing) || thing is Pawn) return;
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
                    if (thing == null || thing.Destroyed) continue;
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
                if (pass == 0) ClearNonPawns();
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
                Log.Error("[MAP] 湮灭炮余波生成失败：" + ex);
            }
        }

        internal void NotifyExplosionEnded() => explosionEnded = true;

        protected override void Tick()
        {
            if (settings == null) { Destroy(); return; }
            if (phase == 0) Begin();
            int now = Find.TickManager.TicksGame;
            if (phase < 4 && now >= nextPhaseTick)
            {
                int action = phase;
                phase++;
                nextPhaseTick = now + settings.delayTicks;
                if (action == 1) StartOuterExplosion();
                else if (action == 2) KillInnerPawns();
                else if (action == 3) CleanupResidues();
            }
            if (phase >= 4 && explosionEnded && !finalCleanupDone)
            {
                // 爆炸晚于 T+9 到达的格子仍可能产生残留，仅在完成时补清一次。
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
