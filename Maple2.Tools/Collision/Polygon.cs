using System.Diagnostics;
using System.Numerics;
using System.Text;
using Maple2.Tools.Extensions;

namespace Maple2.Tools.Collision;

// Polygon is assumed to be convex.
public abstract class Polygon : IPolygon {
    public abstract Vector2[] Points { get; }

    public virtual bool Contains(in Vector2 point, float epsilon = 1e-5f) {
        // Check if a triangle or higher n-gon
        Debug.Assert(Points.Length >= 3);

        // n>2 Keep track of cross product sign changes
        int pos = 0;
        int neg = 0;

        for (int i = 0; i < Points.Length; i++) {
            // Form a segment between the i'th point
            float x1 = Points[i].X;
            float y1 = Points[i].Y;

            // And the i+1'th, or if it is the last, with the first point
            int i2 = (i + 1) % Points.Length;

            float x = point.X;
            float x2 = Points[i2].X;

            float y = point.Y;
            float y2 = Points[i2].Y;

            // Compute the cross product
            float d = (x - x1) * (y2 - y1) - (y - y1) * (x2 - x1);

            if (d > epsilon) {
                pos++;
            } else if (d < -epsilon) {
                neg++;
            }

            // If the sign changes, then point is outside
            if (pos > 0 && neg > 0) {
                return false;
            }
        }

        // If no change in direction, then on same side of all segments, and thus inside
        return true;
    }

    public Vector2[] GetAxes(Polygon? other) {
        var result = new Vector2[Points.Length];
        for (int i = 0; i < Points.Length; i++) {
            Vector2 p1 = Points[i];
            Vector2 p2 = Points[(i + 1) % Points.Length];
            // These vectors are called "normal" vectors. These vectors are not normalized however (not of unit length).
            // If you need only a boolean result from the SAT algorithm this will suffice, but if you need the collision
            // information then these vectors will need to be normalized.
            result[i] = (p1 - p2).Normal();
        }

        return result;
    }

    public Range AxisProjection(Vector2 axis) {
        float min = Vector2.Dot(axis, Points[0]);
        float max = min;

        foreach (Vector2 point in Points) {
            float projection = Vector2.Dot(axis, point);
            if (projection < min) {
                min = projection;
            } else if (projection > max) {
                max = projection;
            }
        }

        return new Range(min, max);
    }

    public bool Intersects(IPolygon other) {
        foreach (Vector2 axis in other.GetAxes(this)) {
            Range range = AxisProjection(axis);
            Range otherRange = other.AxisProjection(axis);
            if (!range.Overlaps(otherRange)) {
                return false;
            }
        }
        // Always test this polygon's edge normals (required for correct SAT)
        // Previously only tested when other was also a Polygon, which broke Circle vs Polygon checks
        foreach (Vector2 axis in GetAxes(other as Polygon)) {
            Range range = AxisProjection(axis);
            Range otherRange = other.AxisProjection(axis);
            if (!range.Overlaps(otherRange)) {
                return false;
            }
        }

        return true;
    }

    public override string ToString() {
        var builder = new StringBuilder();
        foreach (Vector2 point in Points) {
            builder.Append($"({point.X}, {point.Y}), ");
        }

        return builder.ToString(0, builder.Length - 2);
    }
}
