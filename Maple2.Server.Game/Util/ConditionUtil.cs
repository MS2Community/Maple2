using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Util;

public static class ConditionUtil {
    public static bool Check(this ConditionMetadata condition, GameSession session, string targetString = "", long targetLong = 0, string codeString = "", long codeLong = 0) {
        if (condition.PartyCount > 0) {
            if (session.Party.Party == null || session.Party.Party.Members.Count < condition.PartyCount) {
                return false;
            }
        }

        if (condition.GuildPartyCount > 0) {
            if (session.Party.Party == null || session.Party.GuildMemberCount() < condition.GuildPartyCount) {
                return false;
            }
        }
        bool code = condition.Codes == null || condition.Codes.CheckCode(session, condition.Type, codeString, codeLong);
        bool target = condition.Target == null || condition.Target.CheckTarget(session, condition.Type, targetString, targetLong);
        return target && code;
    }

    private static bool CheckCode(this ConditionMetadata.Parameters code, GameSession session, ConditionType conditionType, string stringValue = "", long longValue = 0) {
        switch (conditionType) {
            case ConditionType.emotiontime:
            case ConditionType.emotion:
            case ConditionType.trigger:
            case ConditionType.npc_race:
                if (code.Strings != null && code.Strings.Contains(stringValue)) {
                    return true;
                }
                break;
            case ConditionType.trophy_point:
                if (code.Range != null && InRange((ConditionMetadata.Range<int>) code.Range, (int) longValue)) {
                    return true;
                }
                break;
            case ConditionType.interact_object:
            case ConditionType.interact_object_rep:
                if ((code.Range != null && InRange((ConditionMetadata.Range<int>) code.Range, (int) longValue)) ||
                    (code.Integers != null && code.Integers.Contains((int) longValue))) {
                    return true;
                }
                break;
            case ConditionType.item_collect:
            case ConditionType.item_collect_revise:
                if ((code.Range != null && InRange((ConditionMetadata.Range<int>) code.Range, (int) longValue)) ||
                    (code.Integers != null && code.Integers.Contains((int) longValue))) {
                    if (session.Player.Value.Unlock.CollectedItems.ContainsKey((int) longValue)) {
                        session.Player.Value.Unlock.CollectedItems[(int) longValue]++;
                        return false;
                    }

                    session.Player.Value.Unlock.CollectedItems.Add((int) longValue, 1);
                    return true;
                }
                break;
            case ConditionType.map:
            case ConditionType.fish:
            case ConditionType.fisher_grade:
            case ConditionType.mastery_grade:
            case ConditionType.set_mastery_grade:
            case ConditionType.item_add:
            case ConditionType.item_destroy:
            case ConditionType.beauty_add:
            case ConditionType.beauty_change_color:
            case ConditionType.beauty_random:
            case ConditionType.beauty_style_add:
            case ConditionType.beauty_style_apply:
            case ConditionType.level:
            case ConditionType.level_up:
            case ConditionType.item_exist:
            case ConditionType.item_pickup:
            case ConditionType.quest:
            case ConditionType.mastery_gathering:
            case ConditionType.mastery_gathering_try:
            case ConditionType.mastery_harvest:
            case ConditionType.mastery_harvest_try:
            case ConditionType.mastery_farming_try:
            case ConditionType.mastery_farming:
            case ConditionType.mastery_harvest_otherhouse:
            case ConditionType.mastery_manufacturing:
            case ConditionType.openStoryBook:
            case ConditionType.quest_accept:
            case ConditionType.quest_clear_by_chapter:
            case ConditionType.quest_clear:
            case ConditionType.dungeon_clear:
            case ConditionType.buff:
            case ConditionType.enchant_result:
            case ConditionType.dialogue:
            case ConditionType.talk_in:
            case ConditionType.npc:
            case ConditionType.skill:
            case ConditionType.job:
            case ConditionType.job_change:
            case ConditionType.item_move:
            case ConditionType.install_item:
            case ConditionType.rotate_cube:
            case ConditionType.uninstall_item:
            case ConditionType.fall:
            case ConditionType.swim:
            case ConditionType.swimtime:
            case ConditionType.run:
            case ConditionType.crawl:
            case ConditionType.glide:
            case ConditionType.climb:
            case ConditionType.ropetime:
            case ConditionType.laddertime:
            case ConditionType.holdtime:
            case ConditionType.riding:
            case ConditionType.fish_big:
            case ConditionType.music_play_instrument_mastery:
            case ConditionType.music_play_instrument_time:
            case ConditionType.music_play_ensemble_in:
            case ConditionType.music_play_score:
            case ConditionType.explore_continent:
            case ConditionType.continent:
            case ConditionType.explore:
            case ConditionType.revise_achieve_multi_grade:
            case ConditionType.revise_achieve_single_grade:
            case ConditionType.hero_achieve_grade:
            case ConditionType.stay_map:
            case ConditionType.stay_cube:
                if (code.Range != null && InRange((ConditionMetadata.Range<int>) code.Range, (int) longValue)) {
                    return true;
                }

                if (code.Integers != null && code.Integers.Contains((int) longValue)) {
                    return true;
                }
                break;
            case ConditionType.fish_collect:
            case ConditionType.fish_goldmedal:
                if ((code.Range != null && InRange((ConditionMetadata.Range<int>) code.Range, (int) longValue)) ||
                    (code.Integers != null && code.Integers.Contains((int) longValue))) {
                    return !session.Player.Value.Unlock.FishAlbum.ContainsKey((int) longValue);
                }
                break;
            case ConditionType.jump:
            case ConditionType.meso:
            case ConditionType.taxifind:
            case ConditionType.fall_damage:
            case ConditionType.gemstone_upgrade:
            case ConditionType.gemstone_upgrade_success:
            case ConditionType.gemstone_upgrade_try:
            case ConditionType.socket_unlock_success:
            case ConditionType.socket_unlock_try:
            case ConditionType.socket_unlock:
            case ConditionType.gemstone_puton:
            case ConditionType.gemstone_putoff:
            case ConditionType.fish_fail:
            case ConditionType.music_play_grade:
            case ConditionType.music_play_ensemble:
            case ConditionType.breakable_object:
            case ConditionType.change_profile:
            case ConditionType.install_billboard:
            case ConditionType.buddy_request:
            case ConditionType.fall_survive:
            case ConditionType.fall_die:
            case ConditionType.buy_house:
            case ConditionType.extend_house:
            case ConditionType.exp:
            case ConditionType.item_merge_success:
            case ConditionType.wedding_propose:
            case ConditionType.wedding_propose_decline:
            case ConditionType.wedding_propose_declined:
            case ConditionType.wedding_hall_cancel:
            case ConditionType.item_gear_score:
            case ConditionType.revival:
            case ConditionType.home_doctor:
            case ConditionType.resolve_panelty:
            case ConditionType.hit_tombstone:
            case ConditionType.taxiuse:
            case ConditionType.taxifee:
            case ConditionType.chat:
            case ConditionType.send_mail:
            case ConditionType.change_ugc_equip:
            case ConditionType.unlimited_enchant:
                return true;
        }
        return false;
    }

