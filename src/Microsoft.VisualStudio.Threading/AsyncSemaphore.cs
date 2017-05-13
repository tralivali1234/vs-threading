/********************************************************
*                                                        *
*   © Copyright (C) Microsoft. All rights reserved.      *
*                                                        *
*********************************************************/

namespace Microsoft.VisualStudio.Threading
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// An asynchronous <see cref="SemaphoreSlim"/> like class with more convenient release syntax.
    /// </summary>
    public class AsyncSemaphore : IDisposable
    {
        /// <summary>
        /// A task that is canceled.
        /// </summary>
        private static readonly Task<Releaser> CanceledReleaser = ThreadingTools.TaskFromCanceled<Releaser>(new CancellationToken(canceled: true));

        /// <summary>
        /// A task to return for any uncontested request for the lock.
        /// </summary>
        private readonly Task<Releaser> uncontestedReleaser;

        /// <summary>
        /// The queue of waiters.
        /// </summary>
        private readonly Queue<WaitingReleaser> waitersQueue;

        /// <summary>
        /// The current count on the semaphore.
        /// </summary>
        private int currentCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncSemaphore"/> class.
        /// </summary>
        /// <param name="initialCount">The initial number of requests for the semaphore that can be granted concurrently.</param>
        public AsyncSemaphore(int initialCount)
        {
            this.currentCount = initialCount;

            // Allocate a waiters queue big enough for twice the size of the initial count.
            // But never be less than 2 or more than 200.
            this.waitersQueue = new Queue<WaitingReleaser>(Math.Min(200, Math.Max(2, initialCount * 2)));

            this.uncontestedReleaser = Task.FromResult(new Releaser(this));
        }

        /// <summary>
        /// Gets the number of openings that remain in the semaphore.
        /// </summary>
        public int CurrentCount => Volatile.Read(ref this.currentCount);

        /// <summary>
        /// Requests access to the lock.
        /// </summary>
        /// <param name="cancellationToken">A token whose cancellation signals lost interest in the lock.</param>
        /// <returns>A task whose result is a releaser that should be disposed to release the lock.</returns>
        public Task<Releaser> EnterAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return this.EnterAsync(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        /// <summary>
        /// Requests access to the lock.
        /// </summary>
        /// <param name="timeout">A timeout for waiting for the lock.</param>
        /// <param name="cancellationToken">A token whose cancellation signals lost interest in the lock.</param>
        /// <returns>A task whose result is a releaser that should be disposed to release the lock.</returns>
        public Task<Releaser> EnterAsync(TimeSpan timeout, CancellationToken cancellationToken = default(CancellationToken))
        {
            WaitingReleaser queuedReleaser = default(WaitingReleaser);

            do
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return ThreadingTools.TaskFromCanceled<Releaser>(cancellationToken);
                }

                if (this.TryClaimSemaphoreSlot())
                {
                    // The semaphore had room for this entrance.
                    // If we already created a TaskCompletionSource, use it.
                    if (queuedReleaser.TryIfNotEmptyToScheduleOrReturnSemaphoreSlot(this))
                    {
                        break;
                    }

                    // Otherwise this is our first loop, and we can just return a pre-allocated Task.
                    return this.uncontestedReleaser;
                }
                else if (timeout == TimeSpan.Zero)
                {
                    return CanceledReleaser;
                }

                // We've driven the count below zero. We owe it, but first add our request to the queue.
                queuedReleaser.TryInitializeAndQueueIfEmpty(this, timeout, cancellationToken);
            }
            while (Interlocked.Increment(ref this.currentCount) > 0);

            return queuedReleaser.TaskSource.Task;
        }

        /// <summary>
        /// Requests access to the lock.
        /// </summary>
        /// <param name="timeout">A timeout for waiting for the lock (in milliseconds).</param>
        /// <param name="cancellationToken">A token whose cancellation signals lost interest in the lock.</param>
        /// <returns>A task whose result is a releaser that should be disposed to release the lock.</returns>
        public Task<Releaser> EnterAsync(int timeout, CancellationToken cancellationToken = default(CancellationToken))
        {
            return this.EnterAsync(TimeSpan.FromMilliseconds(timeout), cancellationToken);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes managed and unmanaged resources held by this instance.
        /// </summary>
        /// <param name="disposing"><c>true</c> if <see cref="Dispose()"/> was called; <c>false</c> if the object is being finalized.</param>
        protected virtual void Dispose(bool disposing)
        {
        }

        /// <summary>
        /// Decrements the <see cref="currentCount"/> and indicates whether a legitimate slot was claimed.
        /// </summary>
        /// <returns>
        /// <c>true</c> if a non-empty slot was claimed; <c>false</c> if an empty slot was taken instead
        /// that must be returned (after queuing the work for later, if necessary).
        /// </returns>
        private bool TryClaimSemaphoreSlot()
        {
            return Interlocked.Decrement(ref this.currentCount) >= 0;
        }

        private void Release()
        {
            while (Interlocked.Increment(ref this.currentCount) > 0)
            {
                WaitingReleaser nextInLine = default(WaitingReleaser);
                lock (this.waitersQueue)
                {
                    if (this.waitersQueue.Count == 0)
                    {
                        return;
                    }

                    if (this.TryClaimSemaphoreSlot())
                    {
                        while (this.waitersQueue.Count > 0)
                        {
                            nextInLine = this.waitersQueue.Dequeue();
                            if (!nextInLine.IsCompletedOrEmpty)
                            {
                                break;
                            }
                        }
                    }
                }

                if (!nextInLine.IsCompletedOrEmpty)
                {
                    // Don't complete the task inline here because our caller may have no shared interest in
                    // optimizing the execution of someone else waiting for the semaphore, and completing the task
                    // invites .NET to execute continuations inline.
                    Task.Run(() => nextInLine.TryIfNotEmptyToScheduleOrReturnSemaphoreSlot(this));
                    return;
                }
            }
        }

        /// <summary>
        /// Take this opportunity to drain the queue of canceled requests
        /// to release memory.
        /// </summary>
        private void DrainCompletedWaitersFromQueue()
        {
            lock (this.waitersQueue)
            {
                while (this.waitersQueue.Peek().IsCompletedOrEmpty)
                {
                    this.waitersQueue.Dequeue();
                }
            }
        }

        /// <summary>
        /// A value whose disposal triggers the release of a lock.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public struct Releaser : IDisposable
        {
            /// <summary>
            /// The lock instance to release.
            /// </summary>
            private readonly AsyncSemaphore toRelease;

            /// <summary>
            /// Initializes a new instance of the <see cref="Releaser"/> struct.
            /// </summary>
            /// <param name="toRelease">The lock instance to release on.</param>
            internal Releaser(AsyncSemaphore toRelease)
            {
                this.toRelease = toRelease;
            }

            /// <summary>
            /// Releases the lock.
            /// </summary>
            public void Dispose()
            {
                this.toRelease?.Release();
            }
        }

        private struct WaitingReleaser
        {
            internal TaskCompletionSource<Releaser> TaskSource;

            internal CancellationTokenRegistration CancellationRegistration;

            internal bool IsEmpty => this.TaskSource == null;

            internal bool IsNotEmpty => !this.IsEmpty;

            internal bool IsCompletedOrEmpty => this.TaskSource?.Task.IsCompleted ?? true;

            internal bool TryInitializeAndQueueIfEmpty(AsyncSemaphore semaphore, TimeSpan timeout, CancellationToken cancellationToken)
            {
                Requires.NotNull(semaphore, nameof(semaphore));

                if (this.IsEmpty)
                {
                    this.TaskSource = new TaskCompletionSource<Releaser>();
                    if (timeout != Timeout.InfiniteTimeSpan && timeout != TimeSpan.Zero)
                    {
                        Task delayTask;
                        delayTask = Task.Delay(timeout, cancellationToken);
                        delayTask.ContinueWith(
                            (_, state) => ((TaskCompletionSource<Releaser>)state).TrySetCanceled(),
                            this.TaskSource,
                            cancellationToken,
                            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
                            TaskScheduler.Default);
                    }

                    if (cancellationToken.CanBeCanceled)
                    {
                        this.CancellationRegistration = cancellationToken.Register(
                            state => ((TaskCompletionSource<Releaser>)state).TrySetCanceled(cancellationToken),
                            this.TaskSource);
                    }

                    lock (semaphore.waitersQueue)
                    {
                        semaphore.waitersQueue.Enqueue(this);
                        semaphore.DrainCompletedWaitersFromQueue();
                    }

                    return true;
                }
                else
                {
                    return false;
                }
            }

            internal bool TryIfNotEmptyToScheduleOrReturnSemaphoreSlot(AsyncSemaphore semaphore)
            {
                Requires.NotNull(semaphore, nameof(semaphore));

                if (this.IsNotEmpty)
                {
                    // Take care when completing, since this Task may have already been completed
                    // by another thread by virtue of being on the queue.
                    if (this.TaskSource?.TrySetResult(new Releaser(semaphore)) ?? false)
                    {
                        // We completed it. no more need for cancellation registration, if any.
                        this.CancellationRegistration.Dispose();
                        return true;
                    }
                    else
                    {
                        // We lost a race and someone else completed it, so we need to
                        // return the borrowed semaphore slot.
                        Interlocked.Increment(ref semaphore.currentCount);
                    }
                }

                return false;
            }
        }
    }
}
