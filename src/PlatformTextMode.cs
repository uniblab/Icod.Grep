namespace Icod.Grep;

/// <summary>Tracks and implements GNU grep's Windows text-versus-binary stream policy.</summary>
internal static class PlatformIoContext {
	private static readonly AsyncLocal<bool> WindowsTextMode = new();

	internal static bool IsWindowsTextMode => WindowsTextMode.Value;

	internal static IDisposable EnterProcessMode( IReadOnlyList<string> args ) {
		ArgumentNullException.ThrowIfNull( args );
		var previous = WindowsTextMode.Value;
		WindowsTextMode.Value = OperatingSystem.IsWindows() && !HasBinaryPlatformOption( args );
		return new RestoreScope( previous );
	}

	internal static IDisposable EnterWindowsTextModeForTesting() {
		var previous = WindowsTextMode.Value;
		WindowsTextMode.Value = true;
		return new RestoreScope( previous );
	}

	internal static Stream WrapStandardInput( Stream stream ) {
		ArgumentNullException.ThrowIfNull( stream );
		return IsWindowsTextMode
			? new WindowsTextInputStream( stream, leaveOpen: true )
			: stream;
	}

	internal static Stream WrapStandardOutput( Stream stream ) {
		ArgumentNullException.ThrowIfNull( stream );
		return IsWindowsTextMode
			? new WindowsTextOutputStream( stream, leaveOpen: true )
			: stream;
	}

	private static bool HasBinaryPlatformOption( IReadOnlyList<string> args ) {
		var parsingOptions = true;
		foreach ( var argument in args ) {
			if ( !parsingOptions ) {
				continue;
			}
			if ( "--" == argument ) {
				parsingOptions = false;
				continue;
			}
			if ( "--binary" == argument ) {
				return true;
			}
			if ( argument.Length < 2 || '-' != argument[0] || '-' == argument[1] ) {
				continue;
			}
			for ( var index = 1; index < argument.Length; index++ ) {
				var option = argument[index];
				if ( 'U' == option ) {
					return true;
				}
				if ( option is 'e' or 'f' or 'm' or 'd' or 'D' or 'B' or 'A' or 'C' ) {
					break;
				}
			}
		}
		return false;
	}

	private sealed class RestoreScope( bool previous ) : IDisposable {
		private bool disposed;

		public void Dispose() {
			if ( this.disposed ) {
				return;
			}
			this.disposed = true;
			WindowsTextMode.Value = previous;
		}
	}
}

/// <summary>Provides the FileStream constructor surface used by grep while honoring Windows text mode.</summary>
internal sealed class FileStream : Stream {
	private readonly Stream effective;
	private readonly System.IO.FileStream source;

	internal FileStream(
		string path,
		FileMode mode,
		FileAccess access,
		FileShare share,
		int bufferSize,
		FileOptions options
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		this.source = new System.IO.FileStream(
			path,
			mode,
			access,
			share,
			bufferSize,
			options
		);
		this.effective = PlatformIoContext.IsWindowsTextMode && FileAccess.Read == access
			? new WindowsTextInputStream( this.source, leaveOpen: true )
			: this.source;
	}

	public override bool CanRead => this.effective.CanRead;
	public override bool CanSeek => this.effective.CanSeek;
	public override bool CanWrite => this.effective.CanWrite;
	public override long Length => this.effective.Length;
	public override long Position {
		get => this.effective.Position;
		set => this.effective.Position = value;
	}

	public override void Flush() => this.effective.Flush();
	public override Task FlushAsync( CancellationToken cancellationToken ) => this.effective.FlushAsync( cancellationToken );
	public override int Read( byte[] buffer, int offset, int count ) => this.effective.Read( buffer, offset, count );
	public override int Read( Span<byte> buffer ) => this.effective.Read( buffer );
	public override ValueTask<int> ReadAsync( Memory<byte> buffer, CancellationToken cancellationToken = default ) =>
		this.effective.ReadAsync( buffer, cancellationToken );
	public override long Seek( long offset, SeekOrigin origin ) => this.effective.Seek( offset, origin );
	public override void SetLength( long value ) => this.effective.SetLength( value );
	public override void Write( byte[] buffer, int offset, int count ) => this.effective.Write( buffer, offset, count );
	public override void Write( ReadOnlySpan<byte> buffer ) => this.effective.Write( buffer );
	public override ValueTask WriteAsync( ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default ) =>
		this.effective.WriteAsync( buffer, cancellationToken );

