using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

internal class Guild {
    public long Id { get; set; }
    public required string Name { get; set; }
    public string Emblem { get; set; } = string.Empty;
    public string Notice { get; set; } = string.Empty;
    public GuildFocus Focus { get; set; }
    public int Experience { get; set; }
    public int Funds { get; set; }
    public int HouseRank { get; set; }
    public int HouseTheme { get; set; }
    public IList<GuildRank> Ranks { get; set; } = [];
    public IList<GuildBuff> Buffs { get; set; } = [];
    public IList<GuildPoster> Posters { get; set; } = [];
    public IList<GuildNpc> Npcs { get; set; } = [];

    public long LeaderId { get; set; }
    public IList<GuildMember>? Members { get; set; }

    public DateTime CreationTime { get; set; }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator Guild?(Maple2.Model.Game.Guild? other) {
        return other == null ? null : new Guild {
            Id = other.Id,
            Name = other.Name,
            Emblem = other.Emblem,
            Notice = other.Notice,
            Focus = other.Focus,
            Experience = other.Experience,
            Funds = other.Funds,
            HouseRank = other.HouseRank,
            HouseTheme = other.HouseTheme,
            Ranks = other.Ranks.Select(rank => new GuildRank {
                Name = rank.Name,
                Permission = rank.Permission,
            }).ToArray(),
            Buffs = other.Buffs.Select(buff => new GuildBuff {
                Id = buff.Id,
                Level = buff.Level,
                ExpiryTime = buff.ExpiryTime,
            }).ToArray(),
            Posters = other.Posters.Select(poster => new GuildPoster {
                Id = poster.Id,
                Picture = poster.Picture,
                OwnerId = poster.OwnerId,
                OwnerName = poster.OwnerName,
            }).ToArray(),
            Npcs = other.Npcs.Select(npc => new GuildNpc {
                Type = npc.Type,
                Level = npc.Level,
            }).ToArray(),
            LeaderId = other.LeaderCharacterId,
        };
    }

    public static void Configure(EntityTypeBuilder<Guild> builder) {
        builder.ToTable("guild");
        builder.HasKey(guild => guild.Id);
        builder.Property(guild => guild.Ranks).HasJsonConversion();
        builder.Property(guild => guild.Buffs).HasJsonConversion();
        builder.Property(guild => guild.Posters).HasJsonConversion();
        builder.Property(guild => guild.Npcs).HasJsonConversion();

        builder.OneToOne<Guild, Character>()
            .HasForeignKey<Guild>(guild => guild.LeaderId);
        builder.HasMany<GuildMember>(guild => guild.Members);

        IMutableProperty creationTime = builder.Property(guild => guild.CreationTime)
            .ValueGeneratedOnAdd().Metadata;
        creationTime.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }
}

internal class GuildRank {
    public required string Name { get; set; }
    public GuildPermission Permission { get; set; }
}

internal class GuildBuff {
    public int Id { get; set; }
    public int Level { get; set; }
    public long ExpiryTime { get; set; }
}

internal class GuildPoster {
    public int Id { get; set; }
    public required string Picture { get; set; }
    public long OwnerId { get; set; }
    public required string OwnerName { get; set; }
}

internal class GuildNpc {
    public GuildNpcType Type { get; set; }
    public int Level { get; set; }
}
