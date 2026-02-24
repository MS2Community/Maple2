using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.Skill;
using System.Numerics;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public partial class MovementState {
    private SkillRecord? castSkill = null;
    public NpcTask? CastTask { get; private set; } = null;
    private Vector3 castMoveOffset;
    private string castMoveStartKeyframe = string.Empty;
    private string castMoveEndKeyframe = string.Empty;
    private float castMoveLastTick = -1;
    private bool castMoveFinished;

    public void StateSkillEvent(string keyName) {
        if (castSkill is null) {
            CastTask?.Cancel();

            return;
        }

        actor.AppendDebugMessage("Skill Keyframe: " + keyName);

        if (actor.Animation.PlayingSequence is not null) {
            actor.AppendDebugMessage(actor.Animation.PlayingSequence.Name);
        }

        if (castSkill.Motion.AttackPoints.TryGetValue(keyName, out byte point)) {
            var targets = new List<IActor>();

            // TODO: change BattleState to track a list of targets for multi projectile skills & use that instead
            if (actor.BattleState.Target is not null) {
                targets.Add(actor.BattleState.Target);
            }

            if (point != 0xFF) {
                actor.SkillState.SkillCastAttack(castSkill, point, targets);
            } else {
                // motion has multiple attack points with the name
                point = 0;
                foreach (SkillMetadataAttack attack in castSkill.Motion.Attacks) {
                    if (attack.Point == keyName) {

                        actor.SkillState.SkillCastAttack(castSkill, point, targets);
                    }
                    ++point;
                }
            }
        }

        switch (keyName) {
            case "end":
                int motionCount = castSkill.Metadata.Data.Motions.Length;
                byte motion = castSkill.MotionPoint;

                if (CastTask is NpcSkillCastTask task && motion + 1 < motionCount) {
                    SkillCast(task, castSkill!.SkillId, castSkill!.Level, 0, (byte) (motion + 1));
                    return;
                }

                CastTask?.Completed();

                break;
            case "move0":
                float moveDistance = castSkill.Motion.MotionProperty.MoveDistance;

                if (moveDistance == 0) {
                    return;
                }

                castMoveLastTick = 0;
                castMoveOffset = moveDistance * actor.Transform.FrontAxis;
                castMoveStartKeyframe = "move0";
                castMoveEndKeyframe = "move1";
                castMoveFinished = false;
                break;
            case "move1":
                if (castMoveLastTick == -1) {
                    return;
                }
                castMoveFinished = true;
                break;
            default:
                break;
        }
    }

    private void StateSkillCastMoveUpdate(long tickCount, long tickDelta, float delta) {
        if (castMoveLastTick == -1) {
            return;
        }

        Vector3 newPosition;
        Vector3 offset;
        float castMoveTick = 0;

        if (castMoveLastTick >= 1) {
            newPosition = actor.Position;
            offset = new Vector3(0, 0, 0);
            Velocity = new Vector3(0, 0, 0);

            castMoveLastTick = -1;
        } else {
            castMoveTick = actor.Animation.GetSequenceSegmentTime(castMoveStartKeyframe, castMoveEndKeyframe);

            if (castMoveFinished) {
                castMoveTick = 1;
            }

            if (castMoveTick == -1) {
                castMoveLastTick = -1;

                return;
            }

            newPosition = (castMoveTick - castMoveLastTick) * castMoveOffset + actor.Position;
            offset = newPosition - actor.Position;

            castMoveLastTick = castMoveTick;
        }

        actor.Navigation!.UpdatePosition(newPosition);

        float timeStep = 0;

        if (delta != 0) {
            timeStep = 1 / delta;
        }

        Velocity = timeStep * offset;

        UpdateDebugMarker(actor.Position, debugNpc, tickCount);
        UpdateDebugMarker(actor.Navigation.GetAgentPosition(), debugAgent, tickCount);
    }

    private void StateSkillCastUpdate(long tickCount, long tickDelta) {
        StateSkillCastMoveUpdate(tickCount, tickDelta, (float) tickDelta / 1000);
    }
}