    private static bool CheckTarget(this ConditionMetadata.Parameters target, GameSession session, ConditionType conditionType, string stringValue = "", long longValue = 0) {
        switch (conditionType) {
            case ConditionType.stay_cube:
                if (target.Strings != null && target.Strings.Contains(stringValue)) {
                    return true;
                }
                break;
            case ConditionType.emotiontime:
            case ConditionType.emotion:
                if (target.Range != null && target.Range.Value.Min >= session.Player.Value.Character.MapId &&
                    target.Range.Value.Max <= session.Player.Value.Character.MapId) {
                    return true;
                }

                if (target.Integers != null && target.Integers.Any(i => i == session.Player.Value.Character.MapId)) {
                    return true;
                }

                break;
            case ConditionType.fish:
            case ConditionType.fish_big:
            case ConditionType.fall_damage:
                if (target.Range != null && target.Range.Value.Min >= longValue &&
                    target.Range.Value.Max <= longValue) {
                    return true;
                }

                if (target.Integers != null && target.Integers.Any(value => longValue >= value)) {
                    return true;
                }
                break;
            case ConditionType.gemstone_upgrade:
            case ConditionType.socket_unlock:
            case ConditionType.level_up:
            case ConditionType.level:
            case ConditionType.enchant_result:
            case ConditionType.install_billboard:
            case ConditionType.item_move:
            case ConditionType.npc:
                if (target.Integers != null && target.Integers.Any(value => longValue >= value)) {
                    return true;
                }
                break;
            case ConditionType.swim:
            case ConditionType.swimtime:
            case ConditionType.run:
            case ConditionType.crawl:
            case ConditionType.glide:
            case ConditionType.climb:
            case ConditionType.fall:
            case ConditionType.ropetime:
            case ConditionType.laddertime:
            case ConditionType.holdtime:
            case ConditionType.riding:
            case ConditionType.skill:
            case ConditionType.music_play_instrument_time:
            case ConditionType.music_play_score:
            case ConditionType.music_play_ensemble_in:
            case ConditionType.revise_achieve_multi_grade:
            case ConditionType.revise_achieve_single_grade:
            case ConditionType.chat:
                if (target.Range != null && target.Range.Value.Min >= longValue &&
                    target.Range.Value.Max <= longValue) {
                    return true;
                }

                if (target.Integers != null && target.Integers.Contains((int) longValue)) {
                    return true;
                }
                break;
            case ConditionType.map:
            case ConditionType.jump:
            case ConditionType.meso:
            case ConditionType.taxifind:
            case ConditionType.trophy_point:
            case ConditionType.interact_object:
            case ConditionType.gemstone_upgrade_success:
            case ConditionType.gemstone_upgrade_try:
            case ConditionType.socket_unlock_success:
            case ConditionType.socket_unlock_try:
            case ConditionType.gemstone_puton:
            case ConditionType.gemstone_putoff:
            case ConditionType.fish_fail:
            case ConditionType.fish_collect:
            case ConditionType.fish_goldmedal:
            case ConditionType.fisher_grade:
            case ConditionType.mastery_grade:
            case ConditionType.set_mastery_grade:
            case ConditionType.music_play_grade:
            case ConditionType.music_play_ensemble:
            case ConditionType.music_play_instrument_mastery:
            case ConditionType.item_add:
            case ConditionType.item_pickup:
            case ConditionType.item_destroy:
            case ConditionType.beauty_add:
            case ConditionType.beauty_change_color:
            case ConditionType.beauty_random:
            case ConditionType.beauty_style_add:
            case ConditionType.beauty_style_apply:
            case ConditionType.trigger:
            case ConditionType.mastery_gathering:
            case ConditionType.mastery_gathering_try:
            case ConditionType.mastery_harvest:
            case ConditionType.mastery_harvest_try:
            case ConditionType.mastery_farming_try:
            case ConditionType.mastery_farming:
            case ConditionType.mastery_harvest_otherhouse:
            case ConditionType.mastery_manufacturing:
            case ConditionType.quest_accept:
            case ConditionType.quest_clear_by_chapter:
            case ConditionType.quest_clear:
            case ConditionType.buff:
            case ConditionType.dialogue:
            case ConditionType.talk_in:
            case ConditionType.change_profile:
            case ConditionType.buddy_request:
            case ConditionType.job:
            case ConditionType.job_change:
            case ConditionType.fall_survive:
            case ConditionType.fall_die:
            case ConditionType.install_item:
            case ConditionType.rotate_cube:
            case ConditionType.uninstall_item:
            case ConditionType.buy_house:
            case ConditionType.extend_house:
            case ConditionType.exp:
            case ConditionType.item_merge_success:
            case ConditionType.wedding_propose:
            case ConditionType.wedding_propose_decline:
            case ConditionType.wedding_propose_declined:
            case ConditionType.wedding_hall_cancel:
            case ConditionType.explore_continent:
            case ConditionType.continent:
            case ConditionType.explore:
            case ConditionType.item_gear_score:
            case ConditionType.revival:
            case ConditionType.home_doctor:
            case ConditionType.resolve_panelty:
            case ConditionType.hit_tombstone:
            case ConditionType.hero_achieve_grade:
            case ConditionType.taxiuse:
            case ConditionType.taxifee:
            case ConditionType.send_mail:
            case ConditionType.change_ugc_equip:
            case ConditionType.unlimited_enchant:
                return true;
        }
        return false;
    }

    private static bool InRange(ConditionMetadata.Range<int> range, int value) {
        return value >= range.Min && value <= range.Max;
    }
}
