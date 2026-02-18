using Maple2.Database.Context;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public class BanWordStorage(MetadataContext context) {
    private HashSet<string> banWords = new(StringComparer.CurrentCultureIgnoreCase);
    private HashSet<string> ugcBanWords = new(StringComparer.CurrentCultureIgnoreCase);

    public bool ContainsBannedWord(string word) {
        if (banWords.Count == 0) {
            lock (context) {
                if (banWords.Count == 0) {
                    banWords = context.BanWordMetadata
                        .AsNoTracking()
                        .Where(bw => !bw.Ugc)
                        .Select(bw => bw.Value)
                        .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
                }
            }
        }
        return banWords.Any(bw => word.Contains(bw, StringComparison.CurrentCultureIgnoreCase));
    }

    public bool ContainsUgcBannedWord(string word) {
        if (ugcBanWords.Count == 0) {
            lock (context) {
                if (ugcBanWords.Count == 0) {
                    ugcBanWords = context.BanWordMetadata
                        .AsNoTracking()
                        .Where(bw => bw.Ugc)
                        .Select(bw => bw.Value)
                        .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
                }
            }
        }
        return ugcBanWords.Any(bw => word.Contains(bw, StringComparison.CurrentCultureIgnoreCase));
    }
}
