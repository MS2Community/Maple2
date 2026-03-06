using System.Numerics;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using Maple2.Server.Game.Model;
using Maple2.Tools.DotRecast;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager.Field;

public sealed class AgentNavigation {
    private static readonly ILogger Logger = Log.Logger.ForContext<AgentNavigation>();

    private readonly FieldNpc npc;
    private FieldManager field => npc.Field;
    public readonly DtCrowdAgent agent;
    private readonly DtCrowd crowd;

    public List<RcVec3f>? currentPath = [];
    private int currentPathIndex = 0;
    private float currentPathProgress = 0;
    private bool currentPathIsFallback = false;

    public AgentNavigation(FieldNpc fieldNpc, DtCrowdAgent dtAgent, DtCrowd dtCrowd) {
        npc = fieldNpc;
        agent = dtAgent;
        crowd = dtCrowd;
    }

    public List<RcVec3f>? FindPath(Vector3 startVec, Vector3 targetVec) {
        return FindPath(crowd, DotRecastHelper.ToNavMeshSpace(startVec), DotRecastHelper.ToNavMeshSpace(targetVec));
    }

    public List<RcVec3f>? FindPath(RcVec3f startVec, RcVec3f targetVec) {
        return FindPath(crowd, startVec, targetVec);
    }

    private List<RcVec3f>? FindPath(DtCrowd crowd, RcVec3f startVec, RcVec3f targetVec) {
        DtNavMesh navMesh = crowd.GetNavMesh();
        DtNavMeshQuery navMeshQuery = crowd.GetNavMeshQuery();
        IDtQueryFilter filter = crowd.GetFilter(0);
        if (!field.FindNearestPoly(startVec, out long pos1Ref, out RcVec3f _)) {
            Logger.Error("Failed to find nearest poly at {StartVec}", startVec);
            return null;
        }

        if (!field.FindNearestPoly(targetVec, out long posRef2, out RcVec3f _)) {
            Logger.Error("Failed to find nearest poly at {TargetVec}", targetVec);
            return null;
        }

        List<long> pathIterPolys = [];
        navMeshQuery.FindPath(pos1Ref, posRef2, startVec, targetVec, filter, ref pathIterPolys, new DtFindPathOption(0, float.MaxValue));
        if (pathIterPolys.Count == 0) {
            Logger.Error("Failed to find path from {StartVec} to {TargetVec}", startVec, targetVec);
            return null;
        }

        int pathIterPolysCount = pathIterPolys.Count;

        navMeshQuery.ClosestPointOnPoly(pos1Ref, startVec, out RcVec3f iterPos, out bool _);
        navMeshQuery.ClosestPointOnPoly(pathIterPolys[pathIterPolysCount - 1], targetVec, out RcVec3f endPos, out bool _);

        Span<long> visited = stackalloc long[16];
        int nvisited = 0;

        int MAX_POLYS = 256;
        int MAX_SMOOTH = 256;
        List<RcVec3f> smoothPath = [];
        while (pathIterPolysCount > 0 && smoothPath.Count < MAX_SMOOTH) {
            // Find location to steer towards.
            if (!DtPathUtils.GetSteerTarget(navMeshQuery, iterPos, endPos, DotRecastHelper.MIN_TARGET_DIST,
                pathIterPolys, pathIterPolysCount, out var steerPos, out int steerPosFlag, out long steerPosRef)) {
                break;
            }

            bool endOfPath = (steerPosFlag & DtStraightPathFlags.DT_STRAIGHTPATH_END) != 0;
            bool offMeshConnection = (steerPosFlag & DtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION) != 0;

            // Find movement delta.
            RcVec3f delta = RcVec3f.Subtract(steerPos, iterPos);
            float len = MathF.Sqrt(RcVec3f.Dot(delta, delta));
            // If the steer target is end of path or off-mesh link, do not move past the location.
            if ((endOfPath || offMeshConnection) && len < DotRecastHelper.STEP_SIZE) {
                len = 1;
            } else {
                len = DotRecastHelper.STEP_SIZE / len;
            }

            RcVec3f moveTgt = RcVecUtils.Mad(iterPos, delta, len);

            // Move
            navMeshQuery.MoveAlongSurface(pathIterPolys[0], iterPos, moveTgt, filter, out var result, visited, out nvisited, 16);

            iterPos = result;

            pathIterPolysCount = DtPathUtils.MergeCorridorStartMoved(ref pathIterPolys, pathIterPolysCount, MAX_POLYS, visited, nvisited);
            pathIterPolysCount = DtPathUtils.FixupShortcuts(ref pathIterPolys, pathIterPolysCount, navMeshQuery);

            if (navMeshQuery.GetPolyHeight(pathIterPolys[0], result, out float h).Succeeded()) {
                iterPos.Y = h;
            }

            // Handle end of path and off-mesh links when close enough.
            if (endOfPath && DtPathUtils.InRange(iterPos, steerPos, DotRecastHelper.MIN_TARGET_DIST, 1.0f)) {
                // Reached end of path.
                iterPos = targetVec;
                if (smoothPath.Count < MAX_SMOOTH) {
                    smoothPath.Add(iterPos);
                }

                break;
            } else if (offMeshConnection && DtPathUtils.InRange(iterPos, steerPos, DotRecastHelper.MIN_TARGET_DIST, 1.0f)) {
                // Reached off-mesh connection.
                RcVec3f startPosition = RcVec3f.Zero;
                RcVec3f endPosition = RcVec3f.Zero;

                // Advance the path up to and over the off-mesh connection.
                long prevRef = 0;
                long polyRef = pathIterPolys[0];
                int npos = 0;
                while (npos < pathIterPolysCount && polyRef != steerPosRef) {
                    prevRef = polyRef;
                    polyRef = pathIterPolys[npos];
                    npos++;
                }

                pathIterPolys = pathIterPolys.GetRange(npos, pathIterPolys.Count - npos);
                pathIterPolysCount -= npos;

                // Handle the connection.
                var status4 = navMesh.GetOffMeshConnectionPolyEndPoints(prevRef, polyRef, ref startPosition, ref endPosition);
                if (status4.Succeeded()) {
                    if (smoothPath.Count < MAX_SMOOTH) {
                        smoothPath.Add(startPosition);
                        // Hack to make the dotted path not visible during off-mesh connection.
                        if ((smoothPath.Count & 1) != 0) {
                            smoothPath.Add(startPosition);
                        }
                    }

                    // Move position at the other side of the off-mesh link.
                    iterPos = endPosition;
                    navMeshQuery.GetPolyHeight(pathIterPolys[0], iterPos, out float eh);
                    iterPos.Y = eh;
                }
            }

            // Store results.
            if (smoothPath.Count < MAX_SMOOTH) {
                smoothPath.Add(iterPos);
            }
        }

        return smoothPath;
    }

