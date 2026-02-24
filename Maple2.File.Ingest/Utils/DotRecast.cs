using DotRecast.Core.Collections;
using DotRecast.Core.Numerics;
using DotRecast.Recast;
using DotRecast.Recast.Geom;

namespace Maple2.File.Ingest.Utils;

internal class InputGeomProvider : IInputGeomProvider {
    public readonly float[] vertices;

    public readonly int[] faces;

    public readonly float[] normals;

    private RcVec3f bmin;

    private RcVec3f bmax;

    private readonly RcTriMesh mesh;
    private readonly List<RcOffMeshConnection> offMeshConnections;
    private readonly List<RcConvexVolume> convexVolumes;


    public InputGeomProvider(List<float> verts, List<int> tris) {
        vertices = MapVertices(verts);
        faces = MapFaces(tris);

        normals = new float[faces.Length];
        CalculateNormals();
        bmin = RcVecUtils.Create(vertices);
        bmax = RcVecUtils.Create(vertices);
        for (int i = 1; i < vertices.Length / 3; i++) {
            bmin = RcVecUtils.Min(bmin, vertices, i * 3);
            bmax = RcVecUtils.Max(bmax, vertices, i * 3);
        }

        mesh = new RcTriMesh(vertices, faces);
        offMeshConnections = [];
        convexVolumes = [];
    }

    private void CalculateNormals() {
        for (int i = 0; i < faces.Length; i += 3) {
            int num = faces[i] * 3;
            int num2 = faces[i + 1] * 3;
            int num3 = faces[i + 2] * 3;
            RcVec3f rcVec3f = default;
            RcVec3f rcVec3f2 = default;
            rcVec3f.X = vertices[num2] - vertices[num];
            rcVec3f.Y = vertices[num2 + 1] - vertices[num + 1];
            rcVec3f.Z = vertices[num2 + 2] - vertices[num + 2];
            rcVec3f2.X = vertices[num3] - vertices[num];
            rcVec3f2.Y = vertices[num3 + 1] - vertices[num + 1];
            rcVec3f2.Z = vertices[num3 + 2] - vertices[num + 2];
            normals[i] = rcVec3f.Y * rcVec3f2.Z - rcVec3f.Z * rcVec3f2.Y;
            normals[i + 1] = rcVec3f.Z * rcVec3f2.X - rcVec3f.X * rcVec3f2.Z;
            normals[i + 2] = rcVec3f.X * rcVec3f2.Y - rcVec3f.Y * rcVec3f2.X;
            float num4 = MathF.Sqrt(normals[i] * normals[i] + normals[i + 1] * normals[i + 1] + normals[i + 2] * normals[i + 2]);
            if (num4 > 0f) {
                num4 = 1f / num4;
                normals[i] *= num4;
                normals[i + 1] *= num4;
                normals[i + 2] *= num4;
            }
        }
    }

    private static int[] MapFaces(List<int> meshFaces) {
        int[] array = new int[meshFaces.Count];
        for (int i = 0; i < array.Length; i++) {
            array[i] = meshFaces[i];
        }

        return array;
    }

    private static float[] MapVertices(List<float> vertexPositions) {
        float[] array = new float[vertexPositions.Count];
        for (int i = 0; i < array.Length; i++) {
            array[i] = vertexPositions[i];
        }

        return array;
    }

    public List<RcOffMeshConnection> GetOffMeshConnections() {
        return offMeshConnections;
    }

    public void AddOffMeshConnection(RcVec3f start, RcVec3f end, float radius, bool bidir, int area, int flags) {
        offMeshConnections.Add(new RcOffMeshConnection(start, end, radius, true, area, flags));
    }

    public void RemoveOffMeshConnections(Predicate<RcOffMeshConnection> filter) {
        offMeshConnections.RemoveAll(filter);
    }

    public void AddConvexVolume(float[] verts, float minh, float maxh, RcAreaModification areaMod) {
        AddConvexVolume(new RcConvexVolume {
            verts = verts,
            hmin = minh,
            hmax = maxh,
            areaMod = areaMod,
        });
    }

    public void AddConvexVolume(RcConvexVolume volume) {
        convexVolumes.Add(volume);
    }

    public IList<RcConvexVolume> ConvexVolumes() {
        return convexVolumes;
    }

    public RcTriMesh GetMesh() {
        return mesh;
    }

    public RcVec3f GetMeshBoundsMax() {
        return bmax;
    }

    public RcVec3f GetMeshBoundsMin() {
        return bmin;
    }

    public IEnumerable<RcTriMesh> Meshes() {
        return RcImmutableArray.Create(mesh);
    }
}
