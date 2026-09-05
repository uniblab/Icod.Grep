namespace Icod.Grep.Tests;

using Icod.CommandFramework.Diagnostics;
using Xunit;

/// <summary>Tests binary-probe semantics and T6.4 seekable-stream bounds.</summary>
public sealed class BinaryProbeCommandTests {
	private const int BinaryProbeLength = 98_304;
	private const int ProbeChunkLength = 8_192;

	/// <summary>Verifies seekable probing stops after the first chunk containing NUL.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task SeekableProbeStopsAfterNulDiscovery() {
		var input = Enumerable.Repeat( (byte)'a', BinaryProbeLength + 4_096 ).ToArray();
		input[1] = 0;
		using var stream = new TrackingSeekableStream( input );
		var result = await RunAsync( [ "-I", "TARGET" ], stream );
		Assert.Equal( CommandExitCodes.Failure, result.Status );
		Assert.Empty( result.Output );
		Assert.InRange(
			stream.BytesReadBeforeFirstSeek,
			1,
			ProbeChunkLength
		);
	}

	/// <summary>Verifies a non-binary seekable probe reads exactly the established compatibility window.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task SeekableProbeHonorsCompatibilityWindow() {
		var input = Enumerable.Repeat( (byte)'a', BinaryProbeLength + 4_096 ).ToArray();
		using var stream = new TrackingSeekableStream( input );
		var result = await RunAsync( [ "-I", "TARGET" ], stream );
		Assert.Equal( CommandExitCodes.Failure, result.Status );
		Assert.Equal(
			BinaryProbeLength,
			stream.BytesReadBeforeFirstSeek
		);
	}

	/// <summary>Verifies seekable probing restores the caller's starting position before record search begins.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task SeekableProbeRestoresStartingPosition() {
		using var stream = new MemoryStream(
			"skip\nhit\n"u8.ToArray(),
			writable: false
		);
		stream.Position = 5;
		var result = await RunAsync( [ "hit" ], stream );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "hit\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies the existing non-seekable prefix replay preserves bytes consumed by probing.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task NonSeekableProbeReplaysPrefetchedBytes() {
		using var stream = new ChunkedNonSeekableStream(
			"hit\nmiss\n"u8.ToArray(),
			2
		);
		var result = await RunAsync( [ "hit" ], stream );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "hit\n"u8.ToArray(), result.Output );
	}

	private static async Task<(int Status, byte[] Output, string Error)> RunAsync(
		string[] args,
		Stream input
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( input );
		using var output = new MemoryStream();
		var textOutput = new StringWriter();
		var error = new StringWriter();
		var context = new CommandContext(
			"grep",
			new StringReader( string.Empty ),
			textOutput,
			error,
			input,
			output
		);
		var status = await Command.RunAsync( args, context );
		return ( status, output.ToArray(), error.ToString() );
	}

	private sealed class TrackingSeekableStream : Stream {
		private readonly MemoryStream source;
		private bool observedSeek;

		public TrackingSeekableStream( byte[] input ) {
			ArgumentNullException.ThrowIfNull( input );
			this.source = new MemoryStream( input, writable: false );
		}

		public long BytesReadBeforeFirstSeek { get; private set; }
		public override bool CanRead => true;
		public override bool CanSeek => true;
		public override bool CanWrite => false;
		public override long Length => this.source.Length;
		public override long Position {
			get => this.source.Position;
			set => this.source.Position = value;
		}

		public override void Flush() {
		}

		public override int Read( byte[] buffer, int offset, int count ) {
			ArgumentNullException.ThrowIfNull( buffer );
			var read = this.source.Read( buffer, offset, count );
			this.TrackRead( read );
			return read;
		}

		public override int Read( Span<byte> buffer ) {
			var read = this.source.Read( buffer );
			this.TrackRead( read );
			return read;
		}

		public override async ValueTask<int> ReadAsync(
			Memory<byte> buffer,
			CancellationToken cancellationToken = default
		) {
			var read = await this.source.ReadAsync(
				buffer,
				cancellationToken
			).ConfigureAwait( false );
			this.TrackRead( read );
			return read;
		}

		public override long Seek( long offset, SeekOrigin origin ) {
			this.observedSeek = true;
			return this.source.Seek( offset, origin );
		}

		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();

		protected override void Dispose( bool disposing ) {
			if ( disposing ) {
				this.source.Dispose();
			}
			base.Dispose( disposing );
		}

		private void TrackRead( int count ) {
			if ( !this.observedSeek ) {
				this.BytesReadBeforeFirstSeek += count;
			}
		}
	}

	private sealed class ChunkedNonSeekableStream : Stream {
		private readonly int maximumReadLength;
		private readonly MemoryStream source;

		public ChunkedNonSeekableStream( byte[] input, int maximumReadLength ) {
			ArgumentNullException.ThrowIfNull( input );
			if ( 0 >= maximumReadLength ) {
				throw new ArgumentOutOfRangeException( nameof( maximumReadLength ) );
			}
			this.source = new MemoryStream( input, writable: false );
			this.maximumReadLength = maximumReadLength;
		}

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position {
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override void Flush() {
		}

		public override int Read( byte[] buffer, int offset, int count ) {
			ArgumentNullException.ThrowIfNull( buffer );
			return this.source.Read(
				buffer,
				offset,
				Math.Min( count, this.maximumReadLength )
			);
		}

		public override int Read( Span<byte> buffer ) => this.source.Read(
			buffer[..Math.Min( buffer.Length, this.maximumReadLength )]
		);

		public override ValueTask<int> ReadAsync(
			Memory<byte> buffer,
			CancellationToken cancellationToken = default
		) => this.source.ReadAsync(
			buffer[..Math.Min( buffer.Length, this.maximumReadLength )],
			cancellationToken
		);

		public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException();
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();

		protected override void Dispose( bool disposing ) {
			if ( disposing ) {
				this.source.Dispose();
			}
			base.Dispose( disposing );
		}
	}
}
