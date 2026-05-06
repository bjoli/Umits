using System.Collections;
using System.Collections.Immutable;

namespace Collections;

public sealed partial class RrbList<T> : ICollection<T>, IImmutableList<T> where T : notnull
{
// Explicit Interface Implementation:
    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return new RrbEnumerator<T>(this);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    // ---------------------------------------
    // IImmutableList implementation ---------
    // ---------------------------------------
    public IImmutableList<T> Clear()
    {
        return Empty;
    }

    public RrbList<T> AddRange(IEnumerable<T> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));

        // Optimization: If items is already an RrbList, use O(log N) Merge
        if (items is RrbList<T> otherList) return Merge(otherList);

        // Optimization: Use Builder to create a tree in O(M) then Merge in O(log N).
        // This is strictly faster than repeatedly calling Add (O(M log N)).
        var other = Create(items);
        if (other.Count == 0) return this;

        return Merge(other);
    }
    
    IImmutableList<T> IImmutableList<T>.AddRange(IEnumerable<T> items)
    {
        return AddRange(items);
    }

    public RrbList<T> InsertRange(int index, IEnumerable<T> items)
    {
        if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (items == null) throw new ArgumentNullException(nameof(items));

        // 0. Fast Path: Empty Insert
        if (items is ICollection<T> c && c.Count == 0) return this;

        // 1. Split the tree at the insertion point (O(log N))
        //    Left = [0...index-1], Right = [index...End]
        var (left, right) = Split(index);

        // 2. Convert items to RrbList (O(M))
        //    If it's already an RrbList, this is O(1)
        var middle = items as RrbList<T> ?? Create(items);

        // 3. Merge: Left + Middle + Right (O(log N))
        return left.Merge(middle).Merge(right);
    }
    
    IImmutableList<T> IImmutableList<T>.InsertRange(int index, IEnumerable<T> items)
    {
        return InsertRange(index, items);
    }

    public RrbList<T> RemoveRange(int index, int count)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (index + count > Count) throw new ArgumentOutOfRangeException(nameof(count), "index+count is out of bounds");

        if (count == 0) return this;
        if (count == Count) return Empty;

        // 1. Slice Before (O(log N))
        var left = Slice(0, index);

        // 2. Slice After (O(log N))
        var itemsAfter = Count - (index + count);
        var right = Slice(index + count, itemsAfter);

        // 3. Merge (O(log N))
        return left.Merge(right);
    }

    IImmutableList<T> IImmutableList<T>.RemoveRange(int index, int count)
    {
        return RemoveRange(index, count);
    }


    /**
     * <summary>
     *     Removes the specified values from this list.
     * </summary>
     * <param name="items">The items to remove.</param>
     * <param name="equalityComparer">The equality comparer to use for locating the items.</param>
     * <returns>A new list with the items removed.</returns>
     */
    public RrbList<T> RemoveRange(IEnumerable<T> items, IEqualityComparer<T>? equalityComparer)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));

        // Fast check for empty inputs
        if (Count == 0) return this;

        equalityComparer ??= EqualityComparer<T>.Default;


        // We use a dictionary to track how many instances of each value we need to remove.
        var toRemove = new Dictionary<T, int>(equalityComparer);
        
        // Enumerate to array to avoid multiple enumerations.
        var enumerable = items as T[] ?? items.ToArray();
        foreach (var item in enumerable)
            if (toRemove.TryGetValue(item, out var count))
                toRemove[item] = count + 1;
            else
                toRemove[item] = 1;

        // If nothing to remove, return original
        if (toRemove.Count == 0) return this;

        // Rebuild the list using Builder
        var builder = new RrbBuilder<T>();
        var changed = false;

        foreach (var item in this)
            // Check if this item is one we need to remove
            if (toRemove.TryGetValue(item, out var count) && count > 0)
            {
                // Skip adding this item, and decrement the "quota" for this value
                toRemove[item] = count - 1;
                changed = true;
            }
            else
            {
                builder.Add(item);
            }

        if (!changed) return this;
        return builder.ToImmutable();
    }

    IImmutableList<T> IImmutableList<T>.RemoveRange(IEnumerable<T> items, IEqualityComparer<T>? equalityComparer)
    {
        return RemoveRange(items, equalityComparer);
    }

    // --- IImmutableList<T> Search Operations ---

    public int IndexOf(T item, int index, int count, IEqualityComparer<T>? equalityComparer)
    {
        if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (count < 0 || index + count > Count) throw new ArgumentOutOfRangeException(nameof(count));

        equalityComparer ??= EqualityComparer<T>.Default;

        // Use the optimized RrbEnumerator
        var enumerator = new RrbEnumerator<T>(this, index);
        var matched = 0;

        while (matched < count && enumerator.MoveNext())
        {
            if (equalityComparer.Equals(enumerator.Current, item)) return index + matched;
            matched++;
        }

        return -1;
    }

    public int LastIndexOf(T item, int index, int count, IEqualityComparer<T>? equalityComparer)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index)); // Start index
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (index - count + 1 < 0) throw new ArgumentOutOfRangeException(nameof(count));

        equalityComparer ??= EqualityComparer<T>.Default;

        // Collections doesn't support efficient reverse iteration yet.
        // We use direct indexing, which is O(log N) per item.
        // If this is too slow, we would need to implement RrbReverseEnumerator.
        for (var i = 0; i < count; i++)
        {
            var currIndex = index - i;
            if (equalityComparer.Equals(this[currIndex], item)) return currIndex;
        }

        return -1;
    }

    // --- IImmutableList<T> Modification Operations ---

    public IImmutableList<T> Remove(T value, IEqualityComparer<T>? equalityComparer)
    {
        var index = IndexOf(value, 0, Count, equalityComparer);
        if (index < 0) return this;
        return RemoveAt(index);
    }

    public IImmutableList<T> RemoveAll(Predicate<T> match)
    {
        if (match == null) throw new ArgumentNullException(nameof(match));

        // Bulk removal is best handled by rebuilding.
        // Since we have a fast Builder, this is O(N).
        var builder = new RrbBuilder<T>();
        foreach (var item in this)
            if (!match(item))
                builder.Add(item);

        return builder.ToImmutable();
    }

    public IImmutableList<T> Replace(T oldValue, T newValue, IEqualityComparer<T>? equalityComparer)
    {
        var index = IndexOf(oldValue, 0, Count, equalityComparer);
        if (index < 0) throw new ArgumentException("Value not found"); // Standard IImmutableList behavior
        return SetItem(index, newValue);
    }

    // Explicit Interface Implementations for overloads to avoid ambiguity
    IImmutableList<T> IImmutableList<T>.Add(T value)
    {
        return Add(value);
    }

    IImmutableList<T> IImmutableList<T>.Insert(int index, T element)
    {
        return Insert(index, element);
    }

    IImmutableList<T> IImmutableList<T>.RemoveAt(int index)
    {
        return RemoveAt(index);
    }

    IImmutableList<T> IImmutableList<T>.SetItem(int index, T value)
    {
        return SetItem(index, value);
    }

    /**
  * <summary>
  *     Returns an enumerator that iterates through the list.
  * </summary>
  * <returns>An <see cref="IEnumerator{T}" /> for the list.</returns>
  */
    public RrbEnumerator<T> GetEnumerator()
    {
        return new RrbEnumerator<T>(this);
    }

    public RrbEnumerator<T> RangeEnumerator(int index, int count)
    {
        // Bounds checks handled by constructor
        return new RrbEnumerator<T>(this, index, count);
    }

    // Helper to expose the reverse range
    public RrbReverseEnumerator<T> ReverseEnumerator(int index, int count)
    {
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        // Constructor does bounds check for 'count'
        return new RrbReverseEnumerator<T>(this, index, count);
    }

    public RrbReverseEnumerator<T> ReverseEnumerator()
    {
        return new RrbReverseEnumerator<T>(this);
    }
}