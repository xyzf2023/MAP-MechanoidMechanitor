using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.GD5Expansion;

public class Hediff_JusticeCombatAnalysisCore : HediffWithComps
{
    private const int SynchronizationInterval = 61;
    private HediffStage? cachedBaseStage;
    private HediffStage? cachedStage;
    private int cachedRewardRevision = int.MinValue;
    private int synchronizedRewardRevision = int.MinValue;

    // 扩展点：以后从本存档的独立首领奖励记录读取修订号，不把奖励写入本体成长列表。
    protected virtual int ExtraBossRewardRevision => 0;

    public override HediffStage CurStage
    {
        get
        {
            HediffStage? source = base.CurStage;
            if (source == null) return null!;
            int revision = ExtraBossRewardRevision;
            if (!ReferenceEquals(cachedBaseStage, source) || cachedRewardRevision != revision)
            {
                HediffStage ownedStage = JusticeCombatAnalysisCoreSource.CopyStage(source);
                ApplyExtraBossRewards(ownedStage);
                cachedBaseStage = source;
                cachedRewardRevision = revision;
                cachedStage = ownedStage;
            }
            return cachedStage!;
        }
    }

    // 扩展点：属性加值和倍率已独立复制，后续按 StatDef 合并奖励，不能追加同名属性。
    // 其余引用字段仍为只读；若需要修改容量、伤害等列表，须先复制列表及其中的元素。
    protected virtual void ApplyExtraBossRewards(HediffStage ownedStage)
    {
    }

    public override void PostAdd(DamageInfo? dinfo)
    {
        base.PostAdd(dinfo);
        SynchronizeProgress();
    }

    public override void Notify_Spawned()
    {
        base.Notify_Spawned();
        SynchronizeProgress();
    }

    public override void Notify_Resurrected()
    {
        base.Notify_Resurrected();
        SynchronizeProgress();
    }

    public override void PostTickInterval(int delta)
    {
        base.PostTickInterval(delta);
        if (pawn.IsHashIntervalTick(SynchronizationInterval, delta))
            SynchronizeProgress();
    }

    public override void ExposeData()
    {
        base.ExposeData();
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            // 读档时只清派生缓存，等世界组件就绪后再同步权威进度。
            cachedBaseStage = null;
            cachedStage = null;
            cachedRewardRevision = int.MinValue;
            synchronizedRewardRevision = int.MinValue;
        }
    }

    private void SynchronizeProgress()
    {
        if (!JusticeCombatAnalysisCoreSource.TryGetSeverity(out float severity)) return;
        int previousStage = CurStageIndex;
        int revision = ExtraBossRewardRevision;
        bool rewardsChanged = synchronizedRewardRevision != revision;
        synchronizedRewardRevision = revision;

        // 保留原版严重度更新及切档通知；通知重入查询时会按新阶段重建缓存。
        Severity = severity;
        if (rewardsChanged && previousStage == CurStageIndex
            && pawn.health.hediffSet.hediffs.Contains(this))
            pawn.health.Notify_HediffChanged(this);
    }
}

[StaticConstructorOnStartup]
internal static class JusticeCombatAnalysisCoreSource
{
    private const string CoreDefName = "MAP_JusticeCombatAnalysisCore";
    private static readonly MethodInfo CloneMethod = typeof(object).GetMethod(
        "MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static HediffDef? sourceDef;
    private static Type? componentType;
    private static FieldInfo? recordsField;

    static JusticeCombatAnalysisCoreSource()
    {
        try
        {
            ModContentPack mod = ReflectionTargets.FindMod("fxz.glitterworlddestroyer.mk5")
                ?? throw new InvalidOperationException("未找到闪毁本体。");
            Type type = ReflectionTargets.RequireType(mod, "GD3.MainComponent");
            FieldInfo? field = type.GetField("list_str",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (type.Assembly.GetName().Name != "GD3" || !typeof(WorldComponent).IsAssignableFrom(type)
                || field?.FieldType != typeof(List<string>))
                throw new InvalidOperationException("本体世界成长记录的类型或字段已变化。");

            HediffDef source = DefDatabase<HediffDef>.GetNamed("GD_BlackEnhancer");
            HediffDef own = DefDatabase<HediffDef>.GetNamed(CoreDefName);
            if (source.stages.NullOrEmpty())
                throw new InvalidOperationException("本体神经元优化没有可用阶段。");

            // 只配置自己的 Def；保留本体完整阶段字段，信息卡与实例共用同一套基准数值。
            List<HediffStage> stages = new(source.stages.Count);
            foreach (HediffStage stage in source.stages)
                stages.Add(CopyStage(stage));
            own.stages = stages;
            own.minSeverity = source.minSeverity;
            own.maxSeverity = source.maxSeverity;
            own.initialSeverity = Math.Clamp(0.1f, source.minSeverity, source.maxSeverity);
            sourceDef = source;
            componentType = type;
            recordsField = field;
        }
        catch (Exception ex)
        {
            Log.Error("[MAP-机械族机械师] 正义历战演算核心初始化失败，未启用成长同步。\n" + ex);
        }
    }

    internal static bool TryGetSeverity(out float severity)
    {
        severity = 0.1f;
        if (sourceDef == null || componentType == null || recordsField == null
            || Current.Game == null || Find.World == null)
            return false;

        // 不缓存世界实例或进度，避免切换存档后串档；组件尚未加载时保留已有严重度。
        WorldComponent? component = Find.World.GetComponent(componentType);
        if (component == null) return false;
        List<string>? records = (List<string>?)recordsField.GetValue(component);
        severity = Math.Clamp((records?.Count ?? 0) + 0.1f,
            sourceDef.minSeverity, sourceDef.maxSeverity);
        return true;
    }

    internal static HediffStage CopyStage(HediffStage source)
    {
        // 浅复制保留本体所有阶段字段；奖励可能修改的属性列表和元素另行复制。
        HediffStage copy = (HediffStage)CloneMethod.Invoke(source, null)!;
        copy.statOffsets = CopyModifiers(source.statOffsets);
        copy.statFactors = CopyModifiers(source.statFactors);
        return copy;
    }

    private static List<StatModifier> CopyModifiers(List<StatModifier>? source)
    {
        if (source == null) return null!;
        List<StatModifier> copy = new(source.Count);
        foreach (StatModifier modifier in source)
            copy.Add(new StatModifier { stat = modifier.stat, value = modifier.value });
        return copy;
    }
}