    public bool HasPath => currentPath != null && currentPath.Count > 0;

    private static bool IsVayarianGuardian(int npcId) {
        return npcId is 21500420 or 21500422 or 21500423 or 21500424;
    }

    public bool UpdatePosition() {
        if (!field.FindNearestPoly(npc.Position, out _, out RcVec3f position)) {
            Logger.Error("Failed to find valid position from {Source} => {Position}", npc.Position, position);
            return false;
        }

        agent.npos = position;
        npc.Position = DotRecastHelper.FromNavMeshSpace(position);
        return true;
    }

    public bool UpdatePosition(Vector3 position) {
        if (!field.FindNearestPoly(position, out _, out RcVec3f rcPosition)) {
            Logger.Error("Failed to find valid position from {Source} => {Position}", npc.Position, rcPosition);
            return false;
        }

        agent.npos = rcPosition;
        npc.Position = DotRecastHelper.FromNavMeshSpace(rcPosition);
        return true;
    }

    public Vector3 GetAgentPosition() {
        return DotRecastHelper.FromNavMeshSpace(agent.npos);
    }

    public (Vector3 Start, Vector3 End) Advance(TimeSpan timeSpan, float speed, out bool followSegment) {
        followSegment = false;
        if (currentPath?.Count < 2) {
            return default;
        }

        Vector3 start = DotRecastHelper.FromNavMeshSpace(currentPath![currentPathIndex]);

        if (currentPathIndex >= currentPath.Count - 1) {
            return default;
        }

        followSegment = true;
        float distance = RcVec3f.Distance(currentPath[currentPathIndex], currentPath[currentPathIndex + 1]);

        speed *= DotRecastHelper.MapRotation.GetRightAxis().Length(); // changing speed to navmesh space
        float timeLeft = distance * (1 - currentPathProgress) / speed;
        while (timeLeft < timeSpan.TotalSeconds) {
            timeSpan -= TimeSpan.FromSeconds(timeLeft);
            currentPathIndex++;
            currentPathProgress = 0;

            if (currentPathIndex >= currentPath.Count - 1) {
                return (start, DotRecastHelper.FromNavMeshSpace(currentPath[^1]));
            }

            distance = RcVec3f.Distance(currentPath[currentPathIndex], currentPath[currentPathIndex + 1]);
            timeLeft = distance * (1 - currentPathProgress) / speed;
        }

        currentPathProgress += (float) timeSpan.TotalSeconds * speed / distance;
        RcVec3f end = RcVec3f.Lerp(currentPath[currentPathIndex], currentPath[currentPathIndex + 1], currentPathProgress);
        if (currentPathIsFallback) {
            // Keep the end point on the navmesh each tick; prevents straight-line fallback from pushing NPCs through walls/out of bounds.
            if (field.FindNearestPoly(end, out _, out RcVec3f snappedEnd)) {
                end = snappedEnd;
                if (currentPath is not null && currentPathIndex + 1 < currentPath.Count) {
                    currentPath[currentPathIndex + 1] = snappedEnd;
                }
            } else {
                // Can't stay on mesh; stop movement.
                currentPath = null;
            }
        }

        return (start, DotRecastHelper.FromNavMeshSpace(end));
    }

