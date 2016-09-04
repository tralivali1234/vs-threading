/********************************************************
*                                                        *
*   © Copyright (C) Microsoft. All rights reserved.      *
*                                                        *
*********************************************************/

namespace Microsoft.VisualStudio.Threading
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Describes a JoinableTask created with <see cref="JoinableTaskFactory.Create{T}(Func{Task{T}}, JoinableTaskCreationOptions)"/>
    /// that does not start until <see cref="Start"/> is later invoked.
    /// </summary>
    /// <typeparam name="T">The type of value returned by the deferred <see cref="Threading.JoinableTask"/>.</typeparam>
    public struct DeferredJoinableTask<T> : IEquatable<DeferredJoinableTask<T>>
    {
        /// <summary>
        /// The base struct that provides functionality for this one.
        /// </summary>
        private DeferredJoinableTask djt;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeferredJoinableTask{T}"/> struct.
        /// </summary>
        /// <param name="joinableTask">The unstarted <see cref="JoinableTask"/>.</param>
        /// <param name="invocationDelegate">The delegate to invoke when the task is started.</param>
        internal DeferredJoinableTask(JoinableTask joinableTask, Func<Task> invocationDelegate)
        {
            this.djt = new DeferredJoinableTask(joinableTask, invocationDelegate);
        }

        /// <summary>
        /// Gets the <see cref="Threading.JoinableTask{T}"/> that only starts when <see cref="Start"/> is invoked.
        /// </summary>
        public JoinableTask<T> JoinableTask => (JoinableTask<T>)this.djt.JoinableTask;

        /// <summary>
        /// Starts the <see cref="JoinableTask"/>, executing the delegate immediately on the caller's thread.
        /// </summary>
        /// <remarks>
        /// This method executes the async delegate up to where it returns a <see cref="Task"/>,
        /// just as the <see cref="JoinableTaskFactory.RunAsync{T}(Func{Task{T}})"/> would have.
        /// Any exception thrown by the delegate is not thrown by this method.
        /// Any result returned or exception thrown by the delegate must be observed
        /// after this call either by awaiting this <see cref="JoinableTask"/> or calling the
        /// <see cref="JoinableTask.Join"/> method.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the <see cref="JoinableTask"/> has already been started with this method
        /// or this struct was not created with the <see cref="JoinableTaskFactory.Create{T}"/> method.
        /// </exception>
        public void Start()
        {
            this.djt.Start();
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is DeferredJoinableTask<T>
                && this.Equals((DeferredJoinableTask<T>)obj);
        }

        /// <inheritdoc />
        public bool Equals(DeferredJoinableTask<T> other)
        {
            return this.JoinableTask == other.JoinableTask;
        }

        /// <inheritdoc />
        public override int GetHashCode() => this.JoinableTask?.GetHashCode() ?? 0;

        /// <summary>
        /// Compares two instances for equality.
        /// </summary>
        /// <param name="first">The first item to compare.</param>
        /// <param name="second">The second item to compare.</param>
        /// <returns><c>true</c> if the two instances are equal; <c>false</c> otherwise.</returns>
        public static bool operator ==(DeferredJoinableTask<T> first, DeferredJoinableTask<T> second) => first.Equals(second);

        /// <summary>
        /// Compares two instances for inequality.
        /// </summary>
        /// <param name="first">The first item to compare.</param>
        /// <param name="second">The second item to compare.</param>
        /// <returns><c>false</c> if the two instances are equal; <c>true</c> otherwise.</returns>
        public static bool operator !=(DeferredJoinableTask<T> first, DeferredJoinableTask<T> second) => !(first == second);
    }
}
