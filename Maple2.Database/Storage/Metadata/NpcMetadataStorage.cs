using System.Diagnostics.CodeAnalysis;
using Caching;
using Maple2.Database.Context;
using Maple2.Model.Metadata;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Maple2.Database.Storage;

public class NpcMetadataStorage : MetadataStorage<int, NpcMetadata>, ISearchable<NpcMetadata> {
    private const int CACHE_SIZE = 7500; // ~7.4k total npcs
    private const int ANI_CACHE_SIZE = 2500;
    private const int MOB_BASE_ID = 20000000; // Starting id for mobs

    private readonly Dictionary<string, HashSet<int>> tagLookup;
    protected readonly LRUCache<string, AnimationMetadata> AniCache;
    private static string NormalizeAiPath(string value) {
        return value.Replace('\\', '/');
    }

    private static string GetDirectoryPath(string aiPath) {
        string normalized = NormalizeAiPath(aiPath);
        int index = normalized.LastIndexOf('/');
        return index >= 0 ? normalized.Substring(0, index) : string.Empty;
    }

    private static string GetFileNameWithoutExtensionSafe(string aiPath) {
        string normalized = NormalizeAiPath(aiPath);
        string fileName = Path.GetFileNameWithoutExtension(normalized);
        return fileName ?? string.Empty;
    }

    public NpcMetadata? FindByAiPath(string aiPath, int preferredDifficulty = -1, int preferredId = 0) {
        string normalized = NormalizeAiPath(aiPath);

        lock (Context) {
            List<NpcMetadata> candidates = Context.NpcMetadata
                .Where(npc => npc.AiPath != null)
                .AsEnumerable()
                .Where(npc => NormalizeAiPath(npc.AiPath!).Equals(normalized, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count == 0) {
                return null;
            }

            if (preferredDifficulty >= 0) {
                List<NpcMetadata> difficultyMatches = candidates
                    .Where(npc => npc.Basic.Difficulty == preferredDifficulty)
                    .ToList();

                if (difficultyMatches.Count > 0) {
                    candidates = difficultyMatches;
                }
            }

            if (preferredId != 0) {
                NpcMetadata? closestId = candidates
                    .OrderBy(npc => Math.Abs(npc.Id - preferredId))
                    .FirstOrDefault();

                if (closestId != null) {
                    return closestId;
                }
            }

            return candidates.FirstOrDefault();
        }
    }

    public NpcMetadata? FindByRelativeAiAlias(string currentAiPath, int aliasId) {
        if (string.IsNullOrWhiteSpace(currentAiPath) || aliasId <= 0) {
            return null;
        }

        string normalizedCurrent = NormalizeAiPath(currentAiPath);
        string currentDirectory = GetDirectoryPath(normalizedCurrent);
        string currentFileName = GetFileNameWithoutExtensionSafe(normalizedCurrent);

        if (string.IsNullOrWhiteSpace(currentDirectory)) {
            return null;
        }

        List<NpcMetadata> candidates;
        lock (Context) {
            candidates = Context.NpcMetadata
                .Where(npc => npc.AiPath != null)
                .AsEnumerable()
                .Where(npc => NormalizeAiPath(npc.AiPath!).StartsWith(currentDirectory + "/", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (candidates.Count == 0) {
            return null;
        }

        string[] directCandidates = {
        $"{currentDirectory}/AI_{aliasId}.xml",
        $"{currentDirectory}/{aliasId}.xml",
        $"{currentDirectory}/{currentFileName}_{aliasId}.xml",
        $"{currentDirectory}/{currentFileName}Type{aliasId}.xml",
        $"{currentDirectory}/{currentFileName}_Type{aliasId}.xml",
        $"{currentDirectory}/{currentFileName}Summon{aliasId}.xml",
        $"{currentDirectory}/{currentFileName}_Summon{aliasId}.xml"
    };

        foreach (string candidate in directCandidates) {
            NpcMetadata? exact = candidates.FirstOrDefault(npc =>
                NormalizeAiPath(npc.AiPath!).Equals(candidate, StringComparison.OrdinalIgnoreCase));

            if (exact != null) {
                return exact;
            }
        }

        string[] aliasTokens = {
        $"_{aliasId}",
        $"Type{aliasId}",
        $"Summon{aliasId}",
        $"Soldier{aliasId}",
        $"Mob{aliasId}",
        $"{aliasId}.xml"
    };

        NpcMetadata? tokenMatch = candidates.FirstOrDefault(npc => {
            string fileName = Path.GetFileName(NormalizeAiPath(npc.AiPath!));
            return aliasTokens.Any(token => fileName.Contains(token, StringComparison.OrdinalIgnoreCase));
        });

        if (tokenMatch != null) {
            return tokenMatch;
        }

        return null;
    }

    public NpcMetadataStorage(MetadataContext context) : base(context, CACHE_SIZE) {
        tagLookup = new Dictionary<string, HashSet<int>>();
        AniCache = new LRUCache<string, AnimationMetadata>(ANI_CACHE_SIZE, (int) (ANI_CACHE_SIZE * 0.05));

        foreach (NpcMetadata npc in Context.NpcMetadata.Where(npc => npc.Id > MOB_BASE_ID)) {
            Cache.AddReplace(npc.Id, npc);
            foreach (string tag in npc.Basic.MainTags) {
                if (!tagLookup.ContainsKey(tag)) {
                    tagLookup[tag] = [];
                }

                tagLookup[tag].Add(npc.Id);
            }
        }
    }

    public bool TryGet(int id, [NotNullWhen(true)] out NpcMetadata? npc) {
        if (Cache.TryGet(id, out npc)) {
            return true;
        }

        lock (Context) {
            // Double-checked locking
            if (Cache.TryGet(id, out npc)) {
                return true;
            }

            npc = Context.NpcMetadata.Find(id);

            if (npc == null) {
                return false;
            }

            Cache.AddReplace(id, npc);
        }

        return true;
    }

    public bool TryLookupTag(string tag, [NotNullWhen(true)] out IReadOnlyCollection<int>? npcIds) {
        bool result = tagLookup.TryGetValue(tag, out HashSet<int>? set);
        npcIds = set;

        return result;
    }

    public List<NpcMetadata> Search(string name) {
        lock (Context) {
            return Context.NpcMetadata
                .Where(npc => EF.Functions.Like(npc.Name!, $"%{name}%"))
                .ToList();
        }
    }

    public AnimationMetadata? GetAnimation(string model) {
        if (AniCache.TryGet(model, out AnimationMetadata? animation)) {
            return animation;
        }

        lock (Context) {
            animation = Context.AnimationMetadata.Find(model);
        }

        if (animation == null) {
            return null;
        }

        AniCache.AddReplace(model, animation);

        return animation;
    }
}
