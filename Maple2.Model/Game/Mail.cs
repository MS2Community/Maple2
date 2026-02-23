using System.Text;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Model.Game;

public class Mail : IByteSerializable {
    public long Id { get; init; }
    public long SenderId { get; init; }
    public long ReceiverId { get; init; } // Can be either AccountId or CharacterId

    public MailType Type;

    public string SenderName = string.Empty;
    public string Title = string.Empty;
    public string Content = string.Empty;
    public string WeddingInvite = string.Empty;
    public IList<(string Key, string Value)> TitleArgs { get; init; }
    public IList<(string Key, string Value)> ContentArgs { get; init; }

    public long Meso;
    public long MesoCollectTime;
    public long Meret;
    public long MeretCollectTime;
    public long GameMeret;
    public long GameMeretCollectTime;

    public long ReadTime;
    public long ExpiryTime;
    public long SendTime;

    // More than 1 item may not display properly
    public readonly IList<Item> Items;

    public Mail(int mailExpiryDays) {
        TitleArgs = new List<(string Key, string Value)>();
        ContentArgs = new List<(string Key, string Value)>();
        Items = new List<Item>();
        ExpiryTime = DateTimeOffset.UtcNow.AddDays(mailExpiryDays).ToUnixTimeSeconds();
    }

    public void Update(Mail other) {
        if (Id != other.Id || SenderId != other.SenderId || ReceiverId != other.ReceiverId) {
            throw new ArgumentException("Updating mail with a different mail");
        }

        Meso = other.Meso;
        MesoCollectTime = other.MesoCollectTime;
        Meret = other.Meret;
        MeretCollectTime = other.MeretCollectTime;
        GameMeret = other.GameMeret;
        GameMeretCollectTime = other.GameMeretCollectTime;
        ReadTime = other.ReadTime;
        ExpiryTime = other.ExpiryTime;
        SendTime = other.SendTime;
    }

    public void SetSenderName(StringCode name) {
        SenderName = $"""<ms2><v key="{name.ToString()}" /></ms2>""";
    }

    public void SetTitle(StringCode title) {
        Title = $"""<ms2><v key="{title.ToString()}" /></ms2>""";
    }

    public void SetContent(StringCode content) {
        Content = $"""<ms2><v key="{content.ToString()}" /></ms2>""";
    }

    public void SetWeddingInvite(WeddingHall hall) {
        WeddingInvite = $"""<ms2><wedding groom="{hall.ReserverName}" bride="{hall.PartnerName}" date="{hall.CeremonyTime}" package="{hall.PackageId}" hall="{hall.PackageHallId}"/></ms2>""";
    }

    public bool MesoCollected() {
        return Meso == 0 || MesoCollectTime > 0;
    }

    public bool MeretCollected() {
        return Meret == 0 || MeretCollectTime > 0;
    }

    public bool GameMeretCollected() {
        return GameMeret == 0 || GameMeretCollectTime > 0;
    }

    public void WriteTo(IByteWriter writer) {
        writer.Write<MailType>(Type);
        writer.WriteLong(Id);
        writer.WriteLong(SenderId);
        writer.WriteUnicodeString(SenderName);
        writer.WriteUnicodeString(Title);
        writer.WriteUnicodeString(Content);
        writer.WriteUnicodeString(FormatArgs(TitleArgs));
        writer.WriteUnicodeString(FormatArgs(ContentArgs));

        if (Type == MailType.Ad) { // MailAdItem
            byte count = 0;
            writer.WriteByte(count);
            for (byte i = 0; i < count; i++) {
                writer.WriteInt();
                writer.WriteInt();
                writer.WriteInt();
                writer.WriteInt();
                writer.WriteLong();
                writer.WriteLong();
                writer.WriteLong();
            }

            writer.WriteUnicodeString();
            writer.WriteLong();
            writer.WriteByte();
        } else {
            writer.WriteByte((byte) Items.Count);
            byte index = 0;
            foreach (Item item in Items) {
                writer.WriteInt(item.Id);
                writer.WriteLong(item.Uid);
                writer.WriteByte(index);
                writer.WriteInt(item.Rarity);
                writer.WriteInt(item.Amount);
                // Item Collect Time, this is unused because we directly set owner after collection.
                writer.WriteLong();
                writer.WriteInt();
                writer.WriteLong();
                writer.WriteClass<Item>(item);

                index++;
            }
        }

        writer.WriteLong(Meso);
        writer.WriteLong(MesoCollectTime);
        writer.WriteLong(Meret);
        writer.WriteLong(MeretCollectTime);
        writer.WriteLong(GameMeret);
        writer.WriteLong(GameMeretCollectTime);

        writer.WriteByte();
        // sub_45E8C0
        // count2 = add_byte("Count")
        // for j in range(count2):
        //     add_byte("Unknown")
        //     add_byte("Unknown")
        //     add_long("Unknown")
        //     add_long("Unknown")

        writer.WriteLong(ReadTime);
        writer.WriteLong(ExpiryTime);
        writer.WriteLong(SendTime);
        writer.WriteUnicodeString(WeddingInvite);
    }

    private static string FormatArgs(ICollection<(string Key, string Value)> args) {
        if (args.Count == 0) {
            return string.Empty;
        }

        var result = new StringBuilder();
        result.Append("<ms2>");
        foreach ((string key, string value) in args) {
            result.Append($"""<v {(key.Length > 0 ? key : "key")}="{value}" />""");
        }
        result.Append("</ms2>");

        return result.ToString();
    }
}
