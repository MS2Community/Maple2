using System.Numerics;
using Maple2.Tools.Collision;

namespace Maple2.Server.Tests.Tools.Collision;

public class HoleHoleCircleTest {
    [Test]
    public void ContainsTest() {
        var origin = new Vector2(1, 1);
        const float innerRadius = 2;
        const float outerRadius = 4;
        var circle = new HoleCircle(origin, innerRadius, outerRadius);

        var insidePoint = new Vector2(3.5f, 3.5f);
        Assert.That(circle.Contains(insidePoint), Is.True);
        var outerEdgePoint = new Vector2(5, 1);
        Assert.That(circle.Contains(outerEdgePoint), Is.True);

        var innerEdgePoint = new Vector2(3, 1);
        Assert.That(circle.Contains(innerEdgePoint), Is.False);
        var holePoint = new Vector2(1.5f, 1.5f);
        Assert.That(circle.Contains(holePoint), Is.False);
        var outsidePoint = new Vector2(5.5f, 2);
        Assert.That(circle.Contains(outsidePoint), Is.False);
    }

    [Test]
    public void RectangleIntersectsTest() {
        var circle = new HoleCircle(new Vector2(1, 1), 2, 4);
        {
            var rectangle = new Rectangle(new Vector2(1, 2), 4, 6, 0);
            Assert.That(circle.Intersects(rectangle), Is.True);
        }
        {
            var rectangle = new Rectangle(new Vector2(7, 7), 2, 2, 0);
            Assert.That(circle.Intersects(rectangle), Is.False);
        }
        {
            // Rectangle contained within hole.
            var rectangle = new Rectangle(new Vector2(1, 1), 1, 1, 0);
            Assert.That(circle.Intersects(rectangle), Is.False);
        }
    }

    [Test]
    public void TrapezoidIntersectsTest() {
        var circle = new HoleCircle(new Vector2(1, 1), 2, 4);
        {
            var trapezoid = new Trapezoid(new Vector2(1, 2), 4, 6, 3, 0);
            Assert.That(circle.Intersects(trapezoid), Is.True);
        }
        {
            var trapezoid = new Trapezoid(new Vector2(6, 6), 4, 6, 3, 0);
            Assert.That(circle.Intersects(trapezoid), Is.False);
        }
        {
            // Trapezoid contained within hole.
            var trapezoid = new Trapezoid(new Vector2(1, 1), 1, 2, 1, 0);
            Assert.That(circle.Intersects(trapezoid), Is.False);
        }
    }

    [Test]
    public void CircleIntersectsTest() {
        var circle = new HoleCircle(new Vector2(1, 1), 2, 4);
        {
            var other = new Circle(new Vector2(3, 3), 3);
            Assert.That(circle.Intersects(other), Is.True);
        }
        {
            var other = new Circle(new Vector2(8, 8), 3);
            Assert.That(circle.Intersects(other), Is.False);
        }
        {
            // Circle contained within hole
            var other = new Circle(new Vector2(1, 1), 1.9f);
            Assert.That(circle.Intersects(other), Is.False);
        }
    }

    [Test]
    public void HoleCircleIntersectsTest() {
        var circle = new HoleCircle(new Vector2(1, 1), 2, 4);
        {
            var other = new HoleCircle(new Vector2(1, 2), 2, 4);
            Assert.That(circle.Intersects(other), Is.True);
        }
        {
            var other = new HoleCircle(new Vector2(8, 8), 2, 3);
            Assert.That(circle.Intersects(other), Is.False);
        }
        {
            // HoleCircle contained within hole.
            var other = new HoleCircle(new Vector2(1, 1), 1, 1.9f);
            Assert.That(circle.Intersects(other), Is.False);
        }
    }
}
