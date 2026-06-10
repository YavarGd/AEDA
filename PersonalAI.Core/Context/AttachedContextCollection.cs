namespace PersonalAI.Core.Context;

public sealed class AttachedContextCollection
{
    private readonly List<AttachedContextItem> _items = [];
    private readonly HashSet<string> _duplicateKeys = new(StringComparer.Ordinal);

    public IReadOnlyList<AttachedContextItem> Items => _items;

    public int Count => _items.Count;

    public bool Add(AttachedContextItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!_duplicateKeys.Add(item.DuplicateKey))
        {
            return false;
        }

        _items.Add(item);
        return true;
    }

    public bool Remove(Guid id)
    {
        var index = _items.FindIndex(item => item.Id == id);

        if (index < 0)
        {
            return false;
        }

        _duplicateKeys.Remove(_items[index].DuplicateKey);
        _items.RemoveAt(index);
        return true;
    }

    public void Clear()
    {
        _items.Clear();
        _duplicateKeys.Clear();
    }

    public IReadOnlyList<AttachedContextItem> Snapshot()
    {
        return _items.ToArray();
    }
}