	protected override void Dispose( bool disposing ) {
		if ( disposing ) {
			if ( !ReferenceEquals( this.effective, this.source ) ) {
				this.effective.Dispose();
			}
			this.source.Dispose();
		}
		base.Dispose( disposing );
	}

	public override async ValueTask DisposeAsync() {
		if ( !ReferenceEquals( this.effective, this.source ) ) {
			await this.effective.DisposeAsync().ConfigureAwait( false );
		}
		await this.source.DisposeAsync().ConfigureAwait( false );
		GC.SuppressFinalize( this );
	}
}

/// <summary>Collapses CRLF to LF and honors Control-Z EOF like Windows CRT text input.</summary>
internal sealed class WindowsTextInputStream : Stream {
	private const int BufferSize = 8192;
	private readonly bool leaveOpen;
	private readonly byte[] sourceBuffer = new byte[BufferSize];
	private readonly Stream source;
	private readonly byte[] translatedBuffer = new byte[BufferSize + 1];
	private bool endOfInput;
	private bool pendingCarriageReturn;
	private int translatedCount;
	private int translatedOffset;

	internal WindowsTextInputStream( Stream source, bool leaveOpen = false ) {
		this.source = source ?? throw new ArgumentNullException( nameof( source ) );
		if ( !source.CanRead ) {
			throw new ArgumentException( "The source stream must be readable.", nameof( source ) );
		}
		this.leaveOpen = leaveOpen;
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
		return this.Read( buffer.AsSpan( offset, count ) );
	}

	public override int Read( Span<byte> buffer ) {
		if ( buffer.IsEmpty ) {
			return 0;
		}
		while ( this.translatedOffset >= this.translatedCount ) {
			if ( !this.FillTranslatedBuffer() ) {
				return 0;
			}
		}
		return this.CopyTranslatedBytes( buffer );
	}

	public override async ValueTask<int> ReadAsync(
		Memory<byte> buffer,
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		if ( buffer.IsEmpty ) {
			return 0;
		}
		while ( this.translatedOffset >= this.translatedCount ) {
			if ( !await this.FillTranslatedBufferAsync( cancellationToken ).ConfigureAwait( false ) ) {
				return 0;
			}
		}
		return this.CopyTranslatedBytes( buffer.Span );
	}

	public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException();
	public override void SetLength( long value ) => throw new NotSupportedException();
	public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();

	private int CopyTranslatedBytes( Span<byte> destination ) {
		var count = Math.Min( destination.Length, this.translatedCount - this.translatedOffset );
		this.translatedBuffer.AsSpan( this.translatedOffset, count ).CopyTo( destination );
		this.translatedOffset += count;
		return count;
	}

	private bool FillTranslatedBuffer() {
		while ( true ) {
			this.translatedOffset = 0;
			this.translatedCount = 0;
			if ( this.endOfInput ) {
				return this.FlushPendingCarriageReturn();
			}
			var count = this.source.Read( this.sourceBuffer, 0, this.sourceBuffer.Length );
			if ( 0 == count ) {
				this.endOfInput = true;
				return this.FlushPendingCarriageReturn();
			}
			this.Translate( this.sourceBuffer.AsSpan( 0, count ) );
			if ( this.translatedCount > 0 ) {
				return true;
			}
		}
	}

	private async ValueTask<bool> FillTranslatedBufferAsync( CancellationToken cancellationToken ) {
		while ( true ) {
			this.translatedOffset = 0;
			this.translatedCount = 0;
			if ( this.endOfInput ) {
				return this.FlushPendingCarriageReturn();
			}
			var count = await this.source.ReadAsync(
				this.sourceBuffer.AsMemory(),
				cancellationToken
			).ConfigureAwait( false );
			if ( 0 == count ) {
				this.endOfInput = true;
				return this.FlushPendingCarriageReturn();
			}
			this.Translate( this.sourceBuffer.AsSpan( 0, count ) );
			if ( this.translatedCount > 0 ) {
				return true;
			}
		}
	}

