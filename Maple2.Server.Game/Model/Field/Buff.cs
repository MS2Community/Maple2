using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Model;

public class Buff : IUpdatable, IByteSerializable {
    private FieldManager Field => Owner.Field;
    public readonly AdditionalEffectMetadata Metadata;
    public readonly int ObjectId;
    public long CastUid { get; set; }

    public IActor Caster { get; private set; }
    public IActor Owner { get; private set; }

    public int Id => Metadata.Id;
    public short Level => Metadata.Level;

    public long StartTick { get; private set; }
    public long EndTick { get; private set; }
    public long IntervalTick { get; private set; }
    public long NextProcTick { get; protected set; }
    public int ProcCount { get; private set; }
    public int Stacks { get; private set; }
    public long ShieldHealth { get; set; }

    public bool Enabled { get; private set; }

    private bool activated;
    private readonly bool canExpire;
    private bool canProc;

    private readonly ILogger logger = Log.ForContext<Buff>();

    public Buff(AdditionalEffectMetadata metadata, int objectId, IActor caster, IActor owner, long startTick, long endTick, int stacks) {
        Metadata = metadata;
        ObjectId = objectId;

        Caster = caster;
        Owner = owner;

        StartTick = startTick;
        EndTick = endTick;

        // Buffs with IntervalTick=0 will just proc a single time
        IntervalTick = metadata.Property.IntervalTick > 0 ? metadata.Property.IntervalTick : metadata.Property.DurationTick + 1000;

        Stack(stacks, true);
        NextProcTick = startTick + Metadata.Property.DelayTick + IntervalTick;
        UpdateEnabled(false);
        canProc = metadata.Property.KeepCondition != BuffKeepCondition.UnlimitedDuration;
        canExpire = metadata.Property.KeepCondition != BuffKeepCondition.UnlimitedDuration && EndTick >= startTick;
    }

    public void ResetActor(IActor actor) {
        if (actor.ObjectId == Caster.ObjectId) {
            Caster = actor;
        }
        if (actor.ObjectId == Owner.ObjectId) {
            Owner = actor;
        }
    }

    public bool UpdateEndTime(long endTick) {
        if (endTick == EndTick) {
            return false;
        }
        EndTick = endTick;
        return true;
    }

    public bool Stack(int amount = 1, bool silent = false) {
        if (amount == 0) {
            return false;
        }
        if (amount > 0 && Stacks >= Metadata.Property.MaxCount) {
            return false;
        }
        int currentStacks = Stacks;
        Stacks = Math.Clamp(Stacks + amount, 0, Metadata.Property.MaxCount);
        if (Stacks == currentStacks) {
            return false;
        }

        if (!silent && Stacks >= Metadata.Property.MaxCount) {
            Owner.Buffs.TriggerEvent(Owner, Owner, Owner, EventConditionType.OnBuffStacksReached, buffId: Id);
        }

        return true;
    }

    public void RemoveStack(int amount = 1) {
        Stacks = Math.Max(0, Stacks - amount);
        if (Stacks == 0) {
            Owner.Buffs.Remove(Id, Caster.ObjectId);
        }
    }

    public virtual void Update(long tickCount) {
        if (!activated) {
            if (Metadata.Update.Cancel != null) {
                foreach (int id in Metadata.Update.Cancel.Ids) {
                    if (Metadata.Update.Cancel.CheckSameCaster && Owner.ObjectId != Caster.ObjectId) {
                        continue;
                    }

                    Owner.Buffs.Remove(id, Caster.ObjectId);
                }
            }

            activated = true;
        }

        if (Metadata.Property.ClearOnDistanceFromCaster > 0) {
            // Cancel buff if caster and owner are in different rooms or too far away
            if (Caster.Field.RoomId != Owner.Field.RoomId ||
                Vector3.Distance(Caster.Position, Owner.Position) > Metadata.Property.ClearOnDistanceFromCaster) {
                Owner.Buffs.Remove(Id, Caster.ObjectId);
                logger.Information("Buff {Id} removed due to distance from caster", Id);
                return;
            }
        }

        if (canExpire && !canProc && tickCount > EndTick) {
            Owner.Buffs.Remove(Id, Caster.ObjectId);
            return;
        }

        if (!UpdateEnabled()) {
            return;
        }

        if (!canProc || tickCount < NextProcTick) {
            return;
        }

        Proc();
    }

    public bool UpdateEnabled(bool notifyField = true) {
        bool enabled = Metadata.Condition.Check(Caster, Owner, Owner);
        if (Enabled != enabled) {
            Enabled = enabled;
            if (notifyField) {
                Field.Broadcast(BuffPacket.Update(this));
            }
        }

        return enabled;
    }

    public void Disable() {
        Enabled = false;
        canProc = false;
    }
    public void Enable() => Enabled = true;

    private void Proc() {
        ProcCount++;

        ApplyRecovery();
        ApplyDotDamage();
        ApplyDotBuff();
        ApplyCancel();
        ModifyDuration();
        Owner.ApplyEffects(Metadata.TickSkills, Caster, Owner, EventConditionType.Tick, buffId: Id, targets: [Owner]);
        ApplySplash();

        NextProcTick += IntervalTick;
        if (NextProcTick > EndTick) {
            canProc = false;
        }
    }

    private void ApplySplash() {
        foreach (SkillEffectMetadata effect in Metadata.Skills) {
            if (effect.Splash != null) {
                Caster.Field.AddSkill(Caster, effect, [Owner.Position], Owner.Rotation);
            }
        }
    }

