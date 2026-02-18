using System.Numerics;
using Maple2.Tools.Collision;

namespace Maple2.Server.Tests.Tools.Collision;

public class CircleTests {
    [Test]
    public void ContainsTest() {
        var origin = new Vector2(1, 1);
        const float radius = 3;
        var circle = new Circle(origin, radius);

        var insidePoint = new Vector2(1.5f, 1.5f);
        Assert.That(circle.Contains(insidePoint), Is.True);
        var edgePoint = new Vector2(1, 4);
        Assert.That(circle.Contains(edgePoint), Is.True);
        var outsidePoint = new Vector2(4.5f, 1.5f);
        Assert.That(circle.Contains(outsidePoint), Is.False);
    }

    [Test]
    public void RectangleIntersectsTest() {
        var circle = new Circle(new Vector2(1, 1), 3);
        {
            var rectangle = new Rectangle(new Vector2(1, 2), 4, 6, 0);
            Assert.That(circle.Intersects(rectangle), Is.True);
        }
        {
            var rectangle = new Rectangle(new Vector2(5, 5), 2, 2, 0);
            Assert.That(circle.Intersects(rectangle), Is.False);
        }
    }

    [Test]
    public void TrapezoidIntersectsTest() {
        var circle = new Circle(new Vector2(1, 1), 3);
        {
            var trapezoid = new Trapezoid(new Vector2(1, 2), 4, 6, 3, 0);
            Assert.That(circle.Intersects(trapezoid), Is.True);
        }
        {
            var trapezoid = new Trapezoid(new Vector2(4, 4), 4, 6, 3, 0);
            Assert.That(circle.Intersects(trapezoid), Is.False);
        }
    }

    [Test]
    public void CircleIntersectsTest() {
        var circle = new Circle(new Vector2(1, 1), 3);
        {
            var other = new Circle(new Vector2(3, 3), 3);
            Assert.That(circle.Intersects(other), Is.True);
        }
        {
            var other = new Circle(new Vector2(7, 7), 3);
            Assert.That(circle.Intersects(other), Is.False);
        }
    }

    [Test]
    public void HoleCircleIntersectsTest() {
        var circle = new Circle(new Vector2(1, 1), 2);
        {
            var holeCircle = new HoleCircle(new Vector2(1, 2), 2, 4);
            Assert.That(circle.Intersects(holeCircle), Is.True);
        }
        {
            var holeCircle = new HoleCircle(new Vector2(7, 7), 2, 4);
            Assert.That(circle.Intersects(holeCircle), Is.False);
        }
        {
            // Circle contained within hole.
            var holeCircle = new HoleCircle(new Vector2(1, 1), 3, 5);
            Assert.That(circle.Intersects(holeCircle), Is.False);
        }
    }
}
