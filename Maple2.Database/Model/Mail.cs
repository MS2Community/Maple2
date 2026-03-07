using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

internal class Mail {
    public long ReceiverId { get; set; } // Can be either AccountId or CharacterId
    public long Id { get; set; }
    public MailType Type { get; set; }
    public long SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string WeddingInvite { get; set; } = string.Empty;

    // List is used here to preserve order
    public IList<string> TitleArgs { get; set; } = [];
    public IList<string> ContentArgs { get; set; } = [];

    public required MailCurrency Currency { get; set; }

    public DateTime ReadTime { get; set; }
    public DateTime ExpiryTime { get; set; }
    public DateTime SendTime { get; set; }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator Mail?(Maple2.Model.Game.Mail? other) {
        return other == null ? null : new Mail {
            ReceiverId = other.ReceiverId,
            Id = other.Id,
            SenderId = other.SenderId,
            Type = other.Type,
            SenderName = other.SenderName,
            Title = other.Title,
            Content = other.Content,
            TitleArgs = other.TitleArgs.Select(entry => $"{entry.Key}={entry.Value}").ToArray(),
            ContentArgs = other.ContentArgs.Select(entry => $"{entry.Key}={entry.Value}").ToArray(),
            WeddingInvite = other.WeddingInvite,
            Currency = new MailCurrency {
                Meso = other.Meso,
                MesoCollectTime = other.MesoCollectTime,
                Meret = other.Meret,
                MeretCollectTime = other.MeretCollectTime,
                GameMeret = other.GameMeret,
                GameMeretCollectTime = other.GameMeretCollectTime,
            },
            ReadTime = other.ReadTime.FromEpochSeconds(),
            ExpiryTime = other.ExpiryTime.FromEpochSeconds(),
        };
    }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator Maple2.Model.Game.Mail?(Mail? other) {
        return other == null ? null : new Maple2.Model.Game.Mail() {
            ReceiverId = other.ReceiverId,
            Id = other.Id,
            SenderId = other.SenderId,
            Type = other.Type,
            SenderName = other.SenderName,
            Title = other.Title,
            Content = other.Content,
            TitleArgs = other.TitleArgs.Select(arg => {
                string[] split = arg.Split("=", 2);
                return split.Length > 1 ? (split[0], split[1]) : ("key", split[0]);
            }).ToArray(),
            ContentArgs = other.ContentArgs.Select(arg => {
                string[] split = arg.Split("=", 2);
                return split.Length > 1 ? (split[0], split[1]) : ("key", split[0]);
            }).ToArray(),
            WeddingInvite = other.WeddingInvite,
            Meso = other.Currency.Meso,
            MesoCollectTime = other.Currency.MesoCollectTime,
            Meret = other.Currency.Meret,
            MeretCollectTime = other.Currency.MeretCollectTime,
            GameMeret = other.Currency.GameMeret,
            GameMeretCollectTime = other.Currency.GameMeretCollectTime,
            ReadTime = other.ReadTime.ToEpochSeconds(),
            ExpiryTime = other.ExpiryTime.ToEpochSeconds(),
            SendTime = other.SendTime.ToEpochSeconds(),
        };
    }

    public static void Configure(EntityTypeBuilder<Mail> builder) {
        builder.ToTable("mail");
        builder.HasKey(mail => new { mail.ReceiverId, mail.Id });

        builder.Property(mail => mail.Id).ValueGeneratedOnAdd();
        builder.Property(mail => mail.ReceiverId).IsRequired();
        builder.Property(mail => mail.TitleArgs).HasJsonConversion().IsRequired();
        builder.Property(mail => mail.ContentArgs).HasJsonConversion().IsRequired();
        builder.Property(mail => mail.Currency).HasJsonConversion();

        IMutableProperty sendTime = builder.Property(mail => mail.SendTime)
            .ValueGeneratedOnAdd().Metadata;
        sendTime.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }
}

internal class MailCurrency {
    public long Meso { get; set; }
    public long MesoCollectTime { get; set; }
    public long Meret { get; set; }
    public long MeretCollectTime { get; set; }
    public long GameMeret { get; set; }
    public long GameMeretCollectTime { get; set; }
}
