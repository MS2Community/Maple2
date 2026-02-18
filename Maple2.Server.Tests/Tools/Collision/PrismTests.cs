using System.Numerics;
using Maple2.Tools.Collision;

namespace Maple2.Server.Tests.Tools.Collision;

/// <summary>
/// Tests for Prism.Intersects() — 3D collision combining 2D polygon intersection
/// with a height range overlap check.
/// </summary>
public class PrismTests {
    // Player-sized prism: circle of radius 10, height from Z=0 to Z=100.
    private static Prism PlayerAt(float x, float y, float z) =>
        new(new Circle(new Vector2(x, y), 10), z, 100);

    // Skill box prism: 100 wide, 500 deep, pointing +Y from origin, height 200-500.
    private static Prism SkillBox(float baseZ = 200) =>
        new(new Trapezoid(Vector2.Zero, 100, 100, 500, 0), baseZ, 300);

    [Test]
    public void Prism_PlayerInBox_Intersects() {
        Prism skill = SkillBox();
        Prism player = PlayerAt(0, 250, 250); // centre of skill box, matching height
        Assert.That(skill.Intersects(player), Is.True);
    }

    [Test]
    public void Prism_PlayerBehindBox_NoIntersect() {
        Prism skill = SkillBox();
        Prism player = PlayerAt(0, -200, 250); // behind the box
        Assert.That(skill.Intersects(player), Is.False);
    }

    [Test]
    public void Prism_PlayerAboveBox_NoIntersect() {
        // Skill box spans Z 200–500. Player is at Z 600–700.
        Prism skill = SkillBox();
        Prism player = PlayerAt(0, 250, 600);
        Assert.That(skill.Intersects(player), Is.False);
    }

    [Test]
    public void Prism_PlayerBelowBox_NoIntersect() {
        // Skill box spans Z 200–500. Player is at Z 0–100.
        Prism skill = SkillBox();
        Prism player = PlayerAt(0, 250, 0);
        Assert.That(skill.Intersects(player), Is.False);
    }

    [Test]
    public void Prism_PlayerAtHeightBoundary_Intersects() {
        // Player bottom (Z=190) to top (Z=290) overlaps box bottom (Z=200).
        Prism skill = SkillBox(baseZ: 200);
        Prism player = PlayerAt(0, 250, 190);
        Assert.That(skill.Intersects(player), Is.True);
    }

    [Test]
    public void Prism_PlayerTopTouchesBoxBottom_Intersects() {
        // Player top (Z=100+100=200) exactly touches box bottom (Z=200).
        // Range.Overlaps is inclusive so boundary contact counts as intersection.
        Prism skill = SkillBox(baseZ: 200);
        Prism player = PlayerAt(0, 250, 100); // top = 200, box starts at 200
        Assert.That(skill.Intersects(player), Is.True);
    }

    [Test]
    public void Prism_PlayerClearlyBelowHeightBoundary_NoIntersect() {
        Prism skill = SkillBox(baseZ: 300);
        Prism player = PlayerAt(0, 250, 0); // top = 100, box starts at 300
        Assert.That(skill.Intersects(player), Is.False);
    }

    [Test]
    public void Prism_CylinderSkill_PlayerInside_Intersects() {
        var skill = new Prism(new Circle(Vector2.Zero, 300), 0, 300);
        Prism player = PlayerAt(200, 200, 100);
        Assert.That(skill.Intersects(player), Is.True);
    }

    [Test]
    public void Prism_CylinderSkill_PlayerOutside_NoIntersect() {
        var skill = new Prism(new Circle(Vector2.Zero, 100), 0, 300);
        Prism player = PlayerAt(500, 0, 100);
        Assert.That(skill.Intersects(player), Is.False);
    }
}
