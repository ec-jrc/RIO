using System;
using System.Collections;
using System.Collections.Generic;

namespace JetBlack.Core.Collections.Generic
{
	/// <summary>
	/// Defines the programming interface for a generic collection that behaves as a circular buffer, removing automatically
	/// the oldest elements, when the <see cref="Capacity"/> is reached.
	/// </summary>
	/// <typeparam name="T">The buffer may handle every type of data opaquely.</typeparam>
	public interface ICircularBuffer<T>
	{
		/// <summary>
		/// Number of elements stored in the buffer.
		/// </summary>
		int Count { get; }
		/// <summary>
		/// Maximum number of elements that can be stored in the buffer. Every new element added will pop out the oldest one.
		/// </summary>
		int Capacity { get; set; }
		/// <summary>
		/// Adds a new element to the buffer, returning the oldest element previously in the buffer.
		/// </summary>
		/// <param name="item">The element to be added to the buffer</param>
		/// <returns></returns>
		T Enqueue(T item);
		/// <summary>
		/// Removes the oldest element in the buffer and returns it.
		/// </summary>
		/// <returns>The element that was inserted before all other present in the buffer.</returns>
		T Dequeue();
		/// <summary>
		/// Empties the buffer.
		/// </summary>
		void Clear();
		/// <summary>
		/// Returns the element at position <paramref name="index"/>, leaving it in the collection.
		/// </summary>
		/// <param name="index">This index refers to the present configuration of the collection, not to its internal
		/// organization, coherently with <see cref="IndexOf(T)"/>.</param>
		/// <returns></returns>
		T this[int index] { get; set; }
		/// <summary>
		/// Looks for <paramref name="item"/> in the collection and returns its index related to the present organization
		/// of the collection, coherently with <see cref="this[int]"/>
		/// </summary>
		/// <param name="item"></param>
		/// <returns>The index of the found item, -1 otherwise.</returns>
		int IndexOf(T item);
		/// <summary>
		/// Adds an element to the collection, rearranging the collection internally: the oldest element pops out.
		/// </summary>
		/// <param name="index">The position where to insert the item.</param>
		/// <param name="item">The item to be added to the collection.</param>
		void Insert(int index, T item);
		/// <summary>
		/// Removes an element from the collection, rearranging the collection internally: an empty location will be vacant.
		/// </summary>
		/// <param name="index">The position of the item to remove.</param>
		void RemoveAt(int index);
	}

	/// <summary>
	/// Implementation of the <see cref="ICircularBuffer{T}"/> interface.
	/// </summary>
	/// <typeparam name="T">The type of the elements the collection will store.</typeparam>
	public class CircularBuffer<T> : ICircularBuffer<T>, IEnumerable<T>
	{
		private T[] _buffer;
		private int _head;
		private int _tail;

		/// <summary>
		/// Initializes a new collection of the requested capacity: the collection will contain no more than this number
		/// of items.
		/// </summary>
		/// <param name="capacity">Size of the collection</param>
		public CircularBuffer(int capacity)
		{
			if (capacity < 0)
				throw new ArgumentOutOfRangeException("capacity", "must be positive");
			_buffer = new T[capacity];
			_head = capacity - 1;
		}
		/// <summary>
		/// Number of elements stored in the buffer.
		/// </summary>
		public int Count { get; private set; }
		/// <summary>
		/// Maximum number of elements that can be stored in the buffer. Every new element added will pop out the oldest one.
		/// </summary>
		public int Capacity
		{
			get { return _buffer.Length; }
			set
			{
				if (value < 0)
					throw new ArgumentOutOfRangeException("value", "must be positive");

				if (value == _buffer.Length)
					return;

				var buffer = new T[value];
				var count = 0;
				while (Count > 0 && count < value)
					buffer[count++] = Dequeue();

				_buffer = buffer;
				Count = count;
				_head = count - 1;
				_tail = 0;
			}
		}
		/// <summary>
		/// Adds a new element to the buffer, returning the oldest element previously in the buffer.
		/// </summary>
		/// <param name="item">The element to be added to the buffer</param>
		/// <returns></returns>
		public T Enqueue(T item)
		{
			_head = (_head + 1) % Capacity;
			var overwritten = _buffer[_head];
			_buffer[_head] = item;
			if (Count == Capacity)
				_tail = (_tail + 1) % Capacity;
			else
				++Count;
			return overwritten;
		}
		/// <summary>
		/// Removes the oldest element in the buffer and returns it.
		/// </summary>
		/// <returns>The element that was inserted before all other present in the buffer.</returns>
		public T Dequeue()
		{
			if (Count == 0)
				throw new InvalidOperationException("queue exhausted");

			var dequeued = _buffer[_tail];
			_buffer[_tail] = default(T);
			_tail = (_tail + 1) % Capacity;
			--Count;
			return dequeued;
		}
		/// <summary>
		/// Empties the buffer.
		/// </summary>
		public void Clear()
		{
			_head = Capacity - 1;
			_tail = 0;
			Count = 0;
		}
		/// <summary>
		/// Returns the element at position <paramref name="index"/>, leaving it in the collection.
		/// </summary>
		/// <param name="index">This index refers to the present configuration of the collection, not to its internal
		/// organization, coherently with <see cref="IndexOf(T)"/>.</param>
		/// <returns></returns>
		public T this[int index]
		{
			get
			{
				if (index < 0 || index >= Count)
					throw new ArgumentOutOfRangeException("index");

				return _buffer[(_tail + index) % Capacity];
			}
			set
			{
				if (index < 0 || index >= Count)
					throw new ArgumentOutOfRangeException("index");

				_buffer[(_tail + index) % Capacity] = value;
			}
		}
		/// <summary>
		/// Looks for <paramref name="item"/> in the collection and returns its index related to the present organization
		/// of the collection, coherently with <see cref="this[int]"/>
		/// </summary>
		/// <param name="item"></param>
		/// <returns>The index of the found item, -1 otherwise.</returns>
		public int IndexOf(T item)
		{
			for (var i = 0; i < Count; ++i)
				if (Equals(item, this[i]))
					return i;
			return -1;
		}
		/// <summary>
		/// Adds an element to the collection, rearranging the collection internally: the oldest element pops out.
		/// </summary>
		/// <param name="index">The position where to insert the item.</param>
		/// <param name="item">The item to be added to the collection.</param>
		public void Insert(int index, T item)
		{
			if (index < 0 || index > Count)
				throw new ArgumentOutOfRangeException("index");

			if (Count == index)
				Enqueue(item);
			else
			{
				var last = this[Count - 1];
				for (var i = index; i < Count - 2; ++i)
					this[i + 1] = this[i];
				this[index] = item;
				Enqueue(last);
			}
		}
		/// <summary>
		/// Removes an element from the collection, rearranging the collection internally: an empty location will be vacant.
		/// </summary>
		/// <param name="index">The position of the item to remove.</param>
		public void RemoveAt(int index)
		{
			if (index < 0 || index >= Count)
				throw new ArgumentOutOfRangeException("index");

			for (var i = index; i > 0; --i)
				this[i] = this[i - 1];
			Dequeue();
		}
		/// <summary>
		/// Provides the <see cref="IEnumerable{T}"/> functionality
		/// </summary>
		/// <returns></returns>
		public IEnumerator<T> GetEnumerator()
		{
			if (Count == 0 || Capacity == 0)
				yield break;

			for (var i = 0; i < Count; ++i)
				yield return this[i];
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}
}