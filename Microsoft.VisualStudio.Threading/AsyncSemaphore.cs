namespace Microsoft.VisualStudio.Threading {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	/// An asynchronous <see cref="SemaphoreSlim"/> like class with more convenient release syntax.
	/// </summary>
	public class AsyncSemaphore {
		/// <summary>
		/// The semaphore used to keep concurrent access to this lock to just 1.
		/// </summary>
		private readonly SemaphoreSlim semaphore;

		/// <summary>
		/// A task to return for any uncontested request for the lock.
		/// </summary>
		private readonly Task<Releaser> uncontestedReleaser;

		/// <summary>
		/// A task that is cancelled.
		/// </summary>
		private readonly Task<Releaser> canceledReleaser;

		/// <summary>
		/// The <see cref="JoinableTaskContext"/> to use to avoid deadlocks with the main thread.
		/// </summary>
		private readonly JoinableTaskContext joinableTaskContext;

		/// <summary>
		/// The collection of JoinableTasks that hold the semaphore.
		/// Anyone waiting on the semaphore should Join this.
		/// </summary>
		private readonly JoinableTaskCollection semaphoreHolders;

		/// <summary>
		/// Initializes a new instance of the <see cref="AsyncSemaphore"/> class.
		/// </summary>
		/// <param name="initialCount">The initial number of requests for the semaphore that can be granted concurrently.</param>
		public AsyncSemaphore(int initialCount) {
			this.semaphore = new SemaphoreSlim(initialCount);
			this.uncontestedReleaser = Task.FromResult(new Releaser(this, null));

			var canceledSource = new TaskCompletionSource<Releaser>();
			canceledSource.SetCanceled();
			this.canceledReleaser = canceledSource.Task;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="AsyncSemaphore"/> class.
		/// </summary>
		/// <param name="initialCount">The initial number of requests for the semaphore that can be granted concurrently.</param>
		/// <param name="joinableTaskContext">The joinable task context to use to avoid deadlocks with the main thread.</param>
		public AsyncSemaphore(int initialCount, JoinableTaskContext joinableTaskContext)
			: this(initialCount) {
			Requires.NotNull(joinableTaskContext, "joinableTaskContext");
			this.joinableTaskContext = joinableTaskContext;
		}

		/// <summary>
		/// Requests access to the lock.
		/// </summary>
		/// <param name="cancellationToken">A token whose cancellation signals lost interest in the lock.</param>
		/// <returns>A task whose result is a releaser that should be disposed to release the lock.</returns>
		public Task<Releaser> EnterAsync(CancellationToken cancellationToken = default(CancellationToken)) {
			return this.LockWaitingHelper(this.semaphore.WaitAsync(cancellationToken));
		}

		/// <summary>
		/// Requests access to the lock.
		/// </summary>
		/// <param name="timeout">A timeout for waiting for the lock.</param>
		/// <param name="cancellationToken">A token whose cancellation signals lost interest in the lock.</param>
		/// <returns>A task whose result is a releaser that should be disposed to release the lock.</returns>
		public Task<Releaser> EnterAsync(TimeSpan timeout, CancellationToken cancellationToken = default(CancellationToken)) {
			return this.LockWaitingHelper(this.semaphore.WaitAsync(timeout, cancellationToken));
		}

		/// <summary>
		/// Requests access to the lock.
		/// </summary>
		/// <param name="timeout">A timeout for waiting for the lock (in milliseconds).</param>
		/// <param name="cancellationToken">A token whose cancellation signals lost interest in the lock.</param>
		/// <returns>A task whose result is a releaser that should be disposed to release the lock.</returns>
		public Task<Releaser> EnterAsync(int timeout, CancellationToken cancellationToken = default(CancellationToken)) {
			return this.LockWaitingHelper(this.semaphore.WaitAsync(timeout, cancellationToken));
		}

		/// <summary>
		/// Requests access to the lock.
		/// </summary>
		/// <param name="waitTask">A task that represents a request for the semaphore.</param>
		/// <returns>A task whose result is a releaser that should be disposed to release the lock.</returns>
		private Task<Releaser> LockWaitingHelper(Task waitTask) {
			Requires.NotNull(waitTask, "waitTask");

			var timeoutWaitTask = waitTask as Task<bool>;
			if (this.joinableTaskContext != null) {
				var ambientTask = this.joinableTaskContext.AmbientTask;
				this.semaphoreHolders.Add(ambientTask);

				// As a semaphore awaiter, we must Join the semaphore holders till we get the semaphore.
				var joinRelease = !waitTask.IsCompleted ? this.semaphoreHolders.Join() : new JoinableTaskCollection.JoinRelease();
				return waitTask.ContinueWith(
					(waiter, state) => {
						joinRelease.Dispose();
						if (waiter.IsCanceled) {
							throw new TaskCanceledException();
						}

						var timeoutWaiter = waiter as Task<bool>;
						if (timeoutWaiter != null && !timeoutWaiter.Result) {
							throw new TaskCanceledException();
						}

						return new Releaser((AsyncSemaphore)state, ambientTask);
					},
					this,
					CancellationToken.None,
					TaskContinuationOptions.ExecuteSynchronously,
					TaskScheduler.Default);
			} else if (waitTask.IsCompleted && (timeoutWaitTask == null || timeoutWaitTask.Result)) {
				return this.uncontestedReleaser; // uncontested lock.
			} else {
				return waitTask.ContinueWith(
					(waiter, state) => {
						if (waiter.IsCanceled) {
							throw new TaskCanceledException();
						}

						var timeoutWaiter = waiter as Task<bool>;
						if (timeoutWaiter != null && !timeoutWaiter.Result) {
							throw new TaskCanceledException();
						}

						return new Releaser((AsyncSemaphore)state, null);
					},
					this,
					CancellationToken.None,
					TaskContinuationOptions.ExecuteSynchronously,
					TaskScheduler.Default);
			}
		}

		/// <summary>
		/// A value whose disposal triggers the release of a lock.
		/// </summary>
		public struct Releaser : IDisposable {
			/// <summary>
			/// The lock instance to release.
			/// </summary>
			private readonly AsyncSemaphore toRelease;

			/// <summary>
			/// The task that holds the semaphore;
			/// </summary>
			private readonly JoinableTask joinableTask;

			/// <summary>
			/// Initializes a new instance of the <see cref="Releaser"/> struct.
			/// </summary>
			/// <param name="toRelease">The lock instance to release on.</param>
			/// <param name="joinableTask">The joinable task that has entered the semaphore.</param>
			internal Releaser(AsyncSemaphore toRelease, JoinableTask joinableTask) {
				this.toRelease = toRelease;
				this.joinableTask = joinableTask;
			}

			/// <summary>
			/// Releases the lock.
			/// </summary>
			public void Dispose() {
				if (this.toRelease != null) {
					this.toRelease.semaphore.Release();
					if (this.joinableTask != null && this.toRelease.semaphoreHolders != null) {
						this.toRelease.semaphoreHolders.Remove(this.joinableTask);
					}
				}
			}
		}
	}
}
