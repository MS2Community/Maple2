using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Maple2.Tools;

public class ConcurrentMultiDictionary<TK1, TK2, TV> where TK1 : notnull where TK2 : notnull {
    private readonly ConcurrentDictionary<TK1, TV> data;
    private readonly ConcurrentDictionary<TK2, TK1> mapping;

    public ConcurrentMultiDictionary() {
        data = new ConcurrentDictionary<TK1, TV>();
        mapping = new ConcurrentDictionary<TK2, TK1>();
    }

    public int Count => data.Count;

    public TV this[TK1 key] => data[key];
    public TV this[TK2 key] => data[mapping[key]];

    public ICollection<TK1> Keys1 => data.Keys;
    public ICollection<TK2> Keys2 => mapping.Keys;
    public ICollection<TV> Values => data.Values;

    public bool ContainsKey(TK1 key1) {
        return data.ContainsKey(key1);
    }

    public bool ContainsKey(TK2 key2) {
        return mapping.ContainsKey(key2);
    }

    public bool TryAdd(TK1 key1, TK2 key2, TV value) {
        if (data.ContainsKey(key1) || mapping.ContainsKey(key2)) {
            return false;
        }

        return data.TryAdd(key1, value) && mapping.TryAdd(key2, key1);
    }

    public bool TryGet(TK1 key, [MaybeNullWhen(false)] out TV value) {
        return data.TryGetValue(key, out value);
    }

    public bool TryGetKey1(TK1 key, [MaybeNullWhen(false)] out TV value) {
        return data.TryGetValue(key, out value);
    }

    public bool TryGet(TK2 key2, [MaybeNullWhen(false)] out TV value) {
        if (mapping.TryGetValue(key2, out TK1? key1)) {
            return data.TryGetValue(key1, out value);
        }

        value = default;
        return false;
    }

    public bool TryGetKey2(TK2 key2, [MaybeNullWhen(false)] out TV value) {
        if (mapping.TryGetValue(key2, out TK1? key1)) {
            return data.TryGetValue(key1, out value);
        }

        value = default;
        return false;
    }

    public TV GetValueOrDefault(TK1 key, TV value) {
        return data.TryGetValue(key, out TV? result) ? result : value;
    }

    public TV GetValueOrDefault(TK2 key2, TV value) {
        return mapping.TryGetValue(key2, out TK1? key1) ? GetValueOrDefault(key1, value) : value;
    }

    public bool Remove(TK1 key1, [MaybeNullWhen(false)] out TV value) {
        TK2 key2 = mapping.FirstOrDefault(entry => EqualityComparer<TK1>.Default.Equals(entry.Value, key1)).Key;
        return data.Remove(key1, out value) && mapping.Remove(key2, out _);
    }

    public bool Remove(TK2 key2, [MaybeNullWhen(false)] out TV value) {
        if (!mapping.TryGetValue(key2, out TK1? key1)) {
            value = default;
            return false;
        }
        return data.Remove(key1, out value) && mapping.Remove(key2, out _);
    }
}
