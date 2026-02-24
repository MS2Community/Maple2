using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

internal class Character {
    public long AccountId { get; set; }
    public long Id { get; set; }
    public required string Name { get; set; }
    public Gender Gender { get; set; }
    public Job Job { get; set; }
    public short Level { get; set; }
    public SkinColor SkinColor { get; set; }
    public int MapId { get; set; }
    public int ReturnMapId { get; set; }
    public short Channel { get; set; }
    public short ReturnChannel { get; set; }
    public MentorRole MentorRole { get; set; }
    public required Experience Experience { get; set; }
    public required Profile Profile { get; set; }
    public required Cooldown Cooldown { get; set; }
    public required CharacterCurrency Currency { get; set; }
    public required Mastery Mastery { get; set; }
    public DateTime DeleteTime { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime LastModified { get; init; }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator Character?(Maple2.Model.Game.Character? other) {
        return other == null ? null : new Character {
            LastModified = other.LastModified,
            AccountId = other.AccountId,
            Id = other.Id,
            Name = other.Name,
            Gender = other.Gender,
            Job = other.Job,
            Level = other.Level,
            SkinColor = other.SkinColor,
            MapId = other.MapId,
            ReturnMapId = other.ReturnMaps.Peek(),
            Experience = new Experience {
                Exp = other.Exp,
                RestExp = other.RestExp,
            },
            Profile = new Profile {
                Motto = other.Motto,
                Picture = other.Picture,
                Title = other.Title,
                Insignia = other.Insignia,
            },
            Cooldown = new Cooldown {
                Doctor = other.DoctorCooldown,
                Storage = other.StorageCooldown,
            },
            Currency = new CharacterCurrency(),
            Mastery = new Mastery() {
                Alchemy = other.Mastery.Alchemy,
                Cooking = other.Mastery.Cooking,
                Farming = other.Mastery.Farming,
                Fishing = other.Mastery.Fishing,
                Foraging = other.Mastery.Foraging,
                Handicrafts = other.Mastery.Handicrafts,
                Smithing = other.Mastery.Smithing,
                Instrument = other.Mastery.Instrument,
                Mining = other.Mastery.Mining,
                PetTaming = other.Mastery.PetTaming,
                Ranching = other.Mastery.Ranching,
            },
            DeleteTime = other.DeleteTime.FromEpochSeconds(),
            Channel = other.Channel,
            ReturnChannel = other.ReturnChannel,
            MentorRole = other.MentorRole,
        };
    }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator Maple2.Model.Game.Character?(Character? other) {
        return other == null ? null : new Maple2.Model.Game.Character {
            LastModified = other.LastModified,
            LastOnlineTime = other.LastModified.ToEpochSeconds(),
            AccountId = other.AccountId,
            Id = other.Id,
            Name = other.Name,
            CreationTime = other.CreationTime.ToEpochSeconds(),
            Gender = other.Gender,
            Job = other.Job,
            Level = other.Level,
            SkinColor = other.SkinColor,
            Exp = other.Experience.Exp,
            RestExp = other.Experience.RestExp,
            MapId = other.MapId,
            ReturnMaps = new LimitedStack<int>(3, other.ReturnMapId),
            Mastery = other.Mastery,
            Motto = other.Profile.Motto,
            Picture = other.Profile.Picture,
            Title = other.Profile.Title,
            Insignia = other.Profile.Insignia,
            DoctorCooldown = other.Cooldown.Doctor,
            StorageCooldown = other.Cooldown.Storage,
            DeleteTime = other.DeleteTime.ToEpochSeconds(),
            Channel = other.Channel,
            ReturnChannel = other.ReturnChannel,
            MentorRole = other.MentorRole,
        };
    }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator CharacterInfo?(Character? other) {
        return other == null ? null : new CharacterInfo(other.AccountId, other.Id, other.Name, other.Profile.Motto, other.Profile.Picture, other.Gender, other.Job, other.Level) {
            MapId = other.MapId,
            Channel = other.Channel,
        };
    }

    public static void Configure(EntityTypeBuilder<Character> builder) {
        builder.ToTable("character");
        builder.HasKey(character => character.Id);
        builder.HasOne<Account>()
            .WithMany(account => account.Characters)
            .HasForeignKey(character => character.AccountId);
        builder.HasIndex(character => character.Name).IsUnique();
        builder.Property(character => character.Level)
            .HasDefaultValue(1);
        builder.Property(character => character.SkinColor).HasJsonConversion().IsRequired();
        builder.Property(character => character.Experience).HasJsonConversion().IsRequired();
        builder.Property(character => character.Profile).HasJsonConversion().IsRequired();
        builder.Property(character => character.Cooldown).HasJsonConversion().IsRequired();
        builder.Property(character => character.Currency).HasJsonConversion().IsRequired();
        builder.Property(character => character.Mastery).HasJsonConversion().IsRequired();

        builder.Property(character => character.LastModified).IsRowVersion();
        IMutableProperty creationTime = builder.Property(character => character.CreationTime)
            .ValueGeneratedOnAdd().Metadata;
        creationTime.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }
}

internal class Experience {
    public long Exp { get; set; }
    public long RestExp { get; set; }
}

internal class Profile {
    public required string Motto { get; set; }
    public required string Picture { get; set; }
    public int Title { get; set; }
    public short Insignia { get; set; }
}

internal class Cooldown {
    public long Storage { get; set; }
    public long Doctor { get; set; }
}

internal class CharacterCurrency {
    public long Meso { get; set; }
    public long EventMeret { get; set; }
    public long ValorToken { get; set; }
    public long Treva { get; set; }
    public long Rue { get; set; }
    public long HaviFruit { get; set; }
    public long ReverseCoin { get; set; }
    public long MentorToken { get; set; }
    public long MenteeToken { get; set; }
    public long StarPoint { get; set; }
}
