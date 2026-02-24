using Maple2.Server.DebugGame.Graphics.Resources;
using Maple2.Server.DebugGame.Graphics.Data;
using System.Numerics;
using Maple2.Server.DebugGame.Graphics.Data.VertexBuffer;

namespace Maple2.Server.DebugGame.Graphics.Assets;

public class CoreModels {
    public DebugGraphicsContext Context { get; init; }
    public Mesh Quad { get; init; }
    public Mesh Cube { get; init; }
    public Mesh WireCube { get; init; }
    public Mesh Cylinder { get; init; }
    public Mesh WireCylinder { get; init; }

    public CoreModels(DebugGraphicsContext context) {
        Context = context;
        Quad = CreateQuad();

        (Mesh solid, Mesh wire) cubes = CreateCubes();
        (Mesh solid, Mesh wire) cylinders = CreateCylinders();

        Cube = cubes.solid;
        WireCube = cubes.wire;
        Cylinder = cylinders.solid;
        WireCylinder = cylinders.wire;
    }

    private Mesh CreateQuad() {
        Ms2MeshData meshData = new Ms2MeshData {
            PrimitiveCount = 2,
        };

        meshData.SetPositionBinding([
            new PositionBinding(new Vector3(-1,  1, 0)),
            new PositionBinding(new Vector3( 1,  1, 0)),
            new PositionBinding(new Vector3(-1, -1, 0)),
            new PositionBinding(new Vector3( 1, -1, 0)),
        ]);

        meshData.SetAttributeBinding([
            new AttributeBinding(new Vector3(0, 0, -1), 0xFFFFFFFF, new Vector2(0, 0)),
            new AttributeBinding(new Vector3(0, 0, -1), 0xFFFFFFFF, new Vector2(1, 0)),
            new AttributeBinding(new Vector3(0, 0, -1), 0xFFFFFFFF, new Vector2(0, 1)),
            new AttributeBinding(new Vector3(0, 0, -1), 0xFFFFFFFF, new Vector2(1, 1)),
        ]);

        meshData.SetIndexBuffer([
            0, 1, 3,
            0, 3, 2,
        ]);

        Mesh mesh = new Mesh(Context);

        mesh.UploadData(meshData);

        return mesh;
    }

    private (Mesh solid, Mesh wire) CreateCubes() {
        Ms2MeshData meshData = new Ms2MeshData {
            PrimitiveCount = 2,
        };

        PositionBinding[] cubeVertices = [
            new PositionBinding(new Vector3(-0.5f,  0.5f, 0)),
            new PositionBinding(new Vector3( 0.5f,  0.5f, 0)),
            new PositionBinding(new Vector3(-0.5f, -0.5f, 0)),
            new PositionBinding(new Vector3( 0.5f, -0.5f, 0)),
            new PositionBinding(new Vector3(-0.5f,  0.5f, 1)),
            new PositionBinding(new Vector3( 0.5f,  0.5f, 1)),
            new PositionBinding(new Vector3(-0.5f, -0.5f, 1)),
            new PositionBinding(new Vector3( 0.5f, -0.5f, 1)),
        ];

        uint[] cubeSolidIndices = [
            0, 1, 3,
            0, 3, 2,
            4, 7, 5,
            4, 6, 7,
            0, 4, 5,
            0, 5, 1,
            1, 5, 7,
            1, 7, 3,
            3, 7, 6,
            3, 6, 2,
            0, 2, 6,
            0, 6, 4,
        ];

        meshData.SetPositionBinding(cubeVertices);

        meshData.SetAttributeBinding([
            new AttributeBinding(new Vector3(0, 0, -1), 0xFFFFFFFF, new Vector2(0, 0)),
            new AttributeBinding(new Vector3(0, 0, -1), 0xFFFFFFFF, new Vector2(0, 0)),
            new AttributeBinding(new Vector3(0, 0, -1), 0xFFFFFFFF, new Vector2(0, 0)),
            new AttributeBinding(new Vector3(0, 0, -1), 0xFFFFFFFF, new Vector2(0, 0)),
            new AttributeBinding(new Vector3(0, 0, -1), 0xFFFFFFFF, new Vector2(0, 0)),
            new AttributeBinding(new Vector3(0, 0, -1), 0xFFFFFFFF, new Vector2(0, 0)),
            new AttributeBinding(new Vector3(0, 0, -1), 0xFFFFFFFF, new Vector2(0, 0)),
            new AttributeBinding(new Vector3(0, 0, -1), 0xFFFFFFFF, new Vector2(0, 0)),
        ]);

        meshData.IsTriangleMesh = false;

        meshData.SetIndexBuffer([
            0, 1,
            1, 3,
            3, 2,
            2, 0,
            0, 4,
            1, 5,
            3, 7,
            2, 6,
            4, 5,
            5, 7,
            7, 6,
            6, 4,
        ]);

        Mesh cubeWireMesh = new Mesh(Context);

        cubeWireMesh.UploadData(meshData);

        List<PositionBinding> positionBinding = [];
        List<AttributeBinding> attributeBinding = [];

        for (int i = 0; i < cubeSolidIndices.Length; i += 3) {
            PositionBinding vertA = cubeVertices[cubeSolidIndices[i] + 0];
            PositionBinding vertB = cubeVertices[cubeSolidIndices[i] + 1];
            PositionBinding vertC = cubeVertices[cubeSolidIndices[i] + 2];

            positionBinding.Add(vertA);
            positionBinding.Add(vertB);
            positionBinding.Add(vertC);

            Vector3 normal = Vector3.Normalize(Vector3.Cross(vertC.Position - vertA.Position, vertB.Position - vertA.Position));

            attributeBinding.Add(new AttributeBinding(normal, 0xFFFFFFFF, new Vector2(0, 0)));
            attributeBinding.Add(new AttributeBinding(normal, 0xFFFFFFFF, new Vector2(0, 0)));
            attributeBinding.Add(new AttributeBinding(normal, 0xFFFFFFFF, new Vector2(0, 0)));
        }

        meshData.IsTriangleMesh = true;
        meshData.SetPositionBinding(positionBinding.ToArray());
        meshData.SetIndexBuffer(cubeSolidIndices);

        Mesh cubeSolidMesh = new Mesh(Context);

        cubeSolidMesh.UploadData(meshData);

        return (cubeSolidMesh, cubeWireMesh);
    }

