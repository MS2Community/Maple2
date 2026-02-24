using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using static Maple2.Model.Error.CharacterCreateError;

namespace Maple2.Server.Game.PacketHandlers;

public class CheckCharacterNameHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.CheckCharName;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required GameStorage GameStorage { private get; init; }
    public required BanWordStorage BanWordStorage { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        string characterName = packet.ReadUnicodeString();
        long itemUid = packet.ReadLong();

        if (string.IsNullOrWhiteSpace(characterName)) {
            session.Send(CharacterListPacket.CreateError(s_char_err_name));
            return;
        }

        if (characterName.Length < Constant.CharacterNameLengthMin) {
            session.Send(CharacterListPacket.CreateError(s_char_err_name));
            return;
        }
        if (characterName.Length > Constant.CharacterNameLengthMax) {
            session.Send(CharacterListPacket.CreateError(s_char_err_system));
            return;
        }

        if (BanWordStorage.ContainsBannedWord(characterName)) {
            session.Send(CharacterListPacket.CreateError(s_char_err_ban_any));
            return;
        }

        // Validate character name
        if (!NameValidator.ValidName(characterName)) {
            session.Send(CharacterListPacket.CreateError(s_char_err_ban_all));
            return;
        }

        using GameStorage.Request db = GameStorage.Context();
        long existingId = db.GetCharacterId(characterName);
        session.Send(CheckCharacterNamePacket.Result(existingId != 0, characterName, itemUid));
    }
}
