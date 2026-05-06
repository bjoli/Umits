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


namespace Collections;

public sealed partial class RrbList<T>
{
    /**
     * <summary>
     *     Applies an accumulator function over a sequence.
     * </summary>
     * <typeparam name="TState">The type of the accumulator value.</typeparam>
     * <param name="seed">The initial accumulator value.</param>
     * <param name="func">An accumulator function to be invoked on each element with the arguments (state, value)</param>
     * <returns>The final accumulator value.</returns>
     */
    public TState Fold<TState>(TState seed, Func<TState, T, TState> func)
    {
        var state = seed;

        // Fold over the tree part
        if (Root != null) state = FoldNode(Root, Shift, state, func);

        // Fold over the tail part
        if (TailLen > 0)
        {
            var items = Tail;
            for (var i = 0; i < TailLen; i++) state = func(state, items[i]);
        }

        return state;
    }

    public T Reduce(Func<T, T, T> func)
    {
        if (this.Count == 0)
        {
            throw new InvalidOperationException("Cannot reduce an empty list.");
        }

        var state = this[0];
        
        // Start iterating from index 1. 
        // ForEach will skip the first element in O(log N) time.
        this.ForEach(item => state = func(state, item), index: 1);
        
        return state;
    }

    private TState FoldNode<TState>(Node<T> node, int shift, TState state, Func<TState, T, TState> func)
    {
        // Base case: We are at a leaf node
        if (shift == 0)
        {
            var leaf = (LeafNode<T>)node;
            var items = leaf.Items;
            var len = leaf.Len;

            // Iterate directly over the array - extremely fast
            for (var i = 0; i < len; i++) state = func(state, items[i]);
            return state;
        }

        // Recursive step: We are at an internal node
        var internalNode = (InternalNode<T>)node;
        var lenInternal = internalNode.Len;

        for (var i = 0; i < lenInternal; i++)
            // Recurse down to children
            state = FoldNode(internalNode.Children[i]!, shift - Constants.RRB_BITS, state, func);

        return state;
    }

    
    /**
     * <summary>
     *     High-performance internal iterator.
     *     Returns false if the iteration was terminated early by the predicate.
     * </summary>
     * <param name="action">A function that returns true to continue, or false to break.</param>
     * <example>
     *     list.Iter(item => {
     *     if (item > 100)
     *     {
     *     Console.WriteLine("Found it!");
     *     return false; // Break
     *     }
     *     return true; // Continue
     *     });
     * </example>
     */
    public bool Iter(Func<T, bool> action)
    {
        // 1. Iterate Tree
        if (Root != null)
            // If the tree part returns false, we stop immediately and return false.
            if (!IterNode(Root, Shift, action))
                return false;

        // 2. Iterate Tail
        if (TailLen > 0)
        {
            var items = Tail;
            // Hoist the length check for speed
            var len = TailLen;
            for (var i = 0; i < len; i++)
                if (!action(items[i]))
                    return false;
        }

        return true; // Completed successfully
    }

    private static bool IterNode(Node<T> node, int shift, Func<T, bool> action)
    {
        // Base Case: Leaf Node
        // We iterate the array directly. This is the "hot loop" that beats IEnumerator.
        if (shift == 0)
        {
            var leaf = RrbAlgorithm.AsLeaf(node);
            var items = leaf.Items;
            int len = leaf.Len;

            for (var i = 0; i < len; i++)
                if (!action(items[i]))
                    return false;
            return true;
        }

        // Recursive Step: Internal Node
        var internalNode = RrbAlgorithm.AsInternal(node);
        var children = internalNode.Children;
        int childCount = internalNode.Len;
        var nextShift = shift - Constants.RRB_BITS;

        for (var i = 0; i < childCount; i++)
            // Recurse. If child returns false (break), we propagate it up.
            if (!IterNode(children[i]!, nextShift, action))
                return false;

        return true;
    }

