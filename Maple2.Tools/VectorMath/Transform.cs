using System;
using System.Numerics;
using Maple2.Tools.Extensions;

namespace Maple2.Tools.VectorMath;

// A transform is a container for object position & orientation data, stored in a transformation matrix.
// Transformation matrices are a fairly universal form of storage for positioning data.
// They're fairly well suited for game environments because they're a natural unit to work with when manipulating vertex data & hierarchies.
// They lend themselves fairly well to quick conversion between different desired input & output formats for positioning data.
public class Transform {
    public const float EPSILON = 0.001f; // Used for computing equality within a range of error.

    public Matrix4x4 Transformation = Matrix4x4.Identity;

    // Describes the position. Matrix components: M41 - M43
    public Vector3 Position {
        get { return Transformation.Translation; }
        set { Transformation.Translation = value; }
    }

    // Describes the rotation with Euler angles in MS2's space.
    public Vector3 RotationAngles {
        get { return Transformation.GetRotationAngles(); }
        set { RotationMatrix = NewRotationAngles(value); }
    }

    // Describes the rotation with Euler angles in MS2's space.
    public Vector3 RotationAnglesDegrees {
        get { return Transformation.GetRotationAngles().AnglesToDegrees(); }
        set { RotationMatrix = NewRotationAngles(value.AnglesToRadians()); }
    }

    // Describes the rotation with a rotation around an axis.
    public (Vector3 axis, float angle) RotationAxis {
        get {
            Quaternion quaternion = Transformation.GetQuaternion();

            Vector3 axis = new Vector3(quaternion.X, quaternion.Y, quaternion.Z);
            float squareLength = axis.LengthSquared();

            if (squareLength < 1e-5f) {
                axis = new Vector3(0, 0, 1);
            }

            axis = Vector3.Normalize(axis);

            float angle = (float) Math.Acos(quaternion.W) * 2;

            return (axis, angle);
        }
        set { RotationMatrix = Matrix4x4.CreateFromAxisAngle(value.axis, value.angle); }
    }

    // Describes the rotation with a rotation around an axis.
    public (Vector3 axis, float angle) RotationAxisDegrees {
        get {
            (Vector3 axis, float angle) rotation = RotationAxis;

            return (rotation.axis, 180 * rotation.angle / (float) Math.PI);
        }
        set { RotationAxis = (value.axis, (float) Math.PI * value.angle / 180); }
    }

    // Describes the rotation with a quaternion. Quaternions are 4D vectors of imaginary numbers that behave and look similarly to 3D axis angles.
    public Quaternion Quaternion {
        get { return Transformation.GetQuaternion(); }
        set { RotationMatrix = Matrix4x4.CreateFromQuaternion(value); }
    }

    // The rotation portion of the transformation matrix ignoring position & scale.
    public Matrix4x4 RotationMatrix {
        get { return Transformation.GetRotationMatrix(); }
        set {
            float scale = RightAxis.Length();
            Vector3 position = Position;

            Transformation = value;
            Position = position;
            Scale = scale;
        }
    }

    // May be slow because of square root, only use for exact values. Comparisons of values can be done with ScaleSquared.
    // This is a float because Gamebryo uses float scalings that are uniform across all axis vs Vector3 scalings.
    public float Scale {
        get { return UpAxis.Length(); }
        set {
            float scale = Scale;

            // Normalize each axis before scaling to prevent floating point drift.
            RightAxis = Vector3.Normalize(RightAxis) * value;
            UpAxis = Vector3.Normalize(UpAxis) * value;
            FrontAxis = Vector3.Normalize(FrontAxis) * value;
        }
    }

    // Faster than Scale because it doesn't require square root. Good for comparing between two scales.
    // This is a float because Gamebryo uses float scalings that are uniform across all axis vs Vector3 scalings.
    public float ScaleSquared {
        get { return UpAxis.LengthSquared(); }
    }

    // Right facing axis of the object represented by the transform.
    public Vector3 RightAxis {
        get { return -Transformation.GetRightAxis(); }
        set {
            Transformation.M11 = -value.X;
            Transformation.M12 = -value.Y;
            Transformation.M13 = -value.Z;
        }
    }

