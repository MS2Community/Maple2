using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Manager;

public class CurrencyManager {
    private readonly GameSession session;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    private Currency Currency => session.Player.Value.Currency;

    public CurrencyManager(GameSession session) {
        this.session = session;
    }

    public long Meret {
        get => Currency.Meret;
        set {
            if (value < 0) {
                throw new ArgumentException("Not enough Merets");
            }

            long delta = Math.Min(value, Constant.MaxMeret) - Currency.Meret;
            Currency.Meret = Math.Min(value, Constant.MaxMeret);
            session.Send(CurrencyPacket.UpdateMeret(Currency, delta));
        }
    }

    public long GameMeret {
        get => Currency.GameMeret;
        set {
            if (value < 0) {
                throw new ArgumentException("Not enough RedMerets");
            }

            long delta = Math.Min(value, Constant.MaxMeret) - Currency.GameMeret;
            Currency.GameMeret = Math.Min(value, Constant.MaxMeret);
            session.Send(CurrencyPacket.UpdateMeret(Currency, delta));
        }
    }

    // public long EventMeret {
    //     get => Currency.EventMeret;
    //     private set => Currency.EventMeret = value;
    // }

    public long Meso {
        get => Currency.Meso;
        set {
            if (value < 0) {
                throw new ArgumentException("Not enough Mesos");
            }

            Currency.Meso = Math.Min(value, Constant.MaxMeso);
            session.Send(CurrencyPacket.UpdateMeso(Currency));
        }
    }

    public long CanAddMeso(long amount) {
        return amount >= 0
            ? Math.Min(amount, Constant.MaxMeso - Currency.Meso)
            : Math.Max(amount, -Currency.Meso);
    }

    public long CanAddMeret(long amount) {
        return amount >= 0
            ? Math.Min(amount, Constant.MaxMeret - Currency.Meret)
            : Math.Max(amount, -Currency.Meret);
    }

    public long CanAddGameMeret(long amount) {
        return amount >= 0
            ? Math.Min(amount, Constant.MaxMeret - Currency.GameMeret)
            : Math.Max(amount, -Currency.GameMeret);
    }

    public long this[CurrencyType type] {
        get => type switch {
            CurrencyType.ValorToken => Currency.ValorToken,
            CurrencyType.Treva => Currency.Treva,
            CurrencyType.Rue => Currency.Rue,
            CurrencyType.HaviFruit => Currency.HaviFruit,
            CurrencyType.ReverseCoin => Currency.ReverseCoin,
            CurrencyType.MentorToken => Currency.MentorToken,
            CurrencyType.MenteeToken => Currency.MenteeToken,
            CurrencyType.StarPoint => Currency.StarPoint,
            CurrencyType.MesoToken => Currency.MesoToken,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid currency type."),
        };
        set {
            if (value < 0) {
                throw new ArgumentException($"Not enough {type}");
            }

            long delta;
            long overflow;
            switch (type) {
                case CurrencyType.ValorToken:
                    delta = Math.Min(value, Constants.HonorTokenMax) - Currency.ValorToken;
                    overflow = Math.Max(0, value - Constants.HonorTokenMax);
                    Currency.ValorToken = Math.Min(value, Constants.HonorTokenMax);
                    if (delta > 0) {
                        session.ConditionUpdate(ConditionType.get_honor_token, delta);
                    }
                    break;
                case CurrencyType.Treva:
                    delta = Math.Min(value, Constants.KarmaTokenMax) - Currency.Treva;
                    overflow = Math.Max(0, value - Constants.KarmaTokenMax);
                    Currency.Treva = Math.Min(value, Constants.KarmaTokenMax);
                    if (delta > 0) {
                        session.ConditionUpdate(ConditionType.get_karma_token, delta);
                    }
                    break;
                case CurrencyType.Rue:
                    delta = Math.Min(value, Constants.LuTokenMax) - Currency.Rue;
                    overflow = Math.Max(0, value - Constants.LuTokenMax);
                    Currency.Rue = Math.Min(value, Constants.LuTokenMax);
                    if (delta > 0) {
                        session.ConditionUpdate(ConditionType.get_lu_token, delta);
                    }
                    break;
                case CurrencyType.HaviFruit:
                    delta = Math.Min(value, Constants.HabiTokenMax) - Currency.HaviFruit;
                    overflow = Math.Max(0, value - Constants.HabiTokenMax);
                    Currency.HaviFruit = Math.Min(value, Constants.HabiTokenMax);
                    if (delta > 0) {
                        session.ConditionUpdate(ConditionType.get_habi_token, delta);
                    }
                    break;
                case CurrencyType.ReverseCoin:
                    delta = Math.Min(value, Constants.ReverseCoinMax) - Currency.ReverseCoin;
                    overflow = Math.Max(0, value - Constants.ReverseCoinMax);
                    Currency.ReverseCoin = Math.Min(value, Constants.ReverseCoinMax);
                    if (delta > 0) {
                        session.ConditionUpdate(ConditionType.get_reverse_coin, delta);
                    }
                    break;
                case CurrencyType.MentorToken:
                    delta = Math.Min(value, Constants.MentorTokenMax) - Currency.MentorToken;
                    overflow = Math.Max(0, value - Constants.MentorTokenMax);
                    Currency.MentorToken = Math.Min(value, Constants.MentorTokenMax);
                    if (delta > 0) {
                        session.ConditionUpdate(ConditionType.get_mentor_token, delta);
                    }
                    break;
                case CurrencyType.MenteeToken:
                    delta = Math.Min(value, Constants.MenteeTokenMax) - Currency.MenteeToken;
                    overflow = Math.Max(0, value - Constants.MenteeTokenMax);
                    Currency.MenteeToken = Math.Min(value, Constants.MenteeTokenMax);
                    if (delta > 0) {
                        session.ConditionUpdate(ConditionType.get_mentee_token, delta);
                    }
                    break;
                case CurrencyType.StarPoint:
                    delta = Math.Min(value, Constant.StarPointMax) - Currency.StarPoint;
                    overflow = Math.Max(0, value - Constant.StarPointMax);
                    Currency.StarPoint = Math.Min(value, Constant.StarPointMax);
                    if (delta > 0) {
                        session.ConditionUpdate(ConditionType.get_star_point, delta);
                    }
                    break;
                case CurrencyType.MesoToken:
                    delta = Math.Min(value, Constant.MesoTokenMax) - Currency.MesoToken;
                    overflow = Math.Max(0, value - Constant.MesoTokenMax);
                    Currency.MesoToken = Math.Min(value, Constant.MesoTokenMax);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid currency type.");
            }

            session.Send(CurrencyPacket.UpdateCurrency(Currency, type, delta, overflow));
        }
    }
}
