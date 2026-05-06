/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Linus Björnstam
 *
 * Portions of this code are based on a port of c-rrb (https://github.com/hypirion/c-rrb),
 * Copyright (c) 2013-2014 Jean Niklas L'orange, licensed under the MIT License.
 */

using System.Text;

namespace Collections;

public sealed partial class RrbList<T>
{
    /**
     * <summary>
     *     Gets an empty <see cref="RrbList{T}" />.
     * </summary>
     */
    public static readonly RrbList<T> Empty = new();

    internal readonly Node<T>? Root;
    internal readonly int Shift;

    internal readonly T[] Tail;
    internal readonly int TailLen;

    internal RrbList(Node<T>? root, T[] tail, int cnt, int shift, int tailLen)
    {
        Root = root;
        Tail = tail;
        Count = cnt;
        Shift = shift;
        TailLen = tailLen;
    }

    /**
     * <summary>
     *     Initializes a new instance of the <see cref="RrbList{T}" /> class that is empty.
     * </summary>
     */
    public RrbList()
    {
        Root = null;
        Tail = Array.Empty<T>();
        Count = 0;
        Shift = 0;
        TailLen = 0;
    }

    /**
     * <summary>
     *     Initializes a new instance of the <see cref="RrbList{T}" /> class that contains elements copied from the specified
     *     collection.
     * </summary>
     * <param name="items">The collection whose elements are copied to the new list.</param>
     */
    public RrbList(IEnumerable<T> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        if (items is RrbList<T> other)
        {
            Root = other.Root;
            Tail = other.Tail;
            Count = other.Count;
            Shift = other.Shift;
            TailLen = other.TailLen;
            return;
        }

        RrbBuilder<T> builder;

        // TODO: benchmark where it makes sense to use a fat tail.
        var enumerable = items as T[] ?? items.ToArray();
        if (enumerable.Count() > 4096)
            builder = new RrbBuilder<T>();
        else
            builder = new RrbBuilder<T>();

        foreach (var item in enumerable) builder.Add(item);

        var temp = builder.ToImmutable();
        Root = temp.Root;
        Tail = temp.Tail;
        Count = temp.Count;
        Shift = temp.Shift;
        TailLen = temp.TailLen;
    }

    /**
     * <summary>
     *     Gets a value indicating whether the <see cref="T:System.Collections.Generic.ICollection`1" /> is read-only.
     * </summary>
     */
    public bool IsReadOnly => true;

    // --- ICollection<T> Implementation ---
    void ICollection<T>.Add(T item)
    {
        throw new NotSupportedException("RrbList is immutable.");
    }

    void ICollection<T>.Clear()
    {
        throw new NotSupportedException("RrbList is immutable.");
    }

    bool ICollection<T>.Remove(T item)
    {
        throw new NotSupportedException("RrbList is immutable.");
    }

    /**
     * <summary>
     *     Determines whether the list contains a specific value.
     * </summary>
     * <param name="item">The object to locate in the list.</param>
     * <returns>true if the item is found in the list; otherwise, false.</returns>
     * <remarks>This could be made faster.</remarks>
     */
    public bool Contains(T item)
    {
        return !Iter(x =>
        {
            if (EqualityComparer<T>.Default.Equals(x, item)) return false;

            return true;
        });
    }

    /**
     * <summary>
     *     The number of elements in the list
     * </summary>
     */
    public int Count { get; }

    /**
     * <summary>
     *     Copies the elements of the <see cref="RrbList{T}" /> to an <see cref="T:System.Array" />, starting at a particular
     *     <see cref="T:System.Array" /> index.
     * </summary>
     * <param name="array">
     *     The one-dimensional <see cref="T:System.Array" /> that is the destination of the elements copied
     *     from <see cref="RrbList{T}" />. The <see cref="T:System.Array" /> must have
     * </param>
     * <param name="arrayIndex">The zero-based index in <paramref name="array" /> at which copying begins.</param>
     */
    public void CopyTo(T[] array, int arrayIndex)
    {
        CopyTo(0, array, arrayIndex, Count);
    }


    /**
     * <summary>
     *     Gets the element at the specified index.
     * </summary>
     * <param name="index">The zero-based index of the element to get.</param>
     * <returns>The element at the specified index.</returns>
     */
    // Here we have an indexer that uses AVX for indexing into relaxed nodes. It is about 1.65x faster for a relaxed
    // operation.
    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new IndexOutOfRangeException();

            var tailOffset = Count - TailLen;
            // Direct array access
            if (index >= tailOffset) return Tail[index - tailOffset];

            var node = Root!;
            var shift = Shift;

            if (shift == 0)
                return RrbAlgorithm.AsLeaf(node).Items[index];

            while (node.IsRelaxed())
            {
                var internalNode = RrbAlgorithm.AsInternal(node);
                var (childIndex, relativeIndex) = RrbAlgorithm.GetRelaxedIndexAvx(internalNode, index, shift);

                node = internalNode.Children[childIndex]!;
                index = relativeIndex;
                shift -= Constants.RRB_BITS;
            }

            while (shift > 0)
            {
                var childIndex = (index >> shift) & Constants.RRB_MASK;
                node = RrbAlgorithm.AsInternal(node).Children[childIndex]!;
                shift -= Constants.RRB_BITS;
            }

            return RrbAlgorithm.AsLeaf(node).Items[index & Constants.RRB_MASK];
        }
    }

    /**
     * <summary>
     *     Copies a range of elements from the RrbList to a compatible one-dimensional array.
     * </summary>
     */
    public void CopyTo(int sourceIndex, T[] destination, int destinationIndex, int count)
    {
        if (sourceIndex < 0 || sourceIndex > Count) throw new ArgumentOutOfRangeException(nameof(sourceIndex));
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        if (destinationIndex < 0) throw new ArgumentOutOfRangeException(nameof(destinationIndex));
        if (destination.Length - destinationIndex < count)
            throw new ArgumentException("Destination array is too small.");
        if (count == 0) return;

        var tailOffset = Count - TailLen;

        // 1. Copy from Tree (if the range starts before the tail)
        if (sourceIndex < tailOffset)
        {
            var itemsFromTree = Math.Min(count, tailOffset - sourceIndex);

            if (Root != null)
                CopyRangeNode(Root, Shift, sourceIndex, destination, destinationIndex, itemsFromTree);

            destinationIndex += itemsFromTree;
            count -= itemsFromTree;
            sourceIndex += itemsFromTree; // Should equal tailOffset now
        }

        // 2. Copy from Tail (if there are remaining items to copy)
        if (count > 0)
        {
            var idxInTail = sourceIndex - tailOffset;
            Array.Copy(Tail, idxInTail, destination, destinationIndex, count);
        }
    }

    private static void CopyRangeNode(Node<T> node, int shift, int offsetInNode, T[] dest, int destIdx, int count)
    {
        // Base Case: Leaf
        if (shift == 0)
        {
            var leaf = RrbAlgorithm.AsLeaf(node);
            Array.Copy(leaf.Items, offsetInNode, dest, destIdx, count);
            return;
        }

        // Recursive Step: Internal
        var internalNode = RrbAlgorithm.AsInternal(node);

        var (childIdx, subIdx) = RrbAlgorithm.GetChildIndexAvx(internalNode, offsetInNode, shift);

        while (count > 0 && childIdx < internalNode.Len)
        {
            var child = internalNode.Children[childIdx]!;

            int childSize;
            if (internalNode.SizeTable != null)
            {
                var prev = childIdx > 0 ? internalNode.SizeTable[childIdx - 1] : 0;
                childSize = internalNode.SizeTable[childIdx] - prev;
            }
            else
            {
                if (childIdx == internalNode.Len - 1)
                    childSize = RrbAlgorithm.CountTree(child, shift - Constants.RRB_BITS);
                else
                    childSize = 1 << shift;
            }

            var availableInChild = childSize - subIdx;
            var toCopy = Math.Min(count, availableInChild);

            CopyRangeNode(child, shift - Constants.RRB_BITS, subIdx, dest, destIdx, toCopy);

            count -= toCopy;
            destIdx += toCopy;

            childIdx++;
            subIdx = 0;
        }
    }

    /**
     * <summary>
     *     Creates a new RRB-List from an <see cref="IEnumerable{T}" />.
     * </summary>
     * <param name="items">The items to create the list from.</param>
     * <returns>A new RRB-List containing the items.</returns>
     */
    public static RrbList<T> Create(IEnumerable<T> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        if (items is RrbList<T> rrb) return rrb;
        if (items is ICollection<T> c && c.Count == 0) return Empty;
        return new RrbList<T>(items);
    }


    /**
     * <summary>
     *     Returns a new list with the specified item added to the end.
     * </summary>
     * <param name="item">The item to add.</param>
     * <returns>A new list with the item added.</returns>
     */
    public RrbList<T> Add(T item)
    {
        // Case 1: Tail has room
        if (TailLen < Constants.RRB_BRANCHING)
        {
            var newTail = new T[TailLen + 1];
            Array.Copy(Tail, 0, newTail, 0, TailLen);
            newTail[TailLen] = item;

            return new RrbList<T>(Root, newTail, Count + 1, Shift, TailLen + 1);
        }

        // Case 2: Tail is full, push to tree
        var newShift = Shift;
        var oldTailLeaf = new LeafNode<T>(Tail, TailLen, OwnerId.None); // Wrap current tail

        // Delegate to algorithm with wrapped leaf
        var newRoot = RrbAlgorithm.AppendLeafToTree(Root, oldTailLeaf, ref newShift, OwnerId.None);

        var newTailArr = new[] { item };

        return new RrbList<T>(newRoot, newTailArr, Count + 1, newShift, 1);
    }

    /**
     * <summary>
     *     Returns a new list with the element at the specified index replaced with the new value.
     * </summary>
     * <param name="index">The index of the element to replace.</param>
     * <param name="value">The new value for the element.</param>
     * <returns>A new list with the replaced item.</returns>
     */
    public RrbList<T> SetItem(int index, T value)
    {
        if (index < 0 || index >= Count) throw new IndexOutOfRangeException();

        var tailOffset = Count - TailLen;
        if (index >= tailOffset)
        {
            var newTail = new T[TailLen];
            Array.Copy(Tail, 0, newTail, 0, TailLen);
            newTail[index - tailOffset] = value;

            return new RrbList<T>(Root, newTail, Count, Shift, TailLen);
        }

        var newRoot = RrbAlgorithm.Update(Root!, index, value, Shift, OwnerId.None);
        return new RrbList<T>(newRoot, Tail, Count, Shift, TailLen);
    }


    /**
     * <summary>
     *     Creates a mutable builder from the current list.
     * </summary>
     * <returns>A new <see cref="RrbBuilder{T}" />.</returns>
     */
    public RrbBuilder<T> ToBuilder(int leafCapacify = Constants.RRB_BRANCHING)
    {
        return new RrbBuilder<T>(this);
    }


    /**
     * <summary>
     *     Creates a slice of the list.
     * </summary>
     * <param name="start">The zero-based index at which to begin the slice.</param>
     * <param name="count">The number of elements in the slice.</param>
     * <returns>A new list that is a slice of the original list.</returns>
     */
    public RrbList<T> Slice(int start, int count)
    {
        if (start < 0 || count < 0 || start + count > Count)
            throw new ArgumentOutOfRangeException();
        if (count == 0) return Empty;
        if (start == 0 && count == Count) return this;

        // --- 1. Small Slice Optimization ---
        if (count <= Constants.RRB_BRANCHING * 2)
        {
            var arr = new T[count];
            CopyTo(start, arr, 0, count);

            if (count <= Constants.RRB_BRANCHING)
                // Tail Only
                return new RrbList<T>(null, arr, count, 0, count);

            // Root + Tail
            var rootLen = Constants.RRB_BRANCHING;
            var tailLen = count - rootLen;

            var rootItems = new T[rootLen];
            var tailItems = new T[tailLen];

            Array.Copy(arr, 0, rootItems, 0, rootLen);
            Array.Copy(arr, rootLen, tailItems, 0, tailLen);

            var rootNode = new LeafNode<T>(rootItems, rootLen, OwnerId.None);
            return new RrbList<T>(rootNode, tailItems, count, 0, tailLen);
        }

        // --- 2. General Case ---
        var treeSize = Count - TailLen;
        var treeSliceStart = start;
        var treeSliceEnd = Math.Min(start + count, treeSize);
        var treeSliceCount = Math.Max(0, treeSliceEnd - treeSliceStart);

        Node<T>? newRoot = null;
        
        // Slice falls into tail
        if (start > treeSize)
        {
            var tTail = Tail.AsSpan().Slice(start - treeSize, count).ToArray();
            return new RrbList<T>(null, tTail, count, 0, tTail.Length);
        }
        
        // Drop/skip. Start is less than TreeSize, meaning Root is not null
        if (start != 0 && start + count == Count)
        {
            newRoot = RrbAlgorithm.SliceLeftRec(Root!, start, Shift);
            int tempShift = Shift;
            
            // Squash the tree.
            while (newRoot!.Len == 1 && tempShift > 0)
            {
                newRoot = RrbAlgorithm.AsInternal(newRoot).Children[0];
                tempShift -= Constants.RRB_BITS;
            }
            return new RrbList<T>(newRoot, Tail, count, tempShift, TailLen);
            
        }
        
        // Start is 0. It is a take operation inside the tree
        if (start == 0 && count < treeSize)
        {
            var takeRoot = RrbAlgorithm.SliceRightAndPromote(Root!, count, Shift,
                out T[]? takeTail,
                out int len);
            int tempShift = Shift;
            
            while (takeRoot!.Len == 1 && tempShift < 0)
            {
                takeRoot = RrbAlgorithm.AsInternal(takeRoot).Children[0];
                tempShift -= Constants.RRB_BITS;
            }
            return new RrbList<T>(takeRoot,takeTail, count, tempShift, len);
        }

        // Receive raw array + length directly
        T[] promotedTail = Array.Empty<T>();
        var promotedTailLen = 0;
        var newShift = Shift;

        if (treeSliceCount > 0)
            newRoot = RrbAlgorithm.Slice(Root!, treeSliceStart, treeSliceCount, ref newShift, out promotedTail,
                out promotedTailLen);

        // --- 3. Handle Tail Merging ---
        T[] finalTail;
        var originalTailNeeded = start + count > treeSize;

        if (originalTailNeeded)
        {
            var tailStartIdx = Math.Max(0, start - treeSize);
            var tailEndIdx = start + count - treeSize;
            var tailCount = tailEndIdx - tailStartIdx;

            if (promotedTailLen > 0)
            {
                // Merge: [Tree Promoted] + [Original Tail Slice]
                if (promotedTailLen + tailCount <= Constants.RRB_BRANCHING)
                {
                    finalTail = new T[promotedTailLen + tailCount];
                    Array.Copy(promotedTail, 0, finalTail, 0, promotedTailLen);
                    Array.Copy(Tail, tailStartIdx, finalTail, promotedTailLen, tailCount);
                }
                else
                {
                    // Overflow: promotedTail goes back to Tree
                    var leafWrapper = new LeafNode<T>(promotedTail, promotedTailLen, OwnerId.None);
                    newRoot = RrbAlgorithm.AppendLeafToTree(newRoot, leafWrapper, ref newShift, OwnerId.None);

                    finalTail = new T[tailCount];
                    Array.Copy(Tail, tailStartIdx, finalTail, 0, tailCount);
                }
            }
            else
            {
                // Only old tail slice
                finalTail = new T[tailCount];
                Array.Copy(Tail, tailStartIdx, finalTail, 0, tailCount);
            }
        }
        else
        {
            // Only tree promoted tail
            if (promotedTailLen == promotedTail.Length)
            {
                finalTail = promotedTail;
            }
            else
            {
                finalTail = new T[promotedTailLen];
                if (promotedTailLen > 0)
                    Array.Copy(promotedTail, 0, finalTail, 0, promotedTailLen);
            }
        }

        return new RrbList<T>(newRoot, finalTail, count, newShift, finalTail.Length);
    }

    /**
     * <summary>
     *     Merges two lists together.
     * </summary>
     * <param name="other">The list to merge with the current list.</param>
     * <param name="pure">
     *   Whether to preform a pure concatenation. This is about 50% slower, but leaves the tree
     *   in a cleaner state, where some operations may be slightly faster. Defaults to false.
     * </param>
     * <returns>A new list containing elements from both lists.</returns>
     */
    public RrbList<T> Merge(RrbList<T> other, bool pure = false)
    {
        if (other.Count == 0) return this;
        if (Count == 0) return other;

        // Merging a tree with just a tail (other.Root == null)
        // This avoids the Concat overhead for common "AppendAll" patterns.
        if (other.Root == null)
        {
            // Both tails fit in one buffer (Simple array copy)
            if (TailLen + other.TailLen <= Constants.RRB_BRANCHING)
            {
                var newItems = new T[TailLen + other.TailLen];
                Array.Copy(Tail, 0, newItems, 0, TailLen);
                Array.Copy(other.Tail, 0, newItems, TailLen, other.TailLen);

                return new RrbList<T>(Root, newItems, Count + other.Count, Shift, newItems.Length);
            }

            // Overflow. Current tail must be pushed into the tree.
            // We fill the current tail buffer to 32 using items from 'other.Tail',
            // push it into the tree, and the remainder of 'other.Tail' becomes the new tail.
            
            var spaceInThis = Constants.RRB_BRANCHING - TailLen;
            var overflow = other.TailLen - spaceInThis;

            var newLeafItems = new T[Constants.RRB_BRANCHING];
            Array.Copy(Tail, 0, newLeafItems, 0, TailLen);
            Array.Copy(other.Tail, 0, newLeafItems, TailLen, spaceInThis);

            var newLeaf = new LeafNode<T>(newLeafItems, Constants.RRB_BRANCHING, OwnerId.None);

            var newTailItems = new T[overflow];
            Array.Copy(other.Tail, spaceInThis, newTailItems, 0, overflow);

            var newShift = Shift;
            // AppendLeafToTree safely handles Root being null (promotes leaf to tree logic)
            var newRoot = RrbAlgorithm.AppendLeafToTree(Root, newLeaf, ref newShift, OwnerId.None);

            return new RrbList<T>(newRoot, newTailItems, Count + other.Count, newShift, overflow);
        }
        
        // Full Merge: Tree + Tree
        var newLeftShift = Shift;
        Node<T>? leftTree = Root;

        // If TailLen is 0, we must not push an empty leaf, as that corrupts the tree structure.
        if (TailLen > 0)
        {
            var tailAsLeaf = new LeafNode<T>(Tail, TailLen, OwnerId.None);
            leftTree = RrbAlgorithm.AppendLeafToTree(Root, tailAsLeaf, ref newLeftShift, OwnerId.None);
        }

        // Concat
        // We can safely assume leftTree is not null here because:
        // if Count > 0 and TailLen > 0, leftTree is set by AppendLeafToTree.
        // if Count > 0 and TailLen == 0, Root must be non-null (otherwise Count would be 0).
        var combinedTree = RrbAlgorithm.Concat(leftTree!, other.Root!, newLeftShift, other.Shift, out var combinedShift, !pure);

        // other.Tail is preserved as the new tail
        return new RrbList<T>(combinedTree, other.Tail, Count + other.Count, combinedShift, other.TailLen);
    }


    /**
     * <summary>
     *     Splits the list into two at the specified index.
     * </summary>
     * <param name="index">The index at which to split the list.</param>
     * <returns>A tuple containing the left and right parts of the split list.</returns>
     */
    public (RrbList<T> Left, RrbList<T> Right) Split(int index)
    {
        if (index < 0 || index > Count) throw new IndexOutOfRangeException();

        if (index == 0) return (Empty, this);
        if (index == Count) return (this, Empty);

        // Implementation in terms of Slice
        // This is safe because Slice handles:
        // 1. Tail Promotion (Left list gets a valid tail)
        // 2. Tail Extraction (Right list pulls from the old tail if needed)
        // 3. Density Preservation (Fast)

        var left = Slice(0, index);
        var right = Slice(index, Count - index);

        return (left, right);
    }

    /**
     * <summary>
     *     Inserts item at index.
     * </summary>
     * <param name="index">The zero-based index of the element to remove.</param>
     * <param name="item">The item to insert</param>
     * <returns>A new (unbalanced) list with the item inserted at index.</returns>
     */
    public RrbList<T> Insert(int index, T item)
    {
        if (index < 0 || index > Count) throw new IndexOutOfRangeException();

        // Index is at the very end: delegate to Add
        if (index == Count) return Add(item);

        var tailOffset = Count - TailLen;

        // Insert into Tail
        if (index >= tailOffset)
        {
            // Tail has room (Simple Insert)
            if (TailLen < Constants.RRB_BRANCHING)
            {
                var newTailItems = new T[TailLen + 1];
                var idxInTail = index - tailOffset;

                if (idxInTail > 0)
                    Array.Copy(Tail, 0, newTailItems, 0, idxInTail);

                newTailItems[idxInTail] = item;

                if (idxInTail < TailLen)
                    Array.Copy(Tail, idxInTail, newTailItems, idxInTail + 1, TailLen - idxInTail);

                return new RrbList<T>(Root, newTailItems, Count + 1, Shift, TailLen + 1);
            }
            // Or: Tail is Full (Split & Promote)
            else
            {
                var tempItems = new T[Constants.RRB_BRANCHING + 1];
                var idxInTail = index - tailOffset;

                // Construct the virtual 33-item array
                Array.Copy(Tail, 0, tempItems, 0, idxInTail);
                tempItems[idxInTail] = item;
                Array.Copy(Tail, idxInTail, tempItems, idxInTail + 1, TailLen - idxInTail);

                // Create the Full Node to push into tree
                var promotedItems = new T[Constants.RRB_BRANCHING];
                Array.Copy(tempItems, 0, promotedItems, 0, Constants.RRB_BRANCHING);

                // Wrap for algorithm
                var promotedLeaf = new LeafNode<T>(promotedItems, Constants.RRB_BRANCHING, OwnerId.None);

                // Create the New Tail (with the 1 remaining item)
                var newTailItems = new T[1];
                newTailItems[0] = tempItems[Constants.RRB_BRANCHING];

                // Update Tree
                var newShift = Shift;
                var newRoot = RrbAlgorithm.AppendLeafToTree(Root, promotedLeaf, ref newShift, OwnerId.None);

                return new RrbList<T>(newRoot, newTailItems, Count + 1, newShift, 1);
            }
        }

        // Insert into Tree (Recursive)
        var result = RrbAlgorithm.InsertRecursive(Root!, index, item, Shift, OwnerId.None);

        var treeRoot = result.NewNode;
        var treeShift = Shift;

        // Handle Root Overflow
        if (result.Overflow != null)
        {
            treeShift += Constants.RRB_BITS;
            var children = new[] { result.NewNode, result.Overflow };

            // Calculate sizes for the new root
            var sizes = new int[2];
            sizes[0] = RrbAlgorithm.CountTree(result.NewNode, Shift);
            sizes[1] = sizes[0] + RrbAlgorithm.CountTree(result.Overflow, Shift);

            treeRoot = new InternalNode<T>(children, sizes, 2, OwnerId.None);
        }

        return new RrbList<T>(treeRoot, Tail, Count + 1, treeShift, TailLen);
    }

    /**
     * <summary>
     *     Removes the element at the specified index.
     * </summary>
     * <param name="index">The zero-based index of the element to remove.</param>
     * <returns>A new list with the element removed.</returns>
     */
    public RrbList<T> RemoveAt(int index)
    {
        if (index < 0 || index >= Count) throw new IndexOutOfRangeException();

        // Simple case: Index is in the Tail
        var tailOffset = Count - TailLen;
        if (index >= tailOffset)
        {
            var indexInTail = index - tailOffset;

            // Create new tail array
            var newTailItems = new T[TailLen - 1];
            if (indexInTail > 0)
                Array.Copy(Tail, 0, newTailItems, 0, indexInTail);
            if (indexInTail < TailLen - 1)
                Array.Copy(Tail, indexInTail + 1, newTailItems, indexInTail, TailLen - indexInTail - 1);

            return new RrbList<T>(Root, newTailItems, Count - 1, Shift, newTailItems.Length);
        }

        // Sad case: Index is in the Tree
        // We execute a zip removal, which is always going to be faster than 
        // removing using slice, but may end up with a more unbalanced tree

        var newRoot = RrbAlgorithm.RemoveRecursive(Root!, index, Shift);
        var newShift = Shift;

        // Handle Root Collapse (if root became a single child)
        while (newRoot != null &&
               newShift > 0 &&
               newRoot is InternalNode<T> inode &&
               inode.Len == 1)
        {
            // If the root has only 1 child, that child becomes the new root
            newRoot = inode.Children[0];
            newShift -= Constants.RRB_BITS;
        }

        if (newRoot == null)
            // Tree empty, only tail remains
            return new RrbList<T>(null, Tail, TailLen, 0, TailLen);

        return new RrbList<T>(newRoot, Tail, Count - 1, newShift, TailLen);
    }

    /**
     * <summary>
     *     Returns a new, fully compacted (dense) version of this list.
     *     This operation is O(N) as it rebuilds the tree structure.
     *     Use this if the tree depth becomes excessive due to repeated relaxed operations.
     * </summary>
     */
    public RrbList<T> Compact()
    {
        if (Root == null) return this;
        var builder = new RrbBuilder<T>();
        foreach (var item in this) builder.Add(item);
        return builder.ToImmutable();
    }


    /**
     * <summary>
     *     Returns a string representation with debug information
     * </summary>
     * <returns>A string that represents the current object.</returns>
     */
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"RrbList<{typeof(T).Name}> (Cnt: {Count}, Height: {Shift / Constants.RRB_BITS})");

        if (TailLen > 0)
        {
            sb.Append("  [Tail]: ");
            PrintItems(sb, Tail, TailLen);
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("  [Tail]: <empty>");
        }

        if (Root != null)
        {
            sb.AppendLine("  [Tree Root]:");
            PrintNode(sb, Root, 1, Shift);
        }
        else
        {
            sb.AppendLine("  [Tree Root]: <null>");
        }

        return sb.ToString();
    }

    private void PrintNode(StringBuilder sb, Node<T> node, int indentLevel, int shift)
    {
        var indent = new string(' ', indentLevel * 2 + 2);

        if (node is LeafNode<T> leaf)
        {
            sb.Append($"{indent}Leaf (Len: {leaf.Len}): ");
            PrintItems(sb, leaf.Items, leaf.Len);
            sb.AppendLine();
        }
        else if (node is InternalNode<T> inode)
        {
            var tableInfo = inode.SizeTable != null
                ? $" [TABLE: {string.Join(", ", inode.SizeTable)}]"
                : " [Balanced]";

            sb.AppendLine($"{indent}Node (Len: {inode.Len}, Shift: {shift}){tableInfo}");

            for (var i = 0; i < inode.Len; i++)
                PrintNode(sb, inode.Children[i]!, indentLevel + 1, shift - Constants.RRB_BITS);
        }
    }

    private void PrintItems(StringBuilder sb, T[] items, int count)
    {
        sb.Append("[");
        var limit = Math.Min(count, 10);
        for (var i = 0; i < limit; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(items[i]);
        }

        if (count > limit) sb.Append($", ... and {count - limit} more");
        sb.Append("]");
    }


    /**
     * <summary>
     *     Removes the last element from the list.
     * </summary>
     * <returns>A new list with the last element removed.</returns>
     */
    public RrbList<T> Pop()
    {
        if (Count == 0) throw new InvalidOperationException("List is empty");

        // Fast Path: Just shrink the tail count
        if (TailLen > 1)
        {
            var newTailItems = new T[TailLen - 1];
            Array.Copy(Tail, 0, newTailItems, 0, TailLen - 1);

            return new RrbList<T>(Root, newTailItems, Count - 1, Shift, TailLen - 1);
        }

        // Slow Path: Tail becomes empty.
        // We rely on Slice to find the new tail from the tree.
        return Slice(0, Count - 1);
    }

    /**
     * <summary>
     *     Removes the first element from the list.
     * </summary>
     * <returns>A new list with the first element removed.</returns>
     */
    public RrbList<T> PopFirst()
    {
        if (Count == 0) throw new InvalidOperationException("List is empty");

        return Slice(1, Count);
    }

    /**
     * <summary>
     *     Verifies the internal structural integrity of the RRB-Tree. Throws an exception if an inconsistency is found.
     * </summary>
     */
    public void VerifyIntegrity()
    {
        if (Root == null) return;

        // 1. Traverse and verify structure/invariants
        VerifyNode(Root, Shift);

        // 2. Verify total count matches metadata
        var countedSize = CountNode(Root, Shift);
        if (countedSize != Count - TailLen)
            throw new Exception(
                $"Integrity Error: Root tracks {Count - TailLen} items, but traversal found {countedSize}.");

        if (Tail.Length < TailLen)
            throw new Exception($"Integrity Error: Tail array size {Tail.Length} < TailLen {TailLen}");
    }

    private int CountNode(Node<T> node, int shift)
    {
        if (shift == 0) return node.Len;
        var inode = (InternalNode<T>)node;
        var sum = 0;

        for (var i = 0; i < inode.Len; i++)
        {
            // SAFETY: Check for null before recursion
            var child = inode.Children[i];
            if (child == null) continue;

            sum += CountNode(child, shift - Constants.RRB_BITS);
        }

        return sum;
    }

    private void VerifyNode(Node<T> node, int shift)
    {
        if (shift == 0)
        {
            var leaf = (LeafNode<T>)node;
            // Ensure array is at least as large as Len says
            if (leaf.Items.Length < leaf.Len)
                throw new Exception($"Integrity Error: Leaf Len ({leaf.Len}) > Array Size ({leaf.Items.Length})");
            return;
        }

        var inode = (InternalNode<T>)node;
        var calculatedTotal = 0;

        for (var i = 0; i < inode.Len; i++)
        {
            var child = inode.Children[i];

            if (child == null) break;

            VerifyNode(child, shift - Constants.RRB_BITS);

            var childSize = CountNode(child, shift - Constants.RRB_BITS);
            calculatedTotal += childSize;

            // Verify SizeTable consistency if it exists
            if (inode.SizeTable != null)
            {
                if (inode.SizeTable[i] != calculatedTotal)
                    throw new Exception(
                        $"Integrity Error: SizeTable mismatch at index {i}. Table says {inode.SizeTable[i]}, actual sum is {calculatedTotal}");
            }
            else
            {
                // Verify Balanced Invariant
                // All children except the last must be full
                var capacity = 1 << shift;

                // Only enforce "Fullness" if it's not the very last logical child
                // and not a null gap.
                if (i < inode.Len - 1)
                    if (childSize != capacity)
                        throw new Exception(
                            $"Integrity Error: Balanced node has non-full child at index {i} (Size {childSize}/{capacity})");
            }
        }
    }
}