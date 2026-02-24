using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

internal class UgcMarketItem {
    public long Id { get; set; }
    public int ItemId { get; set; }
    public long Price { get; set; }
    public int SalesCount { get; set; }
    public int TabId { get; set; }
    public UgcMarketListingStatus Status { get; set; }
    public DateTime ListingEndTime { get; set; }
    public DateTime PromotionEndTime { get; set; }
    public long AccountId { get; set; }
    public long CharacterId { get; set; }
    public required string CharacterName { get; set; }
    public required string Description { get; set; }
    public string[] Tags { get; set; } = [];
    public required UgcItemLook Look { get; set; }
    public required ItemBlueprint Blueprint { get; set; }
    public DateTime CreationTime { get; set; }


    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator UgcMarketItem?(Maple2.Model.Game.UgcMarketItem? other) {
        return other == null ? null : new UgcMarketItem {
            Id = other.Id,
            ItemId = other.ItemMetadata.Id,
            Price = other.Price,
            SalesCount = other.SalesCount,
            TabId = other.TabId,
            Status = other.Status,
            ListingEndTime = other.ListingEndTime.FromEpochSeconds(),
            PromotionEndTime = other.PromotionEndTime.FromEpochSeconds(),
            AccountId = other.SellerAccountId,
            CharacterId = other.SellerCharacterId,
            CharacterName = other.SellerCharacterName,
            Description = other.Description,
            Tags = other.Tags,
            Look = other.Look,
            Blueprint = other.Blueprint,
            CreationTime = other.CreationTime.FromEpochSeconds(),
        };
    }

    public Maple2.Model.Game.UgcMarketItem Convert(ItemMetadata metadata) {
        var entry = new Maple2.Model.Game.UgcMarketItem(metadata) {
            Id = Id,
            Price = Price,
            SalesCount = SalesCount,
            TabId = TabId,
            Status = Status,
            ListingEndTime = ListingEndTime.ToEpochSeconds(),
            PromotionEndTime = PromotionEndTime.ToEpochSeconds(),
            SellerAccountId = AccountId,
            SellerCharacterId = CharacterId,
            SellerCharacterName = CharacterName,
            CreationTime = CreationTime.ToEpochSeconds(),
            Description = Description,
            Tags = Tags,
            Look = Look,
            Blueprint = Blueprint,
        };

        return entry;
    }

    public static void Configure(EntityTypeBuilder<UgcMarketItem> builder) {
        builder.ToTable("ugc-market-item");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Look).HasJsonConversion();
        builder.Property(entry => entry.Blueprint).HasJsonConversion();
        builder.Property(entry => entry.Tags).HasJsonConversion();
        IMutableProperty creationTime = builder.Property(listing => listing.CreationTime)
            .ValueGeneratedOnAdd().Metadata;
        creationTime.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }
}
