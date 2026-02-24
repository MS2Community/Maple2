using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

internal class SystemBanner {
    public int Id { get; set; }
    public required string Name { get; set; }
    public SystemBannerType Type { get; set; }
    public SystemBannerFunction Function { get; set; }
    public required string FunctionParameter { get; set; }
    public required string Url { get; set; }
    public SystemBannerLanguage Language { get; set; }
    public DateTime BeginTime { get; set; }
    public DateTime EndTime { get; set; }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator Maple2.Model.Game.SystemBanner?(SystemBanner? other) {
        return other == null ? null : new Maple2.Model.Game.SystemBanner(other.Id) {
            Name = other.Name,
            Type = other.Type,
            Function = other.Function,
            FunctionParameter = other.FunctionParameter,
            Url = other.Url,
            Language = other.Language,
            BeginTime = other.BeginTime.ToEpochSeconds(),
            EndTime = other.EndTime.ToEpochSeconds(),
        };
    }

    public static void Configure(EntityTypeBuilder<SystemBanner> builder) {
        builder.ToTable("system-banner");
        builder.HasKey(banner => banner.Id);
    }
}