    /// <summary>
    /// Advances the actor in a straight line towards the target position, ignoring navmesh.
    /// </summary>
    /// <param name="start">Current position</param>
    /// <param name="target">Target position</param>
    /// <param name="speed">Movement speed</param>
    /// <param name="deltaTime">Time in seconds</param>
    /// <returns>Tuple of (new position, reachedTarget)</returns>
    public (Vector3 newPosition, bool reachedTarget) FlyAdvance(Vector3 start, Vector3 target, float speed, float deltaTime) {
        Vector3 direction = target - start;
        float distance = direction.Length();
        if (distance == 0) return (target, true);

        float moveDist = speed * deltaTime;
        if (moveDist >= distance) {
            return (target, true);
        }
        Vector3 step = direction / distance * moveDist;
        return (start + step, false);
    }

    public Vector3 GetRandomPatrolPoint() {
        if (!field.FindNearestPoly(npc.Origin, out long startRef, out RcVec3f startVec)) {
            return npc.Position;
        }

        float moveArea = npc.Value.Metadata.Action.MoveArea;
        if (moveArea == 0) {
            return npc.Origin;
        }

        moveArea *= DotRecastHelper.MapRotation.GetRightAxis().Length(); // changing moveArea to navmesh space
        DtStatus end = crowd.GetNavMeshQuery().FindRandomPointWithinCircle(startRef, startVec, moveArea, crowd.GetFilter(0), new RcRand(), out _, out RcVec3f randomPt);
        if (end.Failed()) {
            return npc.Origin;
        }

        return DotRecastHelper.FromNavMeshSpace(randomPt);
    }

    public Vector3 FindRandomPointAround(Vector3 point, int maxDistance, Vector3 fallback) {
        if (!field.FindNearestPoly(point, out long closest, out RcVec3f position)) {
            return fallback;
        }

        float distance = maxDistance * DotRecastHelper.MapRotation.GetRightAxis().Length(); // changing distance to navmesh space
        var status = crowd.GetNavMeshQuery().FindRandomPointAroundCircle(closest, position, distance, crowd.GetFilter(0), new RcRand(), out _, out RcVec3f randomPt);

        if (status.Failed()) {
            return fallback;
        }

        return DotRecastHelper.FromNavMeshSpace(randomPt);
    }

    public Vector3 FindRandomPointAround(Vector3 point, int maxDistance) {
        return FindRandomPointAround(point, maxDistance, DotRecastHelper.FromNavMeshSpace(agent.npos));
    }

    public bool PathTo(Vector3 goal) {
        if (!field.FindNearestPoly(agent.npos, out _, out _)) {
            return false;
        }

        if (!field.FindNearestPoly(goal, out _, out RcVec3f end)) {
            return false;
        }

        return SetPathTo(end);
    }