	private bool FlushPendingCarriageReturn() {
		if ( !this.pendingCarriageReturn ) {
			return false;
		}
		this.pendingCarriageReturn = false;
		this.translatedBuffer[0] = (byte)'\r';
		this.translatedCount = 1;
		return true;
	}

	private void Translate( ReadOnlySpan<byte> input ) {
		foreach ( var value in input ) {
			if ( 0x1A == value ) {
				this.endOfInput = true;
				break;
			}
			if ( this.pendingCarriageReturn ) {
				this.pendingCarriageReturn = false;
				if ( (byte)'\n' == value ) {
					this.translatedBuffer[this.translatedCount++] = (byte)'\n';
					continue;
				}
				this.translatedBuffer[this.translatedCount++] = (byte)'\r';
			}
			if ( (byte)'\r' == value ) {
				this.pendingCarriageReturn = true;
			} else {
				this.translatedBuffer[this.translatedCount++] = value;
			}
		}
	}

	protected override void Dispose( bool disposing ) {
		if ( disposing && !this.leaveOpen ) {
			this.source.Dispose();
		}
		base.Dispose( disposing );
	}

	public override async ValueTask DisposeAsync() {
		if ( !this.leaveOpen ) {
			await this.source.DisposeAsync().ConfigureAwait( false );
		}
		GC.SuppressFinalize( this );
	}
}

/// <summary>Expands LF to CRLF like Windows CRT text output.</summary>
internal sealed class WindowsTextOutputStream : Stream {
	private readonly bool leaveOpen;
	private readonly Stream destination;

	internal WindowsTextOutputStream( Stream destination, bool leaveOpen = false ) {
		this.destination = destination ?? throw new ArgumentNullException( nameof( destination ) );
		if ( !destination.CanWrite ) {
			throw new ArgumentException( "The destination stream must be writable.", nameof( destination ) );
		}
		this.leaveOpen = leaveOpen;
	}

	public override bool CanRead => false;
	public override bool CanSeek => false;
	public override bool CanWrite => true;
	public override long Length => throw new NotSupportedException();
	public override long Position {
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public override void Flush() => this.destination.Flush();
	public override Task FlushAsync( CancellationToken cancellationToken ) => this.destination.FlushAsync( cancellationToken );
	public override int Read( byte[] buffer, int offset, int count ) => throw new NotSupportedException();
	public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException();
	public override void SetLength( long value ) => throw new NotSupportedException();

	public override void Write( byte[] buffer, int offset, int count ) {
		ArgumentNullException.ThrowIfNull( buffer );
		this.Write( buffer.AsSpan( offset, count ) );
	}

	public override void Write( ReadOnlySpan<byte> buffer ) {
		var translated = Translate( buffer );
		this.destination.Write( translated );
	}

	public override ValueTask WriteAsync(
		ReadOnlyMemory<byte> buffer,
		CancellationToken cancellationToken = default
	) {
		var translated = Translate( buffer.Span );
		return this.destination.WriteAsync( translated, cancellationToken );
	}

	private static byte[] Translate( ReadOnlySpan<byte> input ) {
		var newlineCount = 0;
		foreach ( var value in input ) {
			if ( (byte)'\n' == value ) {
				newlineCount++;
			}
		}
		if ( 0 == newlineCount ) {
			return input.ToArray();
		}
		var output = new byte[input.Length + newlineCount];
		var outputIndex = 0;
		foreach ( var value in input ) {
			if ( (byte)'\n' == value ) {
				output[outputIndex++] = (byte)'\r';
			}
			output[outputIndex++] = value;
		}
		return output;
	}

	protected override void Dispose( bool disposing ) {
		if ( disposing && !this.leaveOpen ) {
			this.destination.Dispose();
		}
		base.Dispose( disposing );
	}

	public override async ValueTask DisposeAsync() {
		if ( !this.leaveOpen ) {
			await this.destination.DisposeAsync().ConfigureAwait( false );
		}
		GC.SuppressFinalize( this );
	}
}
