using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

public class MobAttackCommand : GameCommand {
    private readonly GameSession session;

    public MobAttackCommand(GameSession session) : base(AdminPermissions.Debug, "mob-attack", "Make first mob on map use a skill.") {
        this.session = session;

        var skillId = new Argument<int?>("skillId", () => null, "Skill id for the mob to use.");
        var skillLevel = new Option<short>(["--level", "-l"], () => 1, "Skill level.");

        AddArgument(skillId);
        AddOption(skillLevel);
        this.SetHandler<InvocationContext, int?, short>(Handle, skillId, skillLevel);
    }

    private void Handle(InvocationContext ctx, int? skillId, short skillLevel) {
        if (session.Field is null) {
            ctx.Console.Error.WriteLine("No field loaded.");
            return;
        }

        // Find first mob on the map
        FieldNpc? mob = session.Field.Mobs.Values.FirstOrDefault();
        if (mob is null) {
            ctx.Console.Error.WriteLine("No mobs found on the current map.");
            return;
        }

        ctx.Console.Out.WriteLine($"Using mob: {mob.Value.Metadata.Name} (Id: {mob.Value.Metadata.Id}, ObjectId: {mob.ObjectId})");

        // Check if mob has skills
        NpcMetadataSkill.Entry[] entries = mob.Value.Metadata.Skill.Entries;
        if (entries.Length == 0) {
            ctx.Console.Error.WriteLine("This mob has no skills.");
            return;
        }

        // If no skill id passed, list available skills grouped by id
        if (skillId is null) {
            ctx.Console.Out.WriteLine("Available skills:");
            foreach (var group in entries.GroupBy(e => e.Id)) {
                string levels = string.Join(", ", group.Select(e => e.Level));
                ctx.Console.Out.WriteLine($"  SkillId: {group.Key} (Levels: {levels})");
            }
            return;
        }

        // Validate skill exists in metadata
        if (!session.Field.SkillMetadata.TryGet(skillId.Value, skillLevel, out SkillMetadata? _)) {
            ctx.Console.Error.WriteLine($"Skill {skillId.Value} level {skillLevel} not found in metadata.");
            return;
        }

        // Cast the skill facing the player
        mob.CastAiSkill(skillId.Value, skillLevel, faceTarget: 1, facePos: session.Player.Position);
        ctx.Console.Out.WriteLine($"Mob {mob.Value.Metadata.Name} casting skill {skillId.Value} (level {skillLevel}).");
    }
}
