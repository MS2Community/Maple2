using System.Diagnostics.CodeAnalysis;
using Maple2.Model.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

internal class GameEventUserValue {
    public long CharacterId { get; set; }
    public GameEventUserValueType Type { get; set; }
    public required string Value { get; set; }
    public int EventId { get; set; }
    public long ExpirationTime { get; set; }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator GameEventUserValue?(Maple2.Model.Game.GameEventUserValue? other) {
        return other == null ? null : new GameEventUserValue {
            Type = other.Type,
            Value = other.Value,
            EventId = other.EventId,
            ExpirationTime = other.ExpirationTime,
        };
    }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator Maple2.Model.Game.GameEventUserValue?(GameEventUserValue? other) {
        return other == null ? null : new Maple2.Model.Game.GameEventUserValue(other.Value) {
            Type = other.Type,
            EventId = other.EventId,
            ExpirationTime = other.ExpirationTime,
        };
    }

    public static void Configure(EntityTypeBuilder<GameEventUserValue> builder) {
        builder.ToTable("game-event-user-value");
        builder.HasKey(value => new { value.CharacterId, value.EventId, value.Type });
        builder.HasOne<Character>()
            .WithMany()
            .HasForeignKey(value => value.CharacterId);
    }
}
