namespace Icod.Grep;

using System.Buffers;
using Icod.CommandFramework.IO;
using Icod.CommandFramework.Records;

/// <summary>Materializes grep records over the segmented reader without redundant single-record copies.</summary>
internal sealed class ByteRecordReader : IDisposable {
	private readonly DelimitedByteRecordSegmentReader reader;

	/// <summary>Initializes a grep-local materializing reader.</summary>
	/// <param name="stream">The readable source stream.</param>
	/// <param name="separator">The record separator.</param>
	/// <param name="bufferSize">The bounded segment-reader buffer size.</param>
	public ByteRecordReader(
		Stream stream,
		byte separator = (byte)'\n',
		int bufferSize = StreamOperations.DefaultBufferSize
	) {
		ArgumentNullException.ThrowIfNull( stream );
		this.reader = new DelimitedByteRecordSegmentReader(
			stream,
			separator,
			bufferSize
		);
	}

	/// <summary>Reads the next independently owned logical record.</summary>
	/// <param name="cancellationToken">A token that may cancel the read.</param>
	/// <returns>The next record, or <see langword="null"/> after end of input.</returns>
	public async ValueTask<GrepByteRecord?> ReadAsync(
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		ArrayBufferWriter<byte>? builder = null;
		while ( true ) {
			var segment = await this.reader.ReadAsync(
				cancellationToken
			).ConfigureAwait( false );
			if ( segment is null ) {
				return null;
			}
			if ( builder is null && segment.EndsRecord ) {
				return new GrepByteRecord(
					segment.Data,
					segment.IsTerminated
				);
			}
			builder ??= new ArrayBufferWriter<byte>(
				Math.Max( 1, segment.Data.Length )
			);
			var destination = builder.GetSpan( segment.Data.Length );
			segment.Data.Span.CopyTo( destination );
			builder.Advance( segment.Data.Length );
			if ( segment.EndsRecord ) {
				return new GrepByteRecord(
					builder.WrittenMemory,
					segment.IsTerminated
				);
			}
		}
	}

	/// <inheritdoc/>
	public void Dispose() {
		this.reader.Dispose();
	}
}

/// <summary>Represents one independently owned grep record.</summary>
internal readonly record struct GrepByteRecord(
	ReadOnlyMemory<byte> Content,
	bool IsTerminated
);
