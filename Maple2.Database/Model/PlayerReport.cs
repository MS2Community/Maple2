using System.Text.Json.Serialization;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

internal class PlayerReport {
    public long Id { get; set; }
    public long CharacterId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public long ReporterCharacterId { get; set; }
    public string ReporterName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public ReportCategory Category { get; set; }
    public required ReportInfo ReportInfo { get; set; }
    public DateTime CreateTime { get; set; }

    public static void Configure(EntityTypeBuilder<PlayerReport> builder) {
        builder.ToTable("player-reports");
        builder.HasKey(report => report.Id);
        builder.Property(report => report.ReportInfo).HasJsonConversion().IsRequired();

    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "!")]
[JsonDerivedType(typeof(PlayerReportInfo), typeDiscriminator: "player")]
[JsonDerivedType(typeof(HomeReportInfo), typeDiscriminator: "home")]
[JsonDerivedType(typeof(ChatReportInfo), typeDiscriminator: "chat")]
[JsonDerivedType(typeof(PetReportInfo), typeDiscriminator: "pet")]
[JsonDerivedType(typeof(PosterReportInfo), typeDiscriminator: "poster")]
[JsonDerivedType(typeof(ItemReportInfo), typeDiscriminator: "item")]
internal abstract record ReportInfo;

internal record PlayerReportInfo(string Flag) : ReportInfo;
internal record HomeReportInfo(string Flag, long HomeId, int MapId, int PlotId) : ReportInfo;
internal record ChatReportInfo(string Flag, string Message) : ReportInfo;
internal record PetReportInfo(string Flag, string PetName) : ReportInfo;
internal record PosterReportInfo(string Flag, long PosterId, string TemplateId) : ReportInfo;
internal record ItemReportInfo(string Flag, long ListingId) : ReportInfo;
