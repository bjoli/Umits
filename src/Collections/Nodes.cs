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


using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Collections;

// Layout: 
// [Header: 16B] + [Ref: 8B] + [Owner: 6B] + [Len: 1B] + [Flags: 1B] = 32 Bytes
[StructLayout(LayoutKind.Auto, Pack = 1)]
internal abstract class Node<T>
{
    // Note: The specific "Children" or "Items" reference is defined in subclasses,
    // but the CLR generally packs references first. 
    // The fields below consume exactly 8 bytes.

    public OwnerId Owner; // 6 bytes
    public byte Len; // 1 byte
    public NodeFlags Flags; // 1 byte

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsLeaf()
    {
        return (Flags & NodeFlags.IsLeaf) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsRelaxed()
    {
        return (Flags & NodeFlags.IsRelaxed) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsDense()
    {
        return (Flags & NodeFlags.IsRelaxed) == 0;
    }
}

[StructLayout(LayoutKind.Auto, Pack = 1)]
internal sealed class LeafNode<T> : Node<T>
{
    public static readonly LeafNode<T> Empty = new(0, OwnerId.None);
    public readonly T[] Items;

    public LeafNode(int size, OwnerId owner)
    {
        Len = (byte)size;
        Owner = owner;
        Flags = NodeFlags.IsLeaf;
        // If owner is valid (Transient), allocate full capacity for cheap appends.
        Items = new T[!owner.IsNone ? Constants.RRB_BRANCHING : size];
    }

    public LeafNode(T[] items, int len, OwnerId owner)
    {
        Items = items;
        Len = (byte)len;
        Owner = owner;
        Flags = NodeFlags.IsLeaf;
    }

    // Corresponds to 'ensure_leaf_editable' in rrb_transients.h
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LeafNode<T> EnsureEditable(OwnerId callerId)
    {
        // If we own it, we can mutate it in place.
        if (Owner == callerId) return this;

        // Clone
        var newCap = !callerId.IsNone ? Constants.RRB_BRANCHING : Len;
        var newItems = new T[newCap];

        // If growing (e.g. persistent node becoming transient), we copy what we have.
        // If shrinking (e.g. transient node becoming persistent), we copy what fits.
        Array.Copy(Items, 0, newItems, 0, Len);

        return new LeafNode<T>(newItems, Len, callerId);
    }

    public LeafNode<T> Freeze(OwnerId callerId)
    {
        // 1. Already Immutable (None) OR Effectively Immutable (Older generation)
        if (Owner.IsNone)
            // If the size is correct, we don't need to do anything.
            if (Items.Length == Len)
                return this;

        // 2. In-Place Freeze (It's OUR node)
        if (Owner == callerId)
            if (Items.Length == Len)
            {
                // Zero allocation freeze
                Owner = OwnerId.None;
                Flags |= NodeFlags.IsFrozen;
                return this;
            }

        // 3. Must Copy (Shrinking required or alien owner)
        var newItems = new T[Len];
        Array.Copy(Items, newItems, Len);
        return new LeafNode<T>(newItems, Len, OwnerId.None);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LeafNode<T> CloneAndSet(int index, T value)
    {
        var newItems = new T[Len];
        Array.Copy(Items, newItems, Len);
        newItems[index] = value;
        return new LeafNode<T>(newItems, Len, OwnerId.None);
    }
}

[StructLayout(LayoutKind.Auto, Pack = 1)]
internal sealed class InternalNode<T> : Node<T>
{
    public readonly Node<T>?[] Children; // Reference (8 bytes)
    public readonly int[]? SizeTable; // Reference (8 bytes) -> Pushes this node to 40 bytes if present

    public InternalNode(int size, OwnerId owner)
    {
        Len = (byte)size;
        Owner = owner;
        // Flags defaults to 0 (Internal, Dense)
        Children = new Node<T>?[!owner.IsNone ? Constants.RRB_BRANCHING : size];
    }

    public InternalNode(Node<T>?[] children, int[]? sizeTable, int len, OwnerId owner)
    {
        Children = children;
        SizeTable = sizeTable;
        Len = (byte)len;
        Owner = owner;

        if (sizeTable != null) Flags |= NodeFlags.IsRelaxed;
    }

    // 'ensure_internal_editable' in c-rrb in rrb_transients.h
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public InternalNode<T> EnsureEditable(OwnerId token, bool expand = false)
    {
        // 1. MATCH: Already mutable and owned by us.
        // INVARIANT: If Owner is set, We know capacity is 32. Party all night
        if (token == Owner && token != OwnerId.None)
            return this;

        // 2. TO TRANSIENT: Target is mutable (token != None).
        // We must expand to full 32 capacity to support future appends without resizing.
        if (token != OwnerId.None)
        {
            var newChildren = new Node<T>?[Constants.RRB_BRANCHING];
            Array.Copy(Children, newChildren, Len);

            int[]? newTable = null;
            if (SizeTable != null)
            {
                newTable = new int[Constants.RRB_BRANCHING];
                Array.Copy(SizeTable, newTable, Len);
            }

            return new InternalNode<T>(newChildren, newTable, Len, token);
        }

        // 3. TO PERSISTENT: Target is immutable (token == None).
        // We strictly fit the size (Current + 1 if expanding).
        {
            var newCap = expand ? Len + 1 : Len;
            var newChildren = new Node<T>?[newCap];
            Array.Copy(Children, newChildren, Len);

            int[]? newTable = null;
            if (SizeTable != null)
            {
                newTable = new int[newCap];
                Array.Copy(SizeTable, newTable, Len);
            }

            return new InternalNode<T>(newChildren, newTable, Len, token);
        }
    }


    public InternalNode<T> Freeze(OwnerId callerId)
    {
        // 1. Effectively Immutable check
        // If the node belongs to an older build, we treat it as frozen.
        if (Owner.IsNone)
            if (Children.Length == Len)
                return this;

        // 2. In-Place Freeze
        if (Owner == callerId)
            if (Children.Length == Len)
            {
                Owner = OwnerId.None;
                Flags |= NodeFlags.IsFrozen;
                return this;
            }

        // 3. Copy/Shrink
        var newChildren = new Node<T>?[Len];
        Array.Copy(Children, newChildren, Len);

        // Shrink SizeTable
        int[]? newTable = null;
        if (SizeTable != null)
        {
            newTable = new int[Len];
            Array.Copy(SizeTable, newTable, Len);
        }

        return new InternalNode<T>(newChildren, newTable, Len, OwnerId.None);
    }

    // Clone and replace a single child (Path Copying)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public InternalNode<T> CloneAndSetChild(int childIdx, Node<T> newChild)
    {
        var newChildren = new Node<T>?[Len];
        Array.Copy(Children, newChildren, Len);
        newChildren[childIdx] = newChild;
        return new InternalNode<T>(newChildren, SizeTable, Len, OwnerId.None);
    }
}