    private bool SetPathTo(RcVec3f target) {
        currentPath = [];
        currentPathIndex = 0;
        currentPathIsFallback = false;
        currentPathProgress = 0;

        try {
            List<RcVec3f>? path = FindPath(agent.npos, target);
            if (path is null) {
                // Clamp target to nearest navmesh polygon to avoid drifting outside walkable area.
                if (!field.FindNearestPoly(target, out _, out RcVec3f snappedTarget)) {
                    Logger.Warning("[Fallback] Could not clamp target to navmesh. Target(nav)={Target}", target);
                    return false;
                }
                target = snappedTarget;
                Vector3 worldTarget = DotRecastHelper.FromNavMeshSpace(target);

                if (IsVayarianGuardian(npc.Value.Id)) {
                    // Scripted NPC jump/warp: advance trigger even when navmesh has no valid path.
                    Logger.Warning("No path for Vayarian Guardian {NpcId}; snapping from {From} to {To}", npc.Value.Id, npc.Position, worldTarget);
                    UpdatePosition(worldTarget);
                    currentPath = null;
                    return true;
                }

                Logger.Warning("Failed to find path from {FromNav} to {ToNav}; fallback: clamped straight-line on navmesh. World(from)={FromWorld} World(to)={ToWorld}", agent.npos, target, DotRecastHelper.FromNavMeshSpace(agent.npos), worldTarget);
                currentPath = [agent.npos, target];
                return true;
            }

            currentPath = path;
            return true;
        } catch (Exception ex) {
            Logger.Error(ex, "Failed to find path to {Target}", target);
            return false;
        }
    }
    public void ClearPath() {
        currentPath?.Clear();
        currentPathIndex = 0;
        currentPathProgress = 0;
        currentPathIsFallback = false;
    }

    public bool PathAwayFrom(Vector3 goal, int distance) {
        if (!field.FindNearestPoly(agent.npos, out _, out RcVec3f position)) {
            return false;
        }

        // get target in navmesh space
        RcVec3f target = DotRecastHelper.ToNavMeshSpace(goal);

        // get distance in navmesh space
        float fDistance = distance * DotRecastHelper.MapRotation.GetRightAxis().Length();

        // get direction from agent to target (guard against zero-length vectors which would produce NaNs)
        RcVec3f delta = RcVec3f.Subtract(target, position);
        float lenSqr = RcVec3f.Dot(delta, delta);
        RcVec3f direction;
        if (lenSqr > 1e-6f) {
            direction = RcVec3f.Normalize(delta);
        } else {
            // If goal overlaps agent (common during melee hit / on-hit stepback), choose a stable fallback direction.
            // Alternate per-object id so mobs don't all backstep the same way.
            direction = (npc.ObjectId & 1) == 0 ? new RcVec3f(1, 0, 0) : new RcVec3f(-1, 0, 0);
        }

        // get the point that is fDistance away from the agent in the opposite direction
        RcVec3f positionAway = RcVec3f.Add(position, direction * -fDistance);

        // find the nearest poly to the positionAway
        if (!field.FindNearestPoly(positionAway, out _, out RcVec3f positionAwayNavMesh)) {
            return false;
        }

        return SetPathAway(positionAwayNavMesh);
    }
    private bool SetPathAway(RcVec3f target) {
        currentPath = [];
        currentPathIndex = 0;
        currentPathIsFallback = false;
        currentPathProgress = 0;

        try {
            List<RcVec3f>? path = FindPath(agent.npos, target);
            if (path is null) {
                Vector3 worldTarget = DotRecastHelper.FromNavMeshSpace(target);

                if (IsVayarianGuardian(npc.Value.Id)) {
                    // Scripted NPC jump/warp: advance trigger even when navmesh has no valid path.
                    Logger.Warning("No path for Vayarian Guardian {NpcId}; snapping from {From} to {To}", npc.Value.Id, npc.Position, worldTarget);
                    UpdatePosition(worldTarget);
                    currentPath = null;
                    return true;
                }

                Logger.Warning("Failed to find path from {FromNav} to {ToNav}; fallback: clamped straight-line on navmesh. World(from)={FromWorld} World(to)={ToWorld}", agent.npos, target, DotRecastHelper.FromNavMeshSpace(agent.npos), worldTarget);
                currentPath = [agent.npos, target];
                return true;
            }

            currentPath = path;
            return true;
        } catch (Exception ex) {
            Logger.Error(ex, "Failed to find path to {Target}", target);
            return false;
        }
    }
}