    private (Mesh solid, Mesh wire) CreateCylinders() {
        const int segments = 16; // Number of segments around the cylinder
        const float radius = 0.5f;
        const float height = 1.0f;

        Ms2MeshData meshData = new Ms2MeshData {
            PrimitiveCount = 2,
        };

        List<PositionBinding> vertices = [];
        List<uint> wireIndices = [];
        List<uint> solidIndices = [];

        // Create vertices for bottom circle (y = 0)
        for (int i = 0; i < segments; i++) {
            float angle = (float) (2 * Math.PI * i / segments);
            float x = radius * MathF.Cos(angle);
            float z = radius * MathF.Sin(angle);
            vertices.Add(new PositionBinding(new Vector3(x, 0, z)));
        }

        // Create vertices for top circle (y = height)
        for (int i = 0; i < segments; i++) {
            float angle = (float) (2 * Math.PI * i / segments);
            float x = radius * MathF.Cos(angle);
            float z = radius * MathF.Sin(angle);
            vertices.Add(new PositionBinding(new Vector3(x, height, z)));
        }

        // Add center vertices for caps
        vertices.Add(new PositionBinding(new Vector3(0, 0, 0)));      // Bottom center
        vertices.Add(new PositionBinding(new Vector3(0, height, 0))); // Top center

        int bottomCenter = segments * 2;
        int topCenter = segments * 2 + 1;

        // Create wire frame indices
        // Bottom circle
        for (int i = 0; i < segments; i++) {
            wireIndices.Add((uint) i);
            wireIndices.Add((uint) ((i + 1) % segments));
        }

        // Top circle
        for (int i = 0; i < segments; i++) {
            wireIndices.Add((uint) (segments + i));
            wireIndices.Add((uint) (segments + (i + 1) % segments));
        }

        // Vertical lines
        for (int i = 0; i < segments; i++) {
            wireIndices.Add((uint) i);
            wireIndices.Add((uint) (segments + i));
        }

        // Create solid indices
        // Bottom cap triangles
        for (int i = 0; i < segments; i++) {
            solidIndices.Add((uint) bottomCenter);
            solidIndices.Add((uint) ((i + 1) % segments));
            solidIndices.Add((uint) i);
        }

        // Top cap triangles
        for (int i = 0; i < segments; i++) {
            solidIndices.Add((uint) topCenter);
            solidIndices.Add((uint) (segments + i));
            solidIndices.Add((uint) (segments + (i + 1) % segments));
        }

        // Side triangles
        for (int i = 0; i < segments; i++) {
            int next = (i + 1) % segments;

            // First triangle
            solidIndices.Add((uint) i);
            solidIndices.Add((uint) (segments + i));
            solidIndices.Add((uint) next);

            // Second triangle
            solidIndices.Add((uint) next);
            solidIndices.Add((uint) (segments + i));
            solidIndices.Add((uint) (segments + next));
        }

        // Create wire mesh
        meshData.SetPositionBinding(vertices.ToArray());
        meshData.SetAttributeBinding(vertices.Select(_ => new AttributeBinding(Vector3.UnitY, 0xFFFFFFFF, Vector2.Zero)).ToArray());
        meshData.IsTriangleMesh = false;
        meshData.SetIndexBuffer(wireIndices.ToArray());

        Mesh cylinderWireMesh = new Mesh(Context);
        cylinderWireMesh.UploadData(meshData);

        // Create solid mesh
        meshData.IsTriangleMesh = true;
        meshData.SetIndexBuffer(solidIndices.ToArray());

        Mesh cylinderSolidMesh = new Mesh(Context);
        cylinderSolidMesh.UploadData(meshData);

        return (cylinderSolidMesh, cylinderWireMesh);
    }
}

