namespace Icod.Grep.Benchmarks;

/// <summary>Counts output without retaining it and applies deterministic periodic backpressure.</summary>
internal sealed class ThrottledCountingWriteStream : Stream {
	private readonly int delayMilliseconds;
	private readonly int delayEveryWrites;
	private long acceptedBytes;
	private int maximumWriteBytes;
	private int writeCount;

	internal ThrottledCountingWriteStream(
		int delayEveryWrites,
		int delayMilliseconds
	) {
		if ( delayEveryWrites <= 0 ) {
			throw new ArgumentOutOfRangeException( nameof( delayEveryWrites ) );
		}
		if ( delayMilliseconds < 0 ) {
			throw new ArgumentOutOfRangeException( nameof( delayMilliseconds ) );
		}
		this.delayEveryWrites = delayEveryWrites;
		this.delayMilliseconds = delayMilliseconds;
	}

	internal long AcceptedBytes => this.acceptedBytes;

	internal int MaximumWriteBytes => this.maximumWriteBytes;

	internal int WriteCount => this.writeCount;

	public override bool CanRead => false;

	public override bool CanSeek => false;

	public override bool CanWrite => true;

	public override long Length => this.acceptedBytes;

	public override long Position {
		get => this.acceptedBytes;
		set => throw new NotSupportedException();
	}

	public override void Flush() {
	}

	public override Task FlushAsync( CancellationToken cancellationToken ) {
		cancellationToken.ThrowIfCancellationRequested();
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
		this.RegisterWrite( count );
		if (
			0 < this.delayMilliseconds
			&& 0 == this.writeCount % this.delayEveryWrites
		) {
			Thread.Sleep( this.delayMilliseconds );
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
		cancellationToken.ThrowIfCancellationRequested();
		this.RegisterWrite( buffer.Length );
		if (
			0 < this.delayMilliseconds
			&& 0 == this.writeCount % this.delayEveryWrites
		) {
			await Task.Delay(
				this.delayMilliseconds,
				cancellationToken
			).ConfigureAwait( false );
		}
	}

	private void RegisterWrite( int count ) {
		if ( count < 0 ) {
			throw new ArgumentOutOfRangeException( nameof( count ) );
		}
		this.acceptedBytes += count;
		this.writeCount++;
		if ( this.maximumWriteBytes < count ) {
			this.maximumWriteBytes = count;
		}
	}
}
