/********************************************************
*                                                        *
*   © Copyright (C) Microsoft. All rights reserved.      *
*                                                        *
*********************************************************/

namespace Microsoft.VisualStudio.Threading {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;
	using System.Threading;
	using System.Threading.Tasks;

	// TODO: consider deriving from AsyncQueue<T> or otherwise making this look and behave
	// almost identically to that one, including all our tests running against both.
	public class AsyncPrioritizedQueue<T, TPriority> {
		private readonly IComparer<TPriority> priorityComparer;

		/// <summary>
		/// Initializes a new instance of the <see cref="AsyncPrioritizedQueue{T, TPriority}"/> class
		/// that uses the default comparer for <typeparamref name="TPriority"/>.
		/// </summary>
		public AsyncPrioritizedQueue()
			: this(Comparer<TPriority>.Default) {
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="AsyncPrioritizedQueue{T, TPriority}"/> class
		/// that uses the specified comparer for <typeparamref name="TPriority"/>.
		/// </summary>
		/// <param name="priorityComparer">The comparer to use for sorting priorities.</param>
		public AsyncPrioritizedQueue(IComparer<TPriority> priorityComparer) {
			Requires.NotNull(priorityComparer, nameof(priorityComparer));
			this.priorityComparer = priorityComparer;
		}

		/// <summary>
		/// Gets a value indicating whether the queue is currently empty.
		/// </summary>
		public bool IsEmpty => true;

		/// <summary>
		/// Gets a value indicating whether the queue has completed.
		/// </summary>
		/// <remarks>
		/// This is arguably redundant with <see cref="Completion"/>.IsCompleted, but this property
		/// won't cause the lazy instantiation of the Task that <see cref="Completion"/> may if there
		/// is no other reason for the Task to exist.
		/// </remarks>
		public bool IsCompleted { get; } = false;

		/// <summary>
		/// Gets a task that transitions to a completed state when <see cref="Complete"/> is called.
		/// </summary>
		public Task Completion { get; } = null;

		/// <summary>
		/// Adds an element to the tail of the queue.
		/// </summary>
		/// <param name="value">The value to add.</param>
		/// <param name="priority">The priority to associate with this the <paramref name="value"/>.</param>
		public void Enqueue(T value, TPriority priority) {
			throw new NotImplementedException();
		}

		/// <summary>
		/// Gets a task whose result is the element at the head of the queue.
		/// </summary>
		/// <param name="cancellationToken">
		/// A token whose cancellation signals lost interest in the item.
		/// Cancelling this token does *not* guarantee that the task will be canceled
		/// before it is assigned a resulting element from the head of the queue.
		/// It is the responsibility of the caller to ensure after cancellation that 
		/// either the task is canceled, or it has a result which the caller is responsible
		/// for then handling.
		/// </param>
		/// <returns>A task whose result is the head element.</returns>
		public Task<T> DequeueAsync(CancellationToken cancellationToken = default(CancellationToken)) {
			throw new NotImplementedException();
		}

		/// <summary>
		/// Signals that no further elements will be enqueued.
		/// </summary>
		public void Complete() {
		}
	}
}
