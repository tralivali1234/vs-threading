namespace Microsoft.VisualStudio.Threading.Tests
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml.Linq;
    using Xunit;
    using Xunit.Abstractions;

    public class JoinableTask_DeferredStartTests : JoinableTaskTestBase
    {
        public JoinableTask_DeferredStartTests(ITestOutputHelper logger)
            : base(logger)
        {
        }

        [StaFact]
        public void Start_InvokesDelegateOfDeferredJoinableTask_AndJoinFinishesIt()
        {
            bool delegateStarted = false, delegateResumed = false;
            var djt = this.asyncPump.Create(async delegate
            {
                delegateStarted = true;
                Assert.Same(this.originalThread, Thread.CurrentThread);
                await Task.Yield();
                delegateResumed = true;
            });
            Assert.False(delegateStarted);
            Assert.NotNull(djt.JoinableTask);
            djt.Start();
            Assert.True(delegateStarted);
            djt.JoinableTask.Join();
            Assert.True(delegateResumed);
        }

        [StaFact]
        public void Start_InvokesDelegateOfDeferredJoinableTaskOfT_AndJoinFinishesIt()
        {
            bool delegateStarted = false;
            var djt = this.asyncPump.Create(async delegate
            {
                delegateStarted = true;
                Assert.Same(this.originalThread, Thread.CurrentThread);
                await Task.Yield();
                return 5;
            });
            Assert.False(delegateStarted);
            Assert.NotNull(djt.JoinableTask);
            djt.Start();
            Assert.True(delegateStarted);
            int result = djt.JoinableTask.Join();
            Assert.Equal(5, result);
        }

        [StaFact]
        public void AwaitingUnstartedDeferredJoinableTaskDoesNotStartTask()
        {
            bool delegateStarted = false;
            var djt = this.asyncPump.Create(delegate
            {
                delegateStarted = true;
                return TplExtensions.CompletedTask;
            });

            // simulate awaiting
            djt.JoinableTask.GetAwaiter().OnCompleted(delegate { });
            Assert.False(delegateStarted);
        }

        [StaFact]
        public void Join_DoesNotStartUnstartedJoinableTask()
        {
            bool delegateStarted = false;
            var djt = this.asyncPump.Create(delegate
            {
                delegateStarted = true;
                return TplExtensions.CompletedTask;
            });

            Task.Run(() => djt.JoinableTask.Join(this.TimeoutToken));
            Thread.Sleep(AsyncDelay);
            Assert.False(delegateStarted);
            djt.Start(); // start so the thread can be released.
        }

        [StaFact]
        public void Start_ThrowsInvalidOperationExceptionOnStartedTask()
        {
            int delegateExecutionCounter = 0;
            var djt = this.asyncPump.Create(async delegate
            {
                delegateExecutionCounter++;
                await Task.Yield();
            });
            var djt2 = djt; // copy the struct
            djt.Start();
            Assert.Equal(1, delegateExecutionCounter);
            Assert.Throws<InvalidOperationException>(() => djt.Start());
            Assert.Equal(1, delegateExecutionCounter);

            // Also try starting via the copy of the struct.
            Assert.Throws<InvalidOperationException>(() => djt2.Start());
            Assert.Equal(1, delegateExecutionCounter);
        }

        [StaFact]
        public void Start_DelegateThrows_Sync()
        {
            var djt = this.asyncPump.Create(delegate
            {
                throw new ApplicationException();
            });
            djt.Start();
            Assert.True(djt.JoinableTask.IsCompleted);
            Assert.Throws<ApplicationException>(() => djt.JoinableTask.GetAwaiter().GetResult());
        }

        [StaFact]
        public void DeferredTaskDelegateThrows_AfterYield()
        {
            var djt = this.asyncPump.Create(async delegate
            {
                await Task.Yield();
                throw new ApplicationException();
            });
            djt.Start();
            Assert.Throws<ApplicationException>(() => djt.JoinableTask.Join());
        }

        [StaFact]
        public void MainThreadClaimIsNotEstablishedByCreateMethod()
        {
            this.asyncPump.Run(async delegate
            {
                var djt = this.asyncPump.Create(async delegate
                {
                    await Task.Yield();
                });
                using (this.asyncPump.Context.SuppressRelevance())
                {
                    djt.Start();
                }

                // Give it time to complete if it were relevant. Await the Task, not the JoinableTask, to avoid spreading relevance.
                await Assert.ThrowsAsync<TimeoutException>(() => djt.JoinableTask.Task.WithTimeout(TimeSpan.FromMilliseconds(AsyncDelay)));

                // Since the context is pulled from where the Start() method is called,
                // which was a background thread, the UI thread should not have let it in.
                Assert.False(djt.JoinableTask.IsCompleted);
            });
        }

        [StaFact]
        public void MainThreadClaimIsEstablishedByStartMethod()
        {
            this.asyncPump.Run(async delegate
            {
                DeferredJoinableTask djt;
                using (this.asyncPump.Context.SuppressRelevance())
                {
                    djt = this.asyncPump.Create(async delegate
                    {
                        await Task.Yield();
                    });
                }

                djt.Start();

                // Give it time to complete if it were relevant. Await the Task, not the JoinableTask, to avoid spreading relevance.
                await djt.JoinableTask.Task.WithTimeout(UnexpectedTimeout);

                // Since the context is pulled from where the Start() method is called,
                // which was a background thread, the UI thread should not have let it in.
                Assert.True(djt.JoinableTask.IsCompleted);
            });
        }

        [StaFact]
        public void SanityCheck()
        {
            this.asyncPump.Run(async delegate
            {
                var djt = this.asyncPump.RunAsync(async delegate
                {
                    await Task.Yield();
                });

                // Give it time to complete if it were relevant.
                await djt.Task.WithTimeout(UnexpectedTimeout);

                // Since the context is pulled from where the Start() method is called,
                // which was a background thread, the UI thread should not have let it in.
                Assert.True(djt.IsCompleted);
            });
        }

        [StaFact]
        public void StructDefaultConstructorsFailGracefully()
        {
            var djt = default(DeferredJoinableTask);
            Assert.Null(djt.JoinableTask);
            Assert.Throws<InvalidOperationException>(() => djt.Start());

            var djtOfT = default(DeferredJoinableTask<int>);
            Assert.Null(djtOfT.JoinableTask);
            Assert.Throws<InvalidOperationException>(() => djtOfT.Start());
        }

        [StaFact]
        public void JoinAsync_AidsDeferredTaskToUIThread_StartFirst()
        {
            this.SimulateUIThread(delegate
            {
                var djt = this.asyncPump.Create(async delegate
                {
                    await Task.Yield();
                });
                djt.Start();
                this.asyncPump.Run(async delegate
                {
                    var joinTask = djt.JoinableTask.JoinAsync();
                    Assert.False(joinTask.IsCompleted);
                    await joinTask;
                });
                return TplExtensions.CompletedTask;
            });
        }

        [StaFact]
        public void JoinAsync_AidsDeferredTaskToUIThread_StartFirst_OfT()
        {
            this.SimulateUIThread(delegate
            {
                var djt = this.asyncPump.Create(async delegate
                {
                    await Task.Yield();
                    return 5;
                });
                djt.Start();
                this.asyncPump.Run(async delegate
                {
                    var joinTask = djt.JoinableTask.JoinAsync();
                    Assert.False(joinTask.IsCompleted);
                    Assert.Equal(5, await joinTask);
                });
                return TplExtensions.CompletedTask;
            });
        }

        [StaFact]
        public void JoinAsync_AidsDeferredTaskToUIThread_JoinFirst()
        {
            this.SimulateUIThread(delegate
            {
                var djt = this.asyncPump.Create(async delegate
                {
                    await Task.Yield();
                });
                this.asyncPump.Run(async delegate
                {
                    var joinTask = djt.JoinableTask.JoinAsync();
                    djt.Start();
                    Assert.False(joinTask.IsCompleted);
                    await joinTask;
                });
                return TplExtensions.CompletedTask;
            });
        }

        [StaFact]
        public void JoinAsync_AidsDeferredTaskToUIThread_JoinFirst_OfT()
        {
            this.SimulateUIThread(delegate
            {
                var djt = this.asyncPump.Create(async delegate
                {
                    await Task.Yield();
                    return 5;
                });
                this.asyncPump.Run(async delegate
                {
                    var joinTask = djt.JoinableTask.JoinAsync();
                    djt.Start();
                    Assert.False(joinTask.IsCompleted);
                    Assert.Equal(5, await joinTask);
                });
                return TplExtensions.CompletedTask;
            });
        }

        [StaFact]
        public void NoDeadlockInSynchronousContinuations_SynthesizedTask()
        {
            var djt = this.asyncPump.Create(async delegate
            {
                await Task.Yield();
            });

            // schedule a continuation on the synthesized Task (since the delegate hasn't returned one yet).
            var continuation = djt.JoinableTask.Task.ContinueWith(
                delegate
                {
                    // The validity of the test depends on synchronous execution of the continuation.
                    Assert.Same(this.originalThread, Thread.CurrentThread);

                    // Get onto another thread to check for deadlocks.
                    Task.Run(delegate
                    {
                        this.asyncPump.Run(delegate
                        {
                            return TplExtensions.CompletedTask;
                        });
                    }).Wait(UnexpectedTimeout);
                },
                TaskContinuationOptions.ExecuteSynchronously);
            djt.Start();
            djt.JoinableTask.Join();
            continuation.Wait();
        }

        [StaFact]
        public void NoDeadlockInSynchronousContinuations_IncompleteRealTask()
        {
            var djt = this.asyncPump.Create(async delegate
            {
                await Task.Yield();
            });
            djt.Start();

            // schedule a continuation on the Task actually returned from the delegate.
            var continuation = djt.JoinableTask.Task.ContinueWith(
                delegate
                {
                    // The validity of the test depends on synchronous execution of the continuation.
                    Assert.Same(this.originalThread, Thread.CurrentThread);

                    // Get onto another thread to check for deadlocks.
                    Task.Run(delegate
                    {
                        this.asyncPump.Run(delegate
                        {
                            return TplExtensions.CompletedTask;
                        });
                    }).Wait(UnexpectedTimeout);
                },
                TaskContinuationOptions.ExecuteSynchronously);
            djt.JoinableTask.Join();
            continuation.Wait();
        }

        [StaFact]
        public void Create_OfT_ThrowsSynchronously()
        {
            var djt = this.asyncPump.Create(new Func<Task<int>>(delegate
            {
                throw new ApplicationException();
            }));
            djt.Start();
            Assert.IsType<ApplicationException>(djt.JoinableTask.Task.Exception.InnerException);
        }
    }
}
