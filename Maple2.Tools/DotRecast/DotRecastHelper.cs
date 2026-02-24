using System;
using System.Numerics;
using DotRecast.Core.Numerics;
using DotRecast.Recast;
using DotRecast.Recast.Toolset;

namespace Maple2.Tools.DotRecast;

public static class DotRecastHelper {
    public const int VERTS_PER_POLY = 6;
    public const float CELL_SIZE = 0.05f;
    public static readonly RcNavMeshBuildSettings NavMeshBuildSettings = new() {
        cellSize = CELL_SIZE,
        cellHeight = CELL_SIZE,
        agentHeight = 1.4f, // approximation of character height
        agentRadius = 0.3f, // approximation of character radius
        agentMaxClimb = 0.7f,
        agentMaxSlope = 47f, // generally 45 degrees, but we add a bit more to account for floating point errors
        agentMaxAcceleration = 8f,
        agentMaxSpeed = 3.5f,
        minRegionSize = 8,
        mergedRegionSize = 20,
        partitioning = (int) RcPartition.WATERSHED,
        filterLowHangingObstacles = true,
        filterLedgeSpans = true,
        filterWalkableLowHeightSpans = true,
        edgeMaxLen = 12f,
        edgeMaxError = 1.3f,
        vertsPerPoly = VERTS_PER_POLY,
        detailSampleDist = 6f,
        detailSampleMaxError = 3f,
        keepInterResults = true,
    };

    public const float STEP_SIZE = 0.5f;
    public const float MIN_TARGET_DIST = 0.01f;

    public static readonly Matrix4x4 MapRotation = Matrix4x4.CreateFromAxisAngle(Vector3.UnitX, (float) (-Math.PI / 2)) * Matrix4x4.CreateScale(1 / 100f);
    public static readonly Matrix4x4 MapRotationInv = Matrix4x4.CreateScale(100f) * Matrix4x4.CreateFromAxisAngle(Vector3.UnitX, (float) (Math.PI / 2));

    public static RcVec3f ToNavMeshSpace(Vector3 vector) {
        Vector3 transform = Vector3.Transform(vector, MapRotation);
        return new RcVec3f(transform.X, transform.Y, transform.Z);
    }

    public static Vector3 FromNavMeshSpace(RcVec3f position) {
        Vector3 vector = new(position.X, position.Y, position.Z);
        return Vector3.Transform(vector, MapRotationInv);
    }
}