    /**
     * <summary>
     *     Performs the specified action on each element of the list within the range.
     * </summary>
     * <param name="action">The delegate to perform on each element.</param>
     * <param name="index">The zero-based starting index (default 0).</param>
     * <param name="count">The number of elements to process.(default -1 means "until the end").</param>
     */
    public void ForEach(Action<T> action, int index = 0, int count = -1)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));

        // Handle default "to the end"
        if (count == -1) count = Count - index;

        if (count < 0 || index + count > Count) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0) return;

        // Traverse the Tree (Root)
        var treeSize = Count - TailLen;

        // Check if our range overlaps with the tree
        if (index < treeSize && Root != null)
        {
            var takeFromTree = Math.Min(count, treeSize - index);
            ForEachNode(Root, Shift, index, takeFromTree, action);

            // Adjust remaining count and index for the tail
            count -= takeFromTree;
            index = 0; // We have consumed the offset inside the tree
        }
        else
        {
            // Entire range is in the tail, just adjust the index relative to tail start
            index -= treeSize;
        }

        // Traverse the Tail
        if (count > 0 && TailLen > 0)
        {
            var items = Tail;

            var end = index + count;
            for (var i = index; i < end; i++) action(items[i]);
        }
    }

    private void ForEachNode(Node<T> node, int shift, int offset, int count, Action<T> action)
    {
        // Base case: Leaf Node
        if (shift == 0)
        {
            var leaf = RrbAlgorithm.AsLeaf(node);
            var items = leaf.Items;
            var end = offset + count;
            for (var i = offset; i < end; i++) action(items[i]);
            return;
        }

        // Internal Node
        var internalNode = RrbAlgorithm.AsInternal(node);
        var childShift = shift - Constants.RRB_BITS;

        // Fast path for Dense nodes
        if (internalNode.SizeTable == null)
        {
            var blockSize = 1 << (childShift + Constants.RRB_BITS);

            for (var i = 0; i < internalNode.Len; i++)
            {
                if (count <= 0) break;

                // If last child, we assume it contains everything else we need. 
                // (Since we validated total bounds at the entry point).
                // Otherwise, it's a full block.
                var currentSize = i == internalNode.Len - 1 ? int.MaxValue : blockSize;

                // Skip child entirely if offset is beyond it
                if (offset >= currentSize)
                {
                    offset -= currentSize;
                }
                else
                {
                    // Overlap: We need to process this child
                    // We take either all that is requested, or all that holds in this child
                    var amount = Math.Min(count, currentSize - offset);

                    ForEachNode(internalNode.Children[i]!, childShift, offset, amount, action);

                    count -= amount;
                    offset = 0; // After the first partial child, subsequent children start at 0
                }
            }
        }
        else
        {
            // Slow path for Relaxed nodes (using SizeTable)
            var prevTotal = 0;
            for (var i = 0; i < internalNode.Len; i++)
            {
                if (count <= 0) break;

                var currentTotal = internalNode.SizeTable[i];
                var currentSize = currentTotal - prevTotal;

                if (offset >= currentSize)
                {
                    offset -= currentSize;
                }
                else
                {
                    var amount = Math.Min(count, currentSize - offset);

                    ForEachNode(internalNode.Children[i]!, childShift, offset, amount, action);

                    count -= amount;
                    offset = 0;
                }

                prevTotal = currentTotal;
            }
        }
    }

    /**
     * <summary>
     * Runs mapper on each value in the RrbList and returns a new list with all the mapped values
     * </summary>
     * <typeparam name="TResult">The type of the value returned by mapper.</typeparam>
     * <param name="mapper">A transform function to apply to each element.</param>
     * <returns>A new RrbList containing the mapped elements.</returns>
     */
    public RrbList<TResult> Map<TResult>(Func<T, TResult> mapper)
    {
        if (mapper == null) throw new ArgumentNullException(nameof(mapper));
        if (Count == 0) return RrbList<TResult>.Empty;

        // 1. Map the Tree
        Node<TResult>? newRoot = null;
        if (Root != null)
        {
            newRoot = MapNode(Root, Shift, mapper);
        }

        // 2. Map the Tail
        TResult[] newTail = Array.Empty<TResult>();
        if (TailLen > 0)
        {
            newTail = new TResult[TailLen];
            for (var i = 0; i < TailLen; i++)
            {
                newTail[i] = mapper(Tail[i]);
            }
        }

        return new RrbList<TResult>(newRoot, newTail, Count, Shift, TailLen);
    }

    // This function copies it's way down to the leaf nodes and then runs a copy of the 
    // leaf with mapper run on every element. This is by far the fastest way to run a function on each 
    // element of the RrbList.
    private static Node<TResult> MapNode<TResult>(Node<T> node, int shift, Func<T, TResult> mapper)
    {
        // Base case: Map values in the leaf
        if (shift == 0)
        {
            var leaf = RrbAlgorithm.AsLeaf(node);
            var newItems = new TResult[leaf.Len];
            
            for (var i = 0; i < leaf.Len; i++)
            {
                newItems[i] = mapper(leaf.Items[i]);
            }
            
            return new LeafNode<TResult>(newItems, leaf.Len, OwnerId.None);
        }

        // Recursive step: Map internal nodes
        var internalNode = RrbAlgorithm.AsInternal(node);
        var newChildren = new Node<TResult>?[internalNode.Len];

        for (var i = 0; i < internalNode.Len; i++)
        {
            newChildren[i] = MapNode(internalNode.Children[i]!, shift - Constants.RRB_BITS, mapper);
        }

        // Because the structural layout is identical, 
        // we can safely pass the exact same SizeTable reference without allocating a new one.
        return new InternalNode<TResult>(newChildren, internalNode.SizeTable, internalNode.Len, OwnerId.None);
    }
    
    
    /**
     * <summary>
     * Filters the elements of the list based on a predicate.
     * </summary>
     * <param name="filter">A function to test each element for a condition.</param>
     * <returns>A new RrbList containing the elements that satisfy the condition.</returns>
     */
    public RrbList<T> Filter(Func<T, bool> filter)
    {
        var builder = new RrbBuilder<T>();
        this.ForEach(elem =>
        {
            if (filter(elem))
            {
                builder.Add(elem);
            }
        });
        return builder.ToImmutable();
    }

    public T? Find(Func<T, bool> predicate)
    {
        T? res = default(T);
        Iter(elem =>
        {
            if (predicate(elem))
            {
                res = elem;
                return false;
            }

            return true;
        });

        return res;
    }
}