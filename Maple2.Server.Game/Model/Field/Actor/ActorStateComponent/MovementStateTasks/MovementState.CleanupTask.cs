using Maple2.Model.Metadata;
using Maple2.Server.Core.Network;
using Maple2.Server.Game.Model.Enum;
using System.Numerics;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public partial class MovementState {
    public class NpcCleanupPatrolDataTask : NpcTask {
        private readonly MovementState movement;
        private readonly FieldPlayer player;
        private readonly Vector3? lastPosition;

        #region Autofac Autowired
        // ReSharper disable MemberCanBePrivate.Global
        private ConstantsTable Constants => player.Session.ServerTableMetadata.ConstantsTable;
        // ReSharper restore All
        #endregion

        public override bool CancelOnInterrupt => false;

        public NpcCleanupPatrolDataTask(FieldPlayer player, TaskState taskState, MovementState movement) : base(taskState, NpcTaskPriority.Cleanup) {
            this.movement = movement;
            this.player = player;
            MS2WayPoint? last = movement.actor.Patrol?.WayPoints.LastOrDefault();
            if (last is null) {
                return;
            }
            lastPosition = last.Position;
        }

        protected override void TaskResumed() {
            if (movement.actor.Patrol is not null) {
                return;
            }

            movement.actor.Field.RemoveNpc(movement.actor.ObjectId);

            if (lastPosition is null) {
                return;
            }

            float maxDistance = Constants.TalkableDistance *
                                Constants.TalkableDistance;

            // find nearest npc
            FieldNpc? closestNpc = player.Field.Npcs.Values
                .Where(npc => npc != movement.actor && Vector3.DistanceSquared(player.Position, npc.Position) < maxDistance)
                .OrderBy(npc => Vector3.DistanceSquared(player.Position, npc.Position))
                .FirstOrDefault();

            if (closestNpc == null) {
                return;
            }

            player.Transform.LookTo(Vector3.Normalize(closestNpc.Position - lastPosition.Value));
            player.MoveToPosition(lastPosition.Value, player.Rotation);
        }
    }
}
