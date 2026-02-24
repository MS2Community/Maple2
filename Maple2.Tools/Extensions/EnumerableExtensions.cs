using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Maple2.Tools.Extensions;

public static class EnumerableExtensions {
    public static IEnumerable<ImmutableList<T>> Batch<T>(this IEnumerable<T> source, int batchSize) {
        using IEnumerator<T> enumerator = source.GetEnumerator();
        while (enumerator.MoveNext()) {
            yield return YieldBatchElements(enumerator, batchSize - 1).ToImmutableList();
        }
    }

    private static IEnumerable<T> YieldBatchElements<T>(IEnumerator<T> source, int batchSize) {
        yield return source.Current;
        for (int i = 0; i < batchSize && source.MoveNext(); i++) {
            yield return source.Current;
        }
    }

    public static T? ElementAtOrDefault<T>(this IList<T> list, int index, T? fallback = default) {
        if (list == null) {
            throw new ArgumentNullException(nameof(list));
        }

        if (index >= 0 && index < list.Count) {
            return list[index];
        }

        return fallback;
    }

    public static T ElementAtOrDefault<T>(this IList<T> list, int index, Func<T> fallback) {
        if (list == null) {
            throw new ArgumentNullException(nameof(list));
        }

        if (index >= 0 && index < list.Count) {
            return list[index];
        }

        return fallback();
    }

    public static T Random<T>(this IList<T> list) {
        return list[System.Random.Shared.Next(list.Count)];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T AsType<TE, T>(this IEnumerable<TE> enumerable, Func<IEnumerable<TE>, T> function) {
        return function.Invoke(enumerable);
    }

    public static TV? GetValueOrDefault<TK, TV>(this IDictionary<TK, TV> dictionary, TK key, TV? @default = default) {
        return dictionary.TryGetValue(key, out TV? value) ? value : @default;
    }

    public delegate bool TryFunc<in T, TResult>(T input, out TResult value);

    public static IEnumerable<TResult> TrySelect<T, TResult>(this IEnumerable<T> source, TryFunc<T, TResult> parse) {
        foreach (T element in source) {
            if (parse(element, out TResult value)) {
                yield return value;
            }
        }
    }

    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source) where T : class => source.Where(x => x != null)!;

    public static bool TryGetValue<TK1, TK2, TV>(this IReadOnlyDictionary<TK1, IReadOnlyDictionary<TK2, TV>> dictionary, TK1 key1, TK2 key2,
                                                 [MaybeNullWhen(false)] out TV value) {
        if (!dictionary.TryGetValue(key1, out IReadOnlyDictionary<TK2, TV>? nested)) {
            value = default;
            return false;
        }

        return nested.TryGetValue(key2, out value);
    }

    public static TV? GetValueOrDefault<TK1, TK2, TV>(this IReadOnlyDictionary<TK1, IReadOnlyDictionary<TK2, TV>> dictionary, TK1 key1, TK2 key2,
                                                      TV? defaultValue = default) {
        if (!dictionary.TryGetValue(key1, out IReadOnlyDictionary<TK2, TV>? nested)) {
            return defaultValue;
        }

        return nested.GetValueOrDefault(key2) ?? defaultValue;
    }

    public static void AddIfNotDefault<TK, TV>(this IDictionary<TK, TV> dictionary, TK key, TV value) where TK : notnull where TV : notnull {
        if (!value.Equals(default(TV))) {
            dictionary[key] = value;
        }
    }

    public static void RemoveAll<TK1, TK2, TValue>(this IDictionary<TK1, IDictionary<TK2, TValue>> nestedDictionary, TK2 key)
        where TK1 : notnull where TK2 : notnull {
        foreach (IDictionary<TK2, TValue> innerDictionary in nestedDictionary.Values) {
            innerDictionary.Remove(key);
        }
    }
}
