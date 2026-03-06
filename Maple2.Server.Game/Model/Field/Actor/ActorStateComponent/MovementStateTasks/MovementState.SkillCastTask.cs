using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.Enum;
using Maple2.Server.Game.Model.Skill;
using System.Numerics;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public partial class MovementState {
    public class NpcSkillCastTask : NpcTask {
        private readonly MovementState movement;

        public SkillRecord? Cast;
        public int SkillId { get; init; }
        public short SkillLevel { get; init; }
        public long SkillUid { get; init; }
        public int FaceTarget;
        public Vector3 FacePos;

        public NpcSkillCastTask(TaskState queue, MovementState movement, int id, short level, int faceTarget, Vector3 facePos, long uid) : base(queue, NpcTaskPriority.BattleAction) {
            this.movement = movement;
            SkillId = id;
            SkillLevel = level;
            SkillUid = uid;
            FaceTarget = faceTarget;
            FacePos = facePos;
        }

        protected override void TaskResumed() {
            movement.SkillCast(this, SkillId, SkillLevel, SkillUid, 0);
        }

        protected override void TaskFinished(bool isCompleted) {
            movement.castSkill = null;
            movement.CastTask = null;
            movement.Idle();
            movement.actor.AppendDebugMessage((isCompleted ? "Finished" : "Canceled") + " cast\n");
        }
    }

    private void SkillCastFaceTarget(SkillRecord cast, IActor target, int faceTarget) {
        Vector3 offset = target.Position - actor.Position;
        float distance = offset.X * offset.X + offset.Y * offset.Y;
        float vertical = MathF.Abs(offset.Z);

        // In MS2 data, many AI skill nodes use FaceTarget=0 while the skill's AutoTargeting.MaxDegree
        // is a narrow cone. If we gate turning by MaxDegree (dot product), the NPC can end up casting
        // while facing away (target behind the cone) and never correct its facing.
        //
        // To avoid "背对玩家放技能", we always rotate to face the current target when:
        //  - the motion requests FaceTarget, and
        //  - AutoTargeting distance/height constraints allow it.
        // MaxDegree is ignored for *turning*.
        if (faceTarget != 1) {
            if (!cast.Motion.MotionProperty.FaceTarget || cast.Metadata.Data.AutoTargeting is null) {
                return;
            }

            var autoTargeting = cast.Metadata.Data.AutoTargeting;

            bool inRange = autoTargeting.MaxDistance == 0 || distance <= autoTargeting.MaxDistance * autoTargeting.MaxDistance;
            inRange &= autoTargeting.MaxHeight == 0 || vertical <= autoTargeting.MaxHeight;
            if (!inRange) {
                return;
            }

            if (distance < 0.0001f) {
                return;
            }

            distance = (float) Math.Sqrt(distance);
            offset *= (1 / distance);
        } else {
            if (distance < 0.0001f) {

                return;

            }


            distance = (float) Math.Sqrt(distance);

            offset *= (1 / distance);
        }

        actor.Transform.LookTo(offset);
    }

    private void SkillCast(NpcSkillCastTask task, int id, short level, long uid, byte motion) {
        if (CastTask != task) {
            CastTask?.Cancel();
        }

        if (!CanTransitionToState(ActorState.PcSkill)) {
            task.Cancel();

            return;
        }

        Velocity = new Vector3(0, 0, 0);

        SkillRecord? cast = actor.CastSkill(id, level, uid, (int) actor.Field.FieldTick, motionPoint: motion);

        if (cast is null) {
            task.Cancel();

            return;
        }

        if (!actor.Animation.TryPlaySequence(cast.Motion.MotionProperty.SequenceName, cast.Motion.MotionProperty.SequenceSpeed, AnimationType.Skill, out AnimationSequenceMetadata? sequence, skill: cast.Metadata)) {
            task.Cancel();

            return;
        }

        if (task.FacePos != new Vector3(0, 0, 0)) {
            actor.Transform.LookTo(task.FacePos - actor.Position); // safe: LookTo normalizes with guards
        } else if (actor.BattleState.Target is not null) {
            // Hard guarantee: NPCs should always face their current battle target when casting.
            // Some boss skills have MotionProperty.FaceTarget=false or narrow AutoTargeting degrees,
            // which previously allowed casting while facing away.
            actor.Transform.LookTo(actor.BattleState.Target.Position - actor.Position); // safe: LookTo normalizes with guards
        }

        CastTask = task;
        castSkill = cast;
        task.Cast = cast;

        //if (faceTarget && actor.BattleState.Target is not null) {
        //    actor.Transform.LookTo(Vector3.Normalize(actor.BattleState.Target.Position - actor.Position));
        //}

        SetState(ActorState.PcSkill);

        stateSequence = sequence;
    }
}
