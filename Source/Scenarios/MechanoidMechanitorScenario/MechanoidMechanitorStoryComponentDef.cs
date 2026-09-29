using System;
using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryComponentDef : Def
    {
        public int displayOrder;

        public Type? workerClass;

        [Unsaved(false)]
        private MechanoidMechanitorStoryComponentWorker? workerInt;

        [Unsaved(false)]
        private bool workerCreationFailed;

        public MechanoidMechanitorStoryComponentWorker? Worker
        {
            get
            {
                if (workerInt != null || workerCreationFailed)
                {
                    return workerInt;
                }

                string? error = GetWorkerClassError(workerClass);
                if (error != null)
                {
                    workerCreationFailed = true;
                    Log.Error(
                        $"[MAP-机械族机械师] {defName}: 无法创建剧情组件执行器。{error}");
                    return null;
                }

                try
                {
                    object? instance = Activator.CreateInstance(workerClass!);
                    if (instance is MechanoidMechanitorStoryComponentWorker worker)
                    {
                        worker.def = this;
                        workerInt = worker;
                    }
                    else
                    {
                        workerCreationFailed = true;
                        Log.Error(
                            $"[MAP-机械族机械师] {defName}: workerClass '{workerClass}' 未创建 {nameof(MechanoidMechanitorStoryComponentWorker)} 类型的实例。");
                    }
                }
                catch (Exception ex)
                {
                    workerCreationFailed = true;
                    Log.Error(
                        $"[MAP-机械族机械师] {defName}: 创建剧情组件执行器 '{workerClass}' 失败：{ex}");
                }

                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            string? workerError = GetWorkerClassError(workerClass);
            if (workerError != null)
            {
                yield return $"{defName}: {workerError}";
            }
        }

        private static string? GetWorkerClassError(Type? workerClass)
        {
            if (workerClass == null)
            {
                return "未配置 workerClass；应指定 MechanoidMechanitorStoryComponentWorker 的非抽象子类。";
            }

            if (!typeof(MechanoidMechanitorStoryComponentWorker).IsAssignableFrom(workerClass))
            {
                return $"workerClass '{workerClass.FullName}' 未继承 {nameof(MechanoidMechanitorStoryComponentWorker)}。";
            }

            if (workerClass.IsAbstract)
            {
                return $"workerClass '{workerClass.FullName}' 是抽象类，无法实例化。";
            }

            if (workerClass.GetConstructor(Type.EmptyTypes) == null)
            {
                return $"workerClass '{workerClass.FullName}' 没有公共无参构造函数，无法通过 Activator.CreateInstance 实例化。";
            }

            return null;
        }
    }
}
