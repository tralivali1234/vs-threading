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
    /// Describes a JoinableTask created with <see cref="JoinableTaskFactory.Create(Func{Task}, JoinableTaskCreationOptions)"/>
    /// that does not start until <see cref="Start"/> is later invoked.
    /// </summary>
    public struct DeferredJoinableTask : IEquatable<DeferredJoinableTask>
    {
        /// <summary>
        /// The delegate to invoke when the task is started.
        /// </summary>
        private Func<Task> invocationDelegate;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeferredJoinableTask"/> struct.
        /// </summary>
        /// <param name="joinableTask">The unstarted <see cref="JoinableTask"/>.</param>
        /// <param name="invocationDelegate">The delegate to invoke when the task is started.</param>
        internal DeferredJoinableTask(JoinableTask joinableTask, Func<Task> invocationDelegate)
        {
            Requires.NotNull(joinableTask, nameof(joinableTask));
            Requires.NotNull(invocationDelegate, nameof(invocationDelegate));

            this.JoinableTask = joinableTask;
            this.invocationDelegate = invocationDelegate;
        }

        /// <summary>
        /// Gets the <see cref="Threading.JoinableTask"/> that only starts when <see cref="Start"/> is invoked.
        /// </summary>
        public JoinableTask JoinableTask { get; }

        /// <summary>
        /// Starts the <see cref="JoinableTask"/>, executing the delegate immediately on the caller's thread.
        /// </summary>
        /// <remarks>
        /// This method executes the async delegate up to where it returns a <see cref="Task"/>,
        /// just as the <see cref="JoinableTaskFactory.RunAsync(Func{Task})"/> would have.
        /// Any exception thrown by the delegate is not thrown by this method.
        /// Any result returned or exception thrown by the delegate must be observed
        /// after this call either by awaiting this <see cref="JoinableTask"/> or calling the
        /// <see cref="JoinableTask.Join"/> method.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the <see cref="JoinableTask"/> has already been started with this method
        /// or this struct was not created with the <see cref="JoinableTaskFactory.Create"/> method.
        /// </exception>
        public void Start()
        {
            // Check condition before looking up string resource for better perf.
            if (this.JoinableTask == null)
            {
                Verify.FailOperation(Strings.StructNotProperlyInitialized); // improper use of the struct's default constructor.
            }

            this.JoinableTask.Factory.Start(this.JoinableTask, this.invocationDelegate);

            // Allow GC to perhaps collect this delegate more quickly.
            this.invocationDelegate = null;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is DeferredJoinableTask
                && this.Equals((DeferredJoinableTask)obj);
        }

        /// <inheritdoc />
        public bool Equals(DeferredJoinableTask other)
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
        public static bool operator ==(DeferredJoinableTask first, DeferredJoinableTask second) => first.Equals(second);

        /// <summary>
        /// Compares two instances for inequality.
        /// </summary>
        /// <param name="first">The first item to compare.</param>
        /// <param name="second">The second item to compare.</param>
        /// <returns><c>false</c> if the two instances are equal; <c>true</c> otherwise.</returns>
        public static bool operator !=(DeferredJoinableTask first, DeferredJoinableTask second) => !(first == second);
    }
}
