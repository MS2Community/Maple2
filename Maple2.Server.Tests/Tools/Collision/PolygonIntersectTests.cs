using System.Numerics;
using Maple2.Tools.Collision;

namespace Maple2.Server.Tests.Tools.Collision;

/// <summary>
/// Tests for Polygon.Intersects() covering the SAT (Separating Axis Theorem)
/// for both polygon-polygon and polygon-circle cases.
///
/// Trapezoid at angle=0 extends from origin along +Y:
///   P0=(-halfW, 0), P1=(halfW, 0), P2=(halfEndW, dist), P3=(-halfEndW, dist)
///
/// Note: Circle.AxisProjection uses Radius directly against non-normalized edge
/// normals, so barely-touching-edge cases may not be reliable. Tests use shapes
/// that are clearly inside or clearly outside.
/// </summary>
public class PolygonIntersectTests {
    // Box: origin at (0,0), 100 wide, 500 deep, pointing +Y (angle=0°).
    // Points: (-50,0), (50,0), (50,500), (-50,500).

    [Test]
    public void Trapezoid_CircleInFront_Intersects() {
        var box = new Trapezoid(Vector2.Zero, 100, 100, 500, 0);
        var circle = new Circle(new Vector2(0, 250), 10); // centre of box
        Assert.That(box.Intersects(circle), Is.True);
    }

    [Test]
    public void Trapezoid_CircleAtTip_Intersects() {
        var box = new Trapezoid(Vector2.Zero, 100, 100, 500, 0);
        var circle = new Circle(new Vector2(0, 480), 10); // near far edge, clearly inside
        Assert.That(box.Intersects(circle), Is.True);
    }

    [Test]
    public void Trapezoid_CircleBehind_NoIntersect() {
        var box = new Trapezoid(Vector2.Zero, 100, 100, 500, 0);
        var circle = new Circle(new Vector2(0, -200), 10); // clearly behind the near edge
        Assert.That(box.Intersects(circle), Is.False);
    }

    [Test]
    public void Trapezoid_CircleBeyondTip_NoIntersect() {
        var box = new Trapezoid(Vector2.Zero, 100, 100, 500, 0);
        var circle = new Circle(new Vector2(0, 700), 10); // well past far edge
        Assert.That(box.Intersects(circle), Is.False);
    }

    [Test]
    public void Trapezoid_CircleToSide_NoIntersect() {
        var box = new Trapezoid(Vector2.Zero, 100, 100, 500, 0);
        var circle = new Circle(new Vector2(300, 250), 10); // far to the right
        Assert.That(box.Intersects(circle), Is.False);
    }

    // At angle=90°, Normal(x,y)=(y,-x) means the box extends in -X direction.
    // Points at 90°: (0,-50), (0,50), (-500,50), (-500,-50).

    [Test]
    public void Trapezoid_CircleInFrontRotated90_Intersects() {
        // Box at 90° extends in -X: from x=0 to x=-500, y=-50 to y=50.
        var box = new Trapezoid(Vector2.Zero, 100, 100, 500, 90);
        var circle = new Circle(new Vector2(-250, 0), 10); // centre of the rotated box
        Assert.That(box.Intersects(circle), Is.True);
    }

    [Test]
    public void Trapezoid_CircleBehindRotated90_NoIntersect() {
        // Box at 90° extends in -X. Circle at +X is clearly behind the near edge.
        var box = new Trapezoid(Vector2.Zero, 100, 100, 500, 90);
        var circle = new Circle(new Vector2(300, 0), 10); // opposite side
        Assert.That(box.Intersects(circle), Is.False);
    }

    [Test]
    public void Trapezoid_FrustumCircleInsideNarrowEnd_Intersects() {
        // Frustum: 200 wide at origin, 50 wide at distance 300.
        var frustum = new Trapezoid(Vector2.Zero, 200, 50, 300, 0);
        var circle = new Circle(new Vector2(15, 280), 10); // inside narrow section
        Assert.That(frustum.Intersects(circle), Is.True);
    }

    [Test]
    public void Trapezoid_FrustumCircleOutsideNarrowEnd_NoIntersect() {
        // Circle is well outside the narrow far end of the frustum.
        var frustum = new Trapezoid(Vector2.Zero, 200, 50, 300, 0);
        var circle = new Circle(new Vector2(150, 280), 10); // far outside narrow edge
        Assert.That(frustum.Intersects(circle), Is.False);
    }

    [Test]
    public void TrapezoidIntersectsTrapezoid_Overlapping() {
        var a = new Trapezoid(Vector2.Zero, 100, 100, 200, 0);
        var b = new Trapezoid(new Vector2(0, 100), 100, 100, 200, 0); // overlaps a
        Assert.That(a.Intersects(b), Is.True);
    }

    [Test]
    public void TrapezoidIntersectsTrapezoid_Separated() {
        var a = new Trapezoid(Vector2.Zero, 100, 100, 200, 0);
        var b = new Trapezoid(new Vector2(500, 0), 100, 100, 200, 0); // far to the right
        Assert.That(a.Intersects(b), Is.False);
    }
}