    private void ApplyRecovery() {
        if (Metadata.Recovery == null) {
            return;
        }

        var record = new HealDamageRecord(Caster, Owner, ObjectId, Metadata.Recovery);
        var updated = new List<BasicAttribute>(3);
        if (record.HpAmount != 0) {
            Owner.Stats.Values[BasicAttribute.Health].Add(record.HpAmount);
            updated.Add(BasicAttribute.Health);
        }
        if (record.SpAmount != 0) {
            Owner.Stats.Values[BasicAttribute.Spirit].Add(record.SpAmount);
            updated.Add(BasicAttribute.Spirit);
        }
        if (record.EpAmount != 0) {
            Owner.Stats.Values[BasicAttribute.Stamina].Add(record.EpAmount);
            updated.Add(BasicAttribute.Stamina);
        }

        if (updated.Count > 0) {
            Field.Broadcast(StatsPacket.Update(Owner, updated.ToArray()));
        }
        Field.Broadcast(SkillDamagePacket.Heal(record));
    }

    private void ApplyDotDamage() {
        if (Metadata.Dot.Damage == null) {
            return;
        }

        var record = new DotDamageRecord(Caster, Owner, Metadata.Dot.Damage) {
            ProcCount = ProcCount,
        };
        var targetUpdated = new List<BasicAttribute>(3);
        if (record.HpAmount != 0) {
            Owner.Stats.Values[BasicAttribute.Health].Add(record.HpAmount);
            targetUpdated.Add(BasicAttribute.Health);
        }
        if (record.SpAmount != 0) {
            Owner.Stats.Values[BasicAttribute.Spirit].Add(record.SpAmount);
            targetUpdated.Add(BasicAttribute.Spirit);
        }
        if (record.EpAmount != 0) {
            Owner.Stats.Values[BasicAttribute.Stamina].Add(record.EpAmount);
            targetUpdated.Add(BasicAttribute.Stamina);
        }

        if (targetUpdated.Count <= 0) {
            return;
        }

        Field.Broadcast(StatsPacket.Update(Owner, targetUpdated.ToArray()));
        Field.Broadcast(SkillDamagePacket.DotDamage(record));
        if (record.RecoverHp != 0) {
            Caster.Stats.Values[BasicAttribute.Health].Add(record.RecoverHp);
            Field.Broadcast(StatsPacket.Update(Caster, BasicAttribute.Health));
        }
    }

    private void ApplyDotBuff() {
        if (Metadata.Dot.Buff == null) {
            return;
        }

        AdditionalEffectMetadataDot.DotBuff dotBuff = Metadata.Dot.Buff;
        if (dotBuff.Target == DotTargetType.Caster) {
            Caster.Buffs.AddBuff(Owner, Caster, dotBuff.Id, dotBuff.Level, Field.FieldTick);
        } else {
            Owner.Buffs.AddBuff(Caster, Owner, dotBuff.Id, dotBuff.Level, Field.FieldTick);
        }
    }

    private void ApplyCancel() {
        if (Metadata.Update.Cancel == null) {
            return;
        }

        if (Metadata.Update.Cancel.Ids.Length > 0) {
            foreach (int id in Metadata.Update.Cancel.Ids) {
                List<Buff> buffs = Owner.Buffs.EnumerateBuffs(id);
                foreach (Buff buff in buffs) {
                    if (!Metadata.Update.Cancel.CheckSameCaster || buff.Caster.ObjectId == Caster.ObjectId) {
                        Owner.Buffs.Remove(id, Caster.ObjectId);
                    }
                }
            }

            List<Buff> buffsToRemove = Owner.Buffs.EnumerateBuffs()
                .Where(buff =>
                    Metadata.Update.Cancel.Categories.Contains(buff.Metadata.Property.Category)
                    && (!Metadata.Update.Cancel.CheckSameCaster || buff.Caster.ObjectId == Caster.ObjectId)
                ).ToList();

            buffsToRemove.ForEach(buff => Owner.Buffs.Remove(buff.Id, Caster.ObjectId));
        }
    }

    public void ModifyDuration() {
        foreach (AdditionalEffectMetadataUpdate.ModifyDuration modifyDuration in Metadata.Update.Duration) {
            List<Buff> buffs = Owner.Buffs.EnumerateBuffs(modifyDuration.Id);
            if (buffs.Count == 0) {
                continue;
            }
            foreach (Buff buff in buffs) {
                buff.EndTick += (long) modifyDuration.Value;
                if (modifyDuration.Rate > 0) {
                    long remainingDuration = (long) (modifyDuration.Rate * (buff.EndTick - Environment.TickCount64));
                    buff.EndTick += (modifyDuration.Rate >= 1) ? remainingDuration : -remainingDuration;
                }

                // restart proc if possible
                if (NextProcTick < EndTick) {
                    canProc = true;
                }
                Field.Broadcast(BuffPacket.Update(buff));
            }
        }
    }

    public void UpdateEndTime(int modifyValue) {
        EndTick += modifyValue;
        Field.Broadcast(BuffPacket.Update(this));
    }

    public void WriteTo(IByteWriter writer) {
        WriteAdditionalEffect(writer);
        WriteShieldHealth(writer);
    }

    public void WriteAdditionalEffect(IByteWriter writer) {
        writer.WriteInt(StartTick.Truncate32());
        writer.WriteInt(EndTick.Truncate32());
        writer.WriteInt(Id);
        writer.WriteShort(Level);
        writer.WriteInt(Stacks);
        writer.WriteBool(Enabled);
    }

    public void WriteShieldHealth(IByteWriter writer) {
        writer.WriteLong(ShieldHealth);
    }

    public override string ToString() {
        return $"Buff(Id={Id}, Level={Level}, Caster={Caster.ObjectId}, Owner={Owner.ObjectId}, StartTick={StartTick}, EndTick={EndTick}, Stacks={Stacks}, Enabled={Enabled})";
    }
}
