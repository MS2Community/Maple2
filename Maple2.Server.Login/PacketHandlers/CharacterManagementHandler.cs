using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.Session;
using Maple2.Server.World.Service;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using static Maple2.Model.Enum.EquipSlot;
using static Maple2.Model.Error.CharacterCreateError;
using static Maple2.Model.Error.CharacterDeleteError;
using static Maple2.Model.Error.MigrationError;
using Enum = System.Enum;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Login.PacketHandlers;

public class CharacterManagementHandler : PacketHandler<LoginSession> {
    public override RecvOp OpCode => RecvOp.CharacterManagement;

    private enum Command : byte {
        Select = 0,
        Create = 1,
        Delete = 2,
        CancelDelete = 3,
        ConfirmDelete = 4,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }
    public required GameStorage GameStorage { private get; init; }
    public required BanWordStorage BanWordStorage { private get; init; }
    public required ItemMetadataStorage ItemMetadata { private get; init; }
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(LoginSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Select:
                HandleSelect(session, packet);
                break;
            case Command.Create:
                HandleCreate(session, packet);
                break;
            case Command.Delete:
                HandleDelete(session, packet);
                break;
            case Command.CancelDelete:
                HandleCancelDelete(session, packet);
                break;
            case Command.ConfirmDelete:
                HandleDelete(session, packet);
                break;
            default:
                throw new ArgumentException($"Invalid CHARACTER_MANAGEMENT type {command}");
        }
    }

    private void HandleSelect(LoginSession session, IByteReader packet) {
        long characterId = packet.ReadLong();
        packet.ReadShort(); // 01 00, world? channel?

        try {
            using GameStorage.Request db = GameStorage.Context();
            Character? character = db.GetCharacter(characterId, session.AccountId);
            if (character == null) {
                session.Send(MigrationPacket.LoginToGameError(s_move_err_default, "Invalid character"));
                return;
            }

            var request = new MigrateOutRequest {
                AccountId = session.AccountId,
                CharacterId = characterId,
                MachineId = session.MachineId.ToString(),
                Server = Server.World.Service.Server.Game,
            };

            Logger.Information("Logging in to game as {Request}", request);

            MigrateOutResponse response = World.MigrateOut(request);
            var endpoint = new IPEndPoint(IPAddress.Parse(response.IpAddress), response.Port);
            session.Send(MigrationPacket.LoginToGame(endpoint, response.Token, character.MapId));
            session.Disconnect();
        } catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable) {
            session.Send(MigrationPacket.LoginToGameError(s_move_err_no_server, ex.Message));
        } catch (RpcException ex) when (ex.StatusCode == StatusCode.ResourceExhausted) {
            session.Send(MigrationPacket.LoginToGameError(s_move_err_member_limit, ex.Message));
        } catch (RpcException ex) {
            session.Send(MigrationPacket.LoginToGameError(s_move_err_default, ex.Message));
            session.Disconnect();
        } catch (Exception ex) {
            session.Send(MigrationPacket.LoginToGameError(s_move_err_default, ex.Message));
            session.Disconnect();
        }
    }

    private void HandleCreate(LoginSession session, IByteReader packet) {
        var gender = packet.Read<Gender>();
        var jobCode = packet.Read<JobCode>();
        var job = (Job) ((int) jobCode * 10);
        string name = packet.ReadUnicodeString();

        if (string.IsNullOrWhiteSpace(name)) {
            session.Send(CharacterListPacket.CreateError(s_char_err_name));
            return;
        }

        if (name.Length < Constant.CharacterNameLengthMin) {
            session.Send(CharacterListPacket.CreateError(s_char_err_name));
            return;
        }
        if (name.Length > Constant.CharacterNameLengthMax) {
            session.Send(CharacterListPacket.CreateError(s_char_err_system));
            return;
        }

        if (BanWordStorage.ContainsBannedWord(name)) {
            session.Send(CharacterListPacket.CreateError(s_char_err_ban_any));
            return;
        }

        // Validate character name
        if (!NameValidator.ValidName(name)) {
            session.Send(CharacterListPacket.CreateError(s_char_err_ban_all));
            return;
        }

        using GameStorage.Request db = GameStorage.Context();
        long existingId = db.GetCharacterId(name);
        if (existingId != 0) {
            session.Send(CharacterListPacket.CreateError(s_char_err_already_taken));
            return;
        }

        var skinColor = packet.Read<SkinColor>();
        packet.Skip(2); // Unknown

        var outfits = new List<Item>();
        int equipCount = packet.ReadByte();
        for (int i = 0; i < equipCount; i++) {
            int id = packet.ReadInt();
            byte slotByte = packet.ReadByte();
            EquipSlot slot = (EquipSlot) slotByte;
            if (slot is SK or OH or Unknown) {
                session.Send(CharacterListPacket.CreateError(s_char_err_invalid_def_item));
                return;
            }
            if (!ValidateDefaultItems(jobCode, id, slot)) {
                session.Send(CharacterListPacket.CreateError(s_char_err_invalid_def_item));
                return;
            }
            if (!ItemMetadata.TryGet(id, out ItemMetadata? metadata)) {
                session.Send(CharacterListPacket.CreateError(s_char_err_invalid_def_item));
                return;
            }
            if (metadata.Limit.Gender != Gender.All && metadata.Limit.Gender != gender) {
                session.Send(CharacterListPacket.CreateError(s_char_err_invalid_def_item));
                return;
            }

            var outfit = new Item(metadata) {
                Group = ItemGroup.Outfit,
                Slot = (short) slot,
            };
            Debug.Assert(outfit.Appearance != null, "equip.Appearance == null");
            outfit.Appearance.ReadFrom(packet);
            outfits.Add(outfit);
        }

        packet.Skip(4); // Unknown

        JobTable.Entry entry = TableMetadata.JobTable.Entries[jobCode];
        var character = new Character {
            AccountId = session.AccountId,
            Gender = gender,
            Job = job,
            Name = name,
            SkinColor = skinColor,
            MapId = entry.Tutorial.StartField,
            ReturnMaps = new LimitedStack<int>(3, entry.Tutorial.StartField),
            Mastery = new Mastery(),
        };

        session.CreateCharacter(character, outfits);
    }

    private void HandleDelete(LoginSession session, IByteReader packet) {
        long characterId = packet.ReadLong();

        using GameStorage.Request db = GameStorage.Context();
        Character? character = db.GetCharacter(characterId, session.AccountId);
        if (character == null || !ValidateDeleteRequest(session, characterId, db, character)) {
            return;
        }

        // Delete already pending
        if (character.DeleteTime != default) {
            if (character.DeleteTime <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
                DeleteCharacter(session, db, characterId);
            } else {
                session.Send(CharacterListPacket.BeginDelete(characterId, character.DeleteTime,
                    s_char_err_next_delete_char_date));
            }
            return;
        }

        if (character.Level >= Constant.CharacterDestroyDivisionLevel) {
            character.DeleteTime = DateTimeOffset.UtcNow.AddSeconds(Constant.CharacterDestroyWaitSecond).ToUnixTimeSeconds();
            if (db.UpdateDelete(session.AccountId, characterId, character.DeleteTime)) {
                session.Send(CharacterListPacket.BeginDelete(characterId, character.DeleteTime));
            } else {
                session.Send(CharacterListPacket.BeginDelete(characterId, character.DeleteTime, s_char_err_destroy));
            }
        } else {
            DeleteCharacter(session, db, characterId);
        }
    }

    private void HandleCancelDelete(LoginSession session, IByteReader packet) {
        long characterId = packet.ReadLong();

        using GameStorage.Request db = GameStorage.Context();
        Character? character = db.GetCharacter(characterId, session.AccountId);
        if (character == null || !ValidateDeleteRequest(session, characterId, db, character)) {
            return;
        }

        if (character.DeleteTime == default) {
            session.Send(CharacterListPacket.CancelDelete(characterId, s_char_err_no_destroy_wait));
            return;
        }

        // Remove DeleteTime
        character.DeleteTime = 0;
        if (db.UpdateDelete(session.AccountId, characterId, 0)) {
            session.Send(CharacterListPacket.CancelDelete(characterId));
        }
    }

    private bool ValidateDefaultItems(JobCode jobCode, int id, EquipSlot slot) {
        if (TableMetadata.DefaultItemsTable.Common.TryGetValue(slot, out int[]? commonItemIds) && commonItemIds.Contains(id)) {
            return true;
        }
        if (TableMetadata.DefaultItemsTable.Job.TryGetValue(jobCode, slot, out int[]? jobItemIds) && jobItemIds.Contains(id)) {
            return true;
        }

        return false;
    }

    private static bool ValidateDeleteRequest(LoginSession session, long characterId, GameStorage.Request db, Character? character) {
        // This character does not exist or is not owned by session's account.
        if (character == null) {
            session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_already_destroy));
            return false;
        }

        if (db.GetBlackMarketListings(characterId).Any()) {
            session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_black_market_count));
            return false;
        }

        if (db.GetMyMesoListingsByCharacterId(characterId).Any()) {
            session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_meso_market_count));
            return false;
        }

        if (db.GetAllMail(characterId).Any(mail => mail.ReadTime == 0)) {
            session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_unread_mail));
            return false;
        }

        if (character.GuildId != 0) {
            if (db.GetGuild(character.GuildId)?.LeaderCharacterId == characterId) {
                session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_guild_master));
                return false;
            }

            session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_guild));
            return false;
        }

        if (db.GetUgcListingsByCharacterId(characterId).Any()) {
            session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_ugc_market));
            return false;
        }

        if (character.MarriageInfo.Status != MaritalStatus.Single) {
            session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_wedding));
            return false;
        }

        return true;
    }

    private static void DeleteCharacter(LoginSession session, GameStorage.Request db, long characterId) {
        if (!db.DeleteCharacter(session.AccountId, characterId)) {
            session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_destroy));
            return;
        }

        session.Send(CharacterListPacket.DeleteEntry(characterId));
    }
}
