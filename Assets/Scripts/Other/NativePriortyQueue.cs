using System;
using Unity.Collections;
using Unity.Mathematics;

public struct NodeWithPriority : System.IComparable<NodeWithPriority>, IEquatable<NodeWithPriority>
{
    private float epsilon;
    public int2 Position;
    public float Priority;     // The fCost or priority value

    public NodeWithPriority(int2 pos, int priority)
    {
        epsilon = 0.0001f;
        Position = pos;
        Priority = priority;
    }

    public NodeWithPriority(int2 pos, float priority)
    {
        epsilon = 0.0001f;
        Position = pos;
        Priority = priority;
    }

    public int CompareTo(NodeWithPriority other)
    {
        if (math.abs(Priority - other.Priority) < epsilon)
        {
            // If the priorities are effectively equal within the epsilon, consider them the same
            return 0;
        }
        // Otherwise, compare as usual
        return Priority < other.Priority ? -1 : 1;
    }

    // Implement Equals for value comparison
    public bool Equals(NodeWithPriority other)
    {
        return math.all(Position == other.Position) && math.abs(Priority - other.Priority) <= epsilon;
    }

    // Override GetHashCode for hash-based collections
    public override int GetHashCode()
    {
        unchecked
        {
            return (Position.GetHashCode() * 397) ^ Priority.GetHashCode();
        }
    }
}

public struct NativePriorityQueue
{
    private NativeList<NodeWithPriority> heap;
    private bool isMinHeap;

    public NativePriorityQueue(Allocator allocator, bool isMinHeap = true)
    {
        heap = new NativeList<NodeWithPriority>(allocator);
        this.isMinHeap = isMinHeap;
    }

    public int Count => heap.Length;

    public bool IsEmpty => heap.Length == 0;

    public void Enqueue(NodeWithPriority value)
    {
        heap.Add(value);
        SiftUp(heap.Length - 1);
    }

    public NodeWithPriority Dequeue()
    {
        NodeWithPriority root = heap[0];
        heap[0] = heap[heap.Length - 1];
        heap.RemoveAt(heap.Length - 1);
        SiftDown(0);
        return root;
    }

    public bool Contains(NodeWithPriority value)
    {
        for (int i = 0; i < heap.Length; i++)
        {
            if (heap[i].Equals(value))
                return true;
        }
        return false;
    }

    public NodeWithPriority Peek()
    {
        return heap[0];
    }

    public void Dispose()
    {
     if (heap.IsCreated) heap.Dispose();
    }

    private void SiftUp(int index)
    {
        while (index > 0)
        {
            int parentIndex = (index - 1) / 2;

            if (Compare(heap[index], heap[parentIndex]) >= 0)
                break;

            // Swap
            NodeWithPriority temp = heap[index];
            heap[index] = heap[parentIndex];
            heap[parentIndex] = temp;

            index = parentIndex;
        }
    }

    private void SiftDown(int index)
    {
        int lastIndex = heap.Length - 1;
        while (true)
        {
            int leftChild = index * 2 + 1;
            int rightChild = index * 2 + 2;
            int smallestOrLargest = index;

            if (leftChild <= lastIndex && Compare(heap[leftChild], heap[smallestOrLargest]) < 0)
                smallestOrLargest = leftChild;

            if (rightChild <= lastIndex && Compare(heap[rightChild], heap[smallestOrLargest]) < 0)
                smallestOrLargest = rightChild;

            if (smallestOrLargest == index)
                break;

            // Swap
            NodeWithPriority temp = heap[index];
            heap[index] = heap[smallestOrLargest];
            heap[smallestOrLargest] = temp;

            index = smallestOrLargest;
        }
    }

    private int Compare(NodeWithPriority a, NodeWithPriority b)
    {
        return isMinHeap ? a.CompareTo(b) : b.CompareTo(a);
    }
}