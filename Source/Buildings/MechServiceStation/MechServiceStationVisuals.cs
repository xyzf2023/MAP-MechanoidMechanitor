using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 迁移战车维修台的双臂解算与焊接节奏；不依赖战车状态或渲染补丁。
    public sealed class MechServiceStationVisuals
    {
        private readonly CompMechServiceStation station;
        private readonly Graphic[] arms = new Graphic[4];
        private const float ArmStep = 1f / 48f;
        private Mote? chargeMote;
        private Pawn? visualPawn;
        private float extension;
        private int cycle;
        private bool repairing;
        private FleckDef? sparkFlash;
        private ThingDef? sparkThrown;

        public MechServiceStationVisuals(CompMechServiceStation station) => this.station = station;

        public void Tick(Pawn pawn, bool charging, bool repairingNow)
        {
            if (visualPawn != pawn)
            {
                // 交接时先沿旧机体的姿态收臂，避免体型或维修落点突变。
                if (extension > 0f)
                {
                    TickIdle();
                    return;
                }
                Stop();
            }
            visualPawn = pawn;
            if (charging)
            {
                if (chargeMote == null || chargeMote.Destroyed)
                    chargeMote = MoteMaker.MakeAttachedOverlay(pawn, ThingDefOf.Mote_MechCharging, Vector3.zero);
                chargeMote?.Maintain();
            }
            else StopCharging();

            repairing = repairingNow;
            if (!repairing)
            {
                Retract();
                return;
            }
            extension = Mathf.MoveTowards(extension, 1f, ArmStep);
            if (extension < 1f) return;
            cycle++;
            for (int side = 0; side < 2; side++)
            {
                if (!IsWelding(ArmTick(side))) continue;
                Solve(side, out _, out _, out Vector3 tip);
                if (!tip.ToIntVec3().InBounds(station.parent.Map)) continue;
                if (Rand.Chance(0.2f))
                    FleckMaker.Static(tip, station.parent.Map,
                        sparkFlash ??= DefDatabase<FleckDef>.GetNamed("SparkFlash"), Rand.Range(2.5f, 3.5f));
                if (Rand.Chance(0.2f))
                {
                    MoteThrown spark = (MoteThrown)ThingMaker.MakeThing(
                        sparkThrown ??= DefDatabase<ThingDef>.GetNamed("Mote_SparkThrown"));
                    spark.Scale = Rand.Range(0.24f, 0.34f);
                    spark.airTimeLeft = Rand.Range(0.08f, 0.16f);
                    spark.rotationRate = Rand.Range(-240f, 240f);
                    spark.exactPosition = tip;
                    spark.SetVelocity(Rand.Range(135f, 225f), Rand.Range(7.2f, 24f));
                    GenSpawn.Spawn(spark, tip.ToIntVec3(), station.parent.Map);
                }
            }
        }

        private void StopCharging()
        {
            if (chargeMote != null && !chargeMote.Destroyed) chargeMote.Destroy();
            chargeMote = null;
        }

        public void Stop()
        {
            StopCharging();
            repairing = false;
            if (extension == 0f) visualPawn = null;
        }

        // 会话结束后仍由建筑 Tick 驱动；固定维修周期，从当前落点反向收回。
        public void TickIdle()
        {
            Stop();
            Retract();
        }

        private void Retract()
        {
            extension = Mathf.MoveTowards(extension, 0f, ArmStep);
            if (extension > 0f) return;
            cycle = 0;
        }

        public void Reset()
        {
            Stop();
            visualPawn = null;
            extension = 0f;
            cycle = 0;
        }

        private static bool IsWelding(int tick) => tick % 180 >= 40 && tick % 180 < 130;
        private int ArmTick(int side) => cycle + (side == 0 ? 0 : 83);

        private void Solve(int side, out Vector3 root, out Vector3 elbow, out Vector3 tip)
        {
            float sign = side == 0 ? -1f : 1f;
            Vector3 center = station.parent.DrawPos;
            center.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            float benchScale = station.parent.def.graphicData.drawSize.x / 2f;
            root = center + new Vector3(sign * 0.76f, 0f, 0.1f) * benchScale;
            int tick = ArmTick(side);
            int segment = tick / 180 + side * 2;
            float t = tick % 180;
            float bodyScale = visualPawn == null ? 1f : Mathf.Clamp(Mathf.Sqrt(visualPawn.BodySize), 0.6f, 1.5f);
            Vector3 point = RepairPoint(segment, sign) * bodyScale;
            Vector3 previous = RepairPoint(segment + 3, sign) * bodyScale;
            float move = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 40f));
            Vector3 target = Vector3.Lerp(previous, point, move);
            if (t < 40f) target.x += sign * Mathf.Sin(move * Mathf.PI) * 0.1f;
            if (repairing && IsWelding(tick)) target.z += Mathf.Sin(tick * 0.22f) * 0.012f;
            Vector3 folded = new Vector3(sign * 0.67f, 0f, -0.12f) * benchScale;
            tip = center + Vector3.Lerp(folded, target, Mathf.SmoothStep(0f, 1f, extension));
            float upper = (side == 0 ? 0.485f : 0.51f) * benchScale;
            float lower = 0.34f * benchScale;
            Vector3 d = tip - root;
            float distance = Mathf.Clamp(d.magnitude, Mathf.Abs(upper - lower) + 0.01f, upper + lower - 0.01f);
            d = d.normalized * distance;
            tip = root + d;
            float angle = Mathf.Atan2(d.z, d.x) + sign * Mathf.Acos(Mathf.Clamp(
                (upper * upper + distance * distance - lower * lower) / (2f * upper * distance), -1f, 1f));
            elbow = root + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * upper;
        }

        private static Vector3 RepairPoint(int index, float sign)
        {
            switch (index % 4)
            {
                case 0: return new Vector3(sign * 0.28f, 0f, 0.30f);
                case 1: return new Vector3(sign * 0.22f, 0f, 0.04f);
                case 2: return new Vector3(sign * 0.24f, 0f, -0.30f);
                default: return new Vector3(sign * 0.25f, 0f, -0.58f);
            }
        }

        public void Draw()
        {
            for (int side = 0; side < 2; side++)
            {
                Solve(side, out Vector3 root, out Vector3 elbow, out Vector3 tip);
                if (side == 0)
                {
                    DrawArm(0, "LeftUpper", new Vector2(333, 188), new Vector2(28, 30), new Vector2(305, 158), root, elbow);
                    DrawArm(1, "LeftLower", new Vector2(219, 163), new Vector2(25, 133), new Vector2(193, 27), elbow, tip);
                }
                else
                {
                    DrawArm(2, "RightUpper", new Vector2(239, 313), new Vector2(209, 30), new Vector2(29, 284), root, elbow);
                    DrawArm(3, "RightLower", new Vector2(226, 158), new Vector2(196, 130), new Vector2(28, 27), elbow, tip);
                }
            }
        }

        private void DrawArm(int index, string texture, Vector2 size, Vector2 pivot, Vector2 end, Vector3 start, Vector3 finish)
        {
            arms[index] ??= GraphicDatabase.Get<Graphic_Single>("Buildings/MechServiceStation/" + texture,
                ShaderDatabase.Cutout, Vector2.one, Color.white);
            Vector3 axis = new Vector3(end.x - pivot.x, 0f, pivot.y - end.y);
            Quaternion rotation = Quaternion.AngleAxis(Vector3.SignedAngle(axis, finish - start, Vector3.up), Vector3.up);
            float scale = (finish - start).magnitude / axis.magnitude;
            Vector3 center = start + rotation * new Vector3(size.x / 2f - pivot.x, 0f, pivot.y - size.y / 2f) * scale;
            center.y += index * 0.001f;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(center, rotation,
                new Vector3(size.x * scale, 1f, size.y * scale)), arms[index].MatSingle, 0);
        }
    }
}
