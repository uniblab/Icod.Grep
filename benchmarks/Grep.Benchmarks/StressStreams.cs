namespace Icod.Grep.Benchmarks;

/// <summary>Provides deterministic bounded reads and triggers cancellation after a configured read.</summary>
internal sealed class TriggeredCancellationReadStream : Stream {
	private readonly Action cancel;
	private readonly byte[] content;
	private readonly int maximumChunkBytes;
	private readonly int triggerRead;
	private int offset;
	private int readCount;

	internal TriggeredCancellationReadStream(
		byte[] content,
		int maximumChunkBytes,
		int triggerRead,
		Action cancel
	) {
		ArgumentNullException.ThrowIfNull( content );
		ArgumentNullException.ThrowIfNull( cancel );
		if ( maximumChunkBytes <= 0 ) {
			throw new ArgumentOutOfRangeException( nameof( maximumChunkBytes ) );
		}
		if ( triggerRead <= 0 ) {
			throw new ArgumentOutOfRangeException( nameof( triggerRead ) );
		}
		this.content = content;
		this.maximumChunkBytes = maximumChunkBytes;
		this.triggerRead = triggerRead;
		this.cancel = cancel;
	}

	internal bool CancellationTriggered => this.readCount >= this.triggerRead;

	public override bool CanRead => true;
	public override bool CanSeek => false;
	public override bool CanWrite => false;
	public override long Length => this.content.Length;
	public override long Position {
		get => this.offset;
		set => throw new NotSupportedException();
	}

	public override void Flush() {
	}

	public override int Read(
		byte[] buffer,
		int offset,
		int count
	) {
		ArgumentNullException.ThrowIfNull( buffer );
		return this.Read( buffer.AsSpan( offset, count ) );
	}

	public override int Read( Span<byte> buffer ) {
		return this.CopyNext( buffer );
	}

	public override ValueTask<int> ReadAsync(
		Memory<byte> buffer,
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult(
			this.CopyNext( buffer.Span )
		);
	}

	public override Task<int> ReadAsync(
		byte[] buffer,
		int offset,
		int count,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( buffer );
		return this.ReadAsync(
			buffer.AsMemory( offset, count ),
			cancellationToken
		).AsTask();
	}

	public override long Seek(
		long offset,
		SeekOrigin origin
	) => throw new NotSupportedException();

	public override void SetLength( long value ) => throw new NotSupportedException();

	public override void Write(
		byte[] buffer,
		int offset,
		int count
	) => throw new NotSupportedException();

	private int CopyNext( Span<byte> buffer ) {
		if ( buffer.IsEmpty || this.offset >= this.content.Length ) {
			return 0;
		}
		var count = Math.Min(
			Math.Min( buffer.Length, this.maximumChunkBytes ),
			this.content.Length - this.offset
		);
		this.content.AsSpan( this.offset, count ).CopyTo( buffer );
		this.offset += count;
		this.readCount++;
		if ( this.readCount == this.triggerRead ) {
			this.cancel();
		}
		return count;
	}
}

/// <summary>Simulates bounded asynchronous output throughput without retaining unbounded data.</summary>
internal sealed class DelayedChunkWriteStream : Stream {
	private readonly int delayMilliseconds;
	private readonly int maximumChunkBytes;
	private readonly MemoryStream sink = new();

	internal DelayedChunkWriteStream(
		int maximumChunkBytes,
		int delayMilliseconds
	) {
		if ( maximumChunkBytes <= 0 ) {
			throw new ArgumentOutOfRangeException( nameof( maximumChunkBytes ) );
		}
		if ( delayMilliseconds < 0 ) {
			throw new ArgumentOutOfRangeException( nameof( delayMilliseconds ) );
		}
		this.maximumChunkBytes = maximumChunkBytes;
		this.delayMilliseconds = delayMilliseconds;
	}

	internal byte[] ToArray() => this.sink.ToArray();

	public override bool CanRead => false;
	public override bool CanSeek => false;
	public override bool CanWrite => true;
	public override long Length => this.sink.Length;
	public override long Position {
		get => this.sink.Position;
		set => throw new NotSupportedException();
	}

	public override void Flush() {
		this.sink.Flush();
	}

	public override Task FlushAsync( CancellationToken cancellationToken ) {
		return this.FlushCoreAsync( cancellationToken );
	}

	public override int Read(
		byte[] buffer,
		int offset,
		int count
	) => throw new NotSupportedException();

