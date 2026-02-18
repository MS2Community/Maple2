using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

internal class GuildMember {
    public long GuildId { get; set; }
    public long CharacterId { get; set; }

    public string Message { get; set; } = string.Empty;
    public byte Rank { get; set; }

    public int WeeklyContribution { get; set; }
    public int TotalContribution { get; set; }
    public int DailyDonationCount { get; set; }

    public Character? Character { get; set; }

    public DateTime CheckinTime { get; set; }
    public DateTime DonationTime { get; set; }
    public DateTime CreationTime { get; set; }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator GuildMember?(Maple2.Model.Game.GuildMember? other) {
        return other == null ? null : new GuildMember {
            GuildId = other.GuildId,
            CharacterId = other.CharacterId,
            Message = other.Message,
            Rank = other.Rank,
            WeeklyContribution = other.WeeklyContribution,
            TotalContribution = other.TotalContribution,
            DailyDonationCount = other.DailyDonationCount,
            CheckinTime = other.CheckinTime.FromEpochSeconds(),
            DonationTime = other.DonationTime.FromEpochSeconds(),
        };
    }

    public static void Configure(EntityTypeBuilder<GuildMember> builder) {
        builder.ToTable("guild-member");
        builder.HasKey(member => new { member.GuildId, member.CharacterId });
        builder.OneToOne<GuildMember, Character>(member => member.Character)
            .HasForeignKey<GuildMember>(member => member.CharacterId);
        builder.HasOne<Guild>()
            .WithMany(guild => guild.Members)
            .HasForeignKey(member => member.GuildId);

        IMutableProperty creationTime = builder.Property(member => member.CreationTime)
            .ValueGeneratedOnAdd().Metadata;
        creationTime.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }
}
