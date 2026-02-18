using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

internal class SoldUgcMarketItem {
    public long Id { get; set; }
    public long Price { get; set; }
    public long Profit { get; set; }
    public required string Name { get; set; }
    public DateTime SoldTime { get; set; }
    public long AccountId { get; set; }


    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator SoldUgcMarketItem?(Maple2.Model.Game.SoldUgcMarketItem? other) {
        return other == null ? null : new SoldUgcMarketItem {
            Id = other.Id,
            Price = other.Price,
            Profit = other.Profit,
            Name = other.Name,
            SoldTime = other.SoldTime.FromEpochSeconds(),
            AccountId = other.AccountId,
        };
    }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator Maple2.Model.Game.SoldUgcMarketItem?(SoldUgcMarketItem? other) {
        return other == null ? null : new Maple2.Model.Game.SoldUgcMarketItem {
            Id = other.Id,
            Price = other.Price,
            Profit = other.Profit,
            Name = other.Name,
            SoldTime = other.SoldTime.ToEpochSeconds(),
            AccountId = other.AccountId,
        };
    }

    public static void Configure(EntityTypeBuilder<SoldUgcMarketItem> builder) {
        builder.ToTable("ugc-market-item-sold");
        builder.HasKey(entry => entry.Id);
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(listing => listing.AccountId)
            .IsRequired();
        IMutableProperty creationTime = builder.Property(listing => listing.SoldTime)
            .ValueGeneratedOnAdd().Metadata;
        creationTime.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }
}