	public override long Seek(
		long offset,
		SeekOrigin origin
	) => throw new NotSupportedException();

	public override void SetLength( long value ) => throw new NotSupportedException();

	public override void Write(
		byte[] buffer,
		int offset,
		int count
	) {
		ArgumentNullException.ThrowIfNull( buffer );
		var remaining = buffer.AsSpan( offset, count );
		while ( !remaining.IsEmpty ) {
			var chunkLength = Math.Min(
				remaining.Length,
				this.maximumChunkBytes
			);
			this.sink.Write( remaining[..chunkLength] );
			remaining = remaining[chunkLength..];
		}
	}

	public override Task WriteAsync(
		byte[] buffer,
		int offset,
		int count,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( buffer );
		return this.WriteAsync(
			buffer.AsMemory( offset, count ),
			cancellationToken
		).AsTask();
	}

	public override async ValueTask WriteAsync(
		ReadOnlyMemory<byte> buffer,
		CancellationToken cancellationToken = default
	) {
		var remaining = buffer;
		while ( !remaining.IsEmpty ) {
			cancellationToken.ThrowIfCancellationRequested();
			var chunkLength = Math.Min(
				remaining.Length,
				this.maximumChunkBytes
			);
			if ( 0 < this.delayMilliseconds ) {
				await Task.Delay(
					this.delayMilliseconds,
					cancellationToken
				).ConfigureAwait( false );
			}
			await this.sink.WriteAsync(
				remaining[..chunkLength],
				cancellationToken
			).ConfigureAwait( false );
			remaining = remaining[chunkLength..];
		}
	}

	protected override void Dispose( bool disposing ) {
		if ( disposing ) {
			this.sink.Dispose();
		}
		base.Dispose( disposing );
	}

	private async Task FlushCoreAsync( CancellationToken cancellationToken ) {
		if ( 0 < this.delayMilliseconds ) {
			await Task.Delay(
				this.delayMilliseconds,
				cancellationToken
			).ConfigureAwait( false );
		}
		await this.sink.FlushAsync( cancellationToken ).ConfigureAwait( false );
	}
}

/// <summary>Fails deterministically during output writes or flush/completion.</summary>
internal sealed class FailingWriteStream : Stream {
	private readonly long failAfterBytes;
	private readonly bool failOnFlush;
	private long acceptedBytes;

	internal FailingWriteStream(
		long failAfterBytes,
		bool failOnFlush
	) {
		if ( failAfterBytes < 0 ) {
			throw new ArgumentOutOfRangeException( nameof( failAfterBytes ) );
		}
		this.failAfterBytes = failAfterBytes;
		this.failOnFlush = failOnFlush;
	}

	internal long AcceptedBytes => this.acceptedBytes;

	public override bool CanRead => false;
	public override bool CanSeek => false;
	public override bool CanWrite => true;
	public override long Length => this.acceptedBytes;
	public override long Position {
		get => this.acceptedBytes;
		set => throw new NotSupportedException();
	}

	public override void Flush() {
		if ( this.failOnFlush ) {
			throw CreateFailure();
		}
	}

	public override Task FlushAsync( CancellationToken cancellationToken ) {
		cancellationToken.ThrowIfCancellationRequested();
		if ( this.failOnFlush ) {
			return Task.FromException( CreateFailure() );
		}
		return Task.CompletedTask;
	}

	public override int Read(
		byte[] buffer,
		int offset,
		int count
	) => throw new NotSupportedException();

	public override long Seek(
		long offset,
		SeekOrigin origin
	) => throw new NotSupportedException();

	public override void SetLength( long value ) => throw new NotSupportedException();

	public override void Write(
		byte[] buffer,
		int offset,
		int count
	) {
		ArgumentNullException.ThrowIfNull( buffer );
		this.Accept( count );
	}

	public override Task WriteAsync(
		byte[] buffer,
		int offset,
		int count,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( buffer );
		cancellationToken.ThrowIfCancellationRequested();
		this.Accept( count );
		return Task.CompletedTask;
	}

	public override ValueTask WriteAsync(
		ReadOnlyMemory<byte> buffer,
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		this.Accept( buffer.Length );
		return ValueTask.CompletedTask;
	}

	private void Accept( int count ) {
		if ( this.failAfterBytes - this.acceptedBytes < count ) {
			throw CreateFailure();
		}
		this.acceptedBytes += count;
	}

	private static IOException CreateFailure() => new(
		"Deterministic T6.8 output failure."
	);
}