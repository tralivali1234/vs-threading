namespace Microsoft.VisualStudio.Threading.Tests {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;
	using System.Threading;
	using System.Threading.Tasks;
	using Xunit;

	public class AsyncPrioritizedQueueTests {
		private AsyncPrioritizedQueue<string, int> queue = new AsyncPrioritizedQueue<string, int>();

		[Fact]
		public void Ctor_RejectNullComparer() {
			Assert.Throws<ArgumentNullException>(() => new AsyncPrioritizedQueue<string, int>(null));
		}

		[Fact]
		public void IsEmpty_Empty() {
			Assert.True(this.queue.IsEmpty);
		}

		[Fact]
		public void Enqueue() {
			this.queue.Enqueue("first", 2);
			Assert.False(this.queue.IsEmpty);
		}

		[Fact]
		public void DequeueAsync_Empty_NoArgs() {
			var dequeueTask = this.queue.DequeueAsync();
			Assert.False(dequeueTask.IsCompleted);
		}

		[Fact]
		public async Task DequeueAsync_Empty() {
			var cts = new CancellationTokenSource();
			var dequeueTask = this.queue.DequeueAsync(cts.Token);
			Assert.False(dequeueTask.IsCompleted);
			cts.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(() => dequeueTask);
		}

		[Fact]
		public async Task EnqueueThenDequeue() {
			var enqueuedItem = "first";
			this.queue.Enqueue(enqueuedItem, 1);
			Assert.False(this.queue.IsEmpty);
			var dequeuedItem = await this.queue.DequeueAsync();
			Assert.True(this.queue.IsEmpty);
			Assert.Same(enqueuedItem, dequeuedItem);
		}

		[Fact]
		public void Completion_Empty() {
			Assert.False(this.queue.Completion.IsCompleted);
			Assert.False(this.queue.IsCompleted);
			this.queue.Complete();
			Assert.True(this.queue.IsCompleted);
			Assert.True(this.queue.Completion.IsCompleted);
		}
	}
}
