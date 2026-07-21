using System;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 世界选点器没有取消回调；该无界面窗口仅用于在选点器退出后恢复通讯窗口。
    /// </summary>
    public sealed class MechForceSupportWorldTargeterWatcher : Window
    {
        private readonly Action onCancelled;

        private bool completed;

        public override Vector2 InitialSize => new Vector2(1f, 1f);

        public MechForceSupportWorldTargeterWatcher(Action onCancelled)
        {
            this.onCancelled = onCancelled;
            forcePause = false;
            doCloseX = false;
            doCloseButton = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            doWindowBackground = false;
            drawShadow = false;
            preventCameraMotion = false;
        }

        public void MarkCompleted()
        {
            completed = true;
            Close(doCloseSound: false);
        }

        public override void OnCancelKeyPressed()
        {
            if (Find.WorldTargeter.IsTargeting)
            {
                Find.WorldTargeter.StopTargeting();
                Event.current.Use();
            }
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (completed || Find.WorldTargeter.IsTargeting)
            {
                return;
            }

            completed = true;
            onCancelled();
            Close(doCloseSound: false);
        }

        public override void DoWindowContents(Rect inRect)
        {
        }
    }
}