    // Up facing axis of the object represented by the transform.
    public Vector3 UpAxis {
        get { return Transformation.GetUpAxis(); }
        set {
            Transformation.M31 = value.X;
            Transformation.M32 = value.Y;
            Transformation.M33 = value.Z;
        }
    }

    // Front facing axis of the object represented by the transform.
    public Vector3 FrontAxis {
        get { return -Transformation.GetFrontAxis(); }
        set {
            Transformation.M21 = -value.X;
            Transformation.M22 = -value.Y;
            Transformation.M23 = -value.Z;
        }
    }

    public Transform() { }

    public void LookTo(Vector3 direction, bool snapToGroundPlane = true) {
        LookTo(direction, new Vector3(0, 0, 1), snapToGroundPlane);
    }

    public void LookTo(Vector3 direction, Vector3 up, bool snapToGroundPlane = true) {
        // Defensive normalization: callers sometimes pass a zero vector (or already-invalid values),
        // which would turn into NaN via Vector3.Normalize and then poison FrontAxis/RightAxis/UpAxis.
        // That can later corrupt movement (e.g., skill cast keyframe movement) and ultimately navmesh queries.
        if (float.IsNaN(direction.X) || float.IsNaN(direction.Y) || float.IsNaN(direction.Z) ||
            float.IsInfinity(direction.X) || float.IsInfinity(direction.Y) || float.IsInfinity(direction.Z) ||
            direction.LengthSquared() < 1e-6f) {
            direction = FrontAxis;
        }
        if (float.IsNaN(up.X) || float.IsNaN(up.Y) || float.IsNaN(up.Z) ||
            float.IsInfinity(up.X) || float.IsInfinity(up.Y) || float.IsInfinity(up.Z) ||
            up.LengthSquared() < 1e-6f) {
            up = new Vector3(0, 0, 1);
        }

        direction = Vector3.Normalize(direction);
        up = Vector3.Normalize(up);

        if (snapToGroundPlane) {
            // Project direction onto plane defined by up.
            Vector3 projected = direction - Vector3.Dot(direction, up) * up;
            if (float.IsNaN(projected.X) || float.IsNaN(projected.Y) || float.IsNaN(projected.Z) ||
                float.IsInfinity(projected.X) || float.IsInfinity(projected.Y) || float.IsInfinity(projected.Z) ||
                projected.LengthSquared() < 1e-6f) {
                projected = FrontAxis;
            }
            direction = Vector3.Normalize(projected);
        }

        Vector3 right = Vector3.Cross(direction, up);
        if (float.IsNaN(right.X) || float.IsNaN(right.Y) || float.IsNaN(right.Z) ||
            float.IsInfinity(right.X) || float.IsInfinity(right.Y) || float.IsInfinity(right.Z) ||
            right.LengthSquared() < 1e-6f) {
            // Fallback: keep current basis if cross product degenerated.
            right = RightAxis.LengthSquared() < 1e-6f ? Vector3.UnitX : Vector3.Normalize(RightAxis);
        } else {
            right = Vector3.Normalize(right);
        }

        up = Vector3.Cross(right, direction);

        float scale = Scale;

        RightAxis = scale * right;
        UpAxis = scale * up;
        FrontAxis = scale * direction;
    }

    // Rotations happen in Z (yaw) -> Y (roll) -> X (pitch) order, which is MS2's Euler angles format.
    public static Matrix4x4 NewRotationAngles(Vector3 angles) {
        return NewRotationAngles(angles.X, angles.Y, angles.Z);
    }

    // Rotations happen in yaw -> roll -> pitch order, which is MS2's Euler angles format.
    public static Matrix4x4 NewRotationAngles(float pitch, float roll, float yaw) {
        return Matrix4x4.CreateFromYawPitchRoll(roll, pitch, yaw);
    }

    // Rotates around around the Z, up & down axis.
    public static Matrix4x4 NewYawRotation(float angle) {
        return Matrix4x4.CreateRotationZ(angle);
    }

    // Rotates around the Y axis.
    public static Matrix4x4 NewRollRotation(float angle) {
        return Matrix4x4.CreateRotationY(angle);
    }

    // Rotates around the X axis.
    public static Matrix4x4 NewPitchRotation(float angle) {
        return Matrix4x4.CreateRotationX(angle);
    }
}
