using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace RIO
{
    /// <summary>
    /// This generic class maintains a Heap, a collection where the data are organized in an unsorted tree, and the
    /// only rule is that the root element, the only one available to the user, is the least of the collection.
    /// For this reason, the data must be <see cref="IComparable"/>.
    /// The Heap has a fixed size, defined at construction time.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Heap<T> : IEnumerable<T> where T : IComparable
    {
        private readonly uint size;
        private uint count = 0;
        private readonly T[] memory;

        /// <summary>
        /// The number of elements present in the Heap.
        /// </summary>
        public uint Count => count;
        /// <summary>
        /// The average value of the elements present in the Heap.
        /// </summary>
        /// <typeparam name="T">This functionality is more restrictive: it requires the data to be also <see cref="IConvertible"/>, in order to convert them in <see cref="decimal"/>.</typeparam>
        /// <returns></returns>
        public decimal Average<T>() where T : IConvertible => memory.Take((int)count).Select(d => d.ToDecimal()).Average();
        /// <summary>
        /// It is true, when no other data can be added to the Heap.
        /// </summary>
        public bool Full => count == size;

        /// <summary>
        /// A new Heap is created of the requested size.
        /// </summary>
        /// <param name="size">The maximum number of data that can be stored in the Heap. Adding more will raise an exception. The size cannot be altered.</param>
        public Heap(uint size)
        {
            this.size = size;
            memory = new T[size];
        }

        /// <summary>
        /// This method adds a new data top the Heap. No more data than the size provided in the constructor may be added to the Heap.
        /// When <see cref="Full"/> is true, it is not possible to add new data.
        /// </summary>
        /// <param name="data"></param>
        public void Add(T data)
        {
            if (count < size)
            {
                memory[count++] = data;
                Down();
                return;
            }
            throw new IndexOutOfRangeException("Cannot add to full Heap");
        }

        /// <summary>
        /// The method retrieves the least data in the Heap. Any subsequent reading after this, will return the same value until a <see cref="Remove"/> or possibly a <see cref="Add"/>
        /// operation is perfomed onto the heap.
        /// If the Heap is empty, i.e. when <see cref="Count"/> is zero, this method will raise a <see cref="IndexOutOfRangeException"/>.
        /// </summary>
        /// <returns>The least data in the Heap.</returns>
        public T Get()
        {
            if (count < 1)
                throw new IndexOutOfRangeException("Cannot read empty Heap");
            return memory[0];
        }

        /// <summary>
        /// The method removes the least data from the Heap. <see cref="Count"/> is reduced by one.
        /// If the Heap is empty, i.e. when <see cref="Count"/> is zero, this method will raise a <see cref="IndexOutOfRangeException"/>.
        /// </summary>
        public void Remove()
        {
            if (count < 1)
                throw new IndexOutOfRangeException("Cannot read empty Heap");
            count--;
            memory[0] = memory[count];
            Up();
        }

        private void Down()
        {
            uint idx = count - 1, father = (idx - 1) / 2;
            while (idx > 0 && memory[father].CompareTo(memory[idx]) > 0)
            {
                (memory[idx], memory[father]) = (memory[father], memory[idx]);
                idx = father;
                father = (idx - 1) / 2;
            }
        }

        private void Up()
        {
            uint idx = 0;
            while (true)
            {
                uint child = 2 * idx + 1;
                if (child >= count) break;
                if (child + 1 < count && memory[child + 1].CompareTo(memory[child]) < 0)
                    child++;
                if (memory[idx].CompareTo(memory[child]) > 0)
                {
                    (memory[idx], memory[child]) = (memory[child], memory[idx]);
                    idx = child;
                }
                else break;
            }
        }

        /// <summary>
        /// It creates and returns an opaque <see cref="IEnumerator{T}"/> for the Heap.
        /// BEWARE!! Enumerating a Heap will empty it.
        /// </summary>
        /// <returns>An opaque <see cref="IEnumerator{T}"/> for this Heap.</returns>
        public IEnumerator<T> GetEnumerator()
        {
            return new Heap<T>.Enumerator<T>(this);
        }

        /// <summary>
        /// It creates and returns an opaque <see cref="IEnumerator"/> for the Heap.
        /// BEWARE!! Enumerating a Heap will empty it.
        /// </summary>
        /// <returns>An opaque <see cref="IEnumerator"/> for this Heap.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return new Heap<T>.Enumerator<T>(this);
        }

        private class Enumerator<T> : IEnumerator<T> where T : IComparable
        {
            bool started = false;
            private readonly Heap<T> heap;

            public Enumerator(Heap<T> heap)
            {
                this.heap = heap;
            }

            public T Current => heap.Get();

            object IEnumerator.Current => heap.Get();

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if (started)
                    heap.Remove();
                started = true;
                return heap.count > 0;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }
        }
    }
}
