namespace Icod.Grep.Tests;

using Xunit;

/// <summary>Tests the grep-local segmented record materializer.</summary>
public sealed class ByteRecordReaderTests {
	/// <summary>Verifies ordinary records preserve content and termination without spanning segments.</summary>
	[Fact]
	public async Task ReadsSingleSegmentRecords() {
		using var input = new MemoryStream(
			"alpha\nbeta\n"u8.ToArray(),
			writable: false
		);
		using var reader = new ByteRecordReader(
			input,
			bufferSize: 64
		);
		var first = await reader.ReadAsync();
		var second = await reader.ReadAsync();
		var end = await reader.ReadAsync();
		Assert.NotNull( first );
		Assert.Equal( "alpha"u8.ToArray(), first.Value.Content.ToArray() );
		Assert.True( first.Value.IsTerminated );
		Assert.NotNull( second );
		Assert.Equal( "beta"u8.ToArray(), second.Value.Content.ToArray() );
		Assert.True( second.Value.IsTerminated );
		Assert.Null( end );
	}

	/// <summary>Verifies records spanning multiple bounded segments are reassembled exactly once.</summary>
	[Fact]
	public async Task ReassemblesMultiSegmentRecord() {
		using var input = new MemoryStream(
			"abcdefghij\n"u8.ToArray(),
			writable: false
		);
		using var reader = new ByteRecordReader(
			input,
			bufferSize: 4
		);
		var record = await reader.ReadAsync();
		Assert.NotNull( record );
		Assert.Equal(
			"abcdefghij"u8.ToArray(),
			record.Value.Content.ToArray()
		);
		Assert.True( record.Value.IsTerminated );
	}

	/// <summary>Verifies empty, consecutive, and final unterminated records remain distinguishable.</summary>
	[Fact]
	public async Task PreservesEmptyAndUnterminatedRecords() {
		using var input = new MemoryStream(
			"\n\ntail"u8.ToArray(),
			writable: false
		);
		using var reader = new ByteRecordReader(
			input,
			bufferSize: 2
		);
		var first = await reader.ReadAsync();
		var second = await reader.ReadAsync();
		var third = await reader.ReadAsync();
		Assert.NotNull( first );
		Assert.Empty( first.Value.Content.ToArray() );
		Assert.True( first.Value.IsTerminated );
		Assert.NotNull( second );
		Assert.Empty( second.Value.Content.ToArray() );
		Assert.True( second.Value.IsTerminated );
		Assert.NotNull( third );
		Assert.Equal( "tail"u8.ToArray(), third.Value.Content.ToArray() );
		Assert.False( third.Value.IsTerminated );
	}

	/// <summary>Verifies NUL-delimited records retain embedded newlines as data.</summary>
	[Fact]
	public async Task ReadsNullDelimitedRecords() {
		using var input = new MemoryStream(
			"a\nb\0tail\0"u8.ToArray(),
			writable: false
		);
		using var reader = new ByteRecordReader(
			input,
			separator: 0,
			bufferSize: 3
		);
		var first = await reader.ReadAsync();
		var second = await reader.ReadAsync();
		Assert.NotNull( first );
		Assert.Equal( "a\nb"u8.ToArray(), first.Value.Content.ToArray() );
		Assert.True( first.Value.IsTerminated );
		Assert.NotNull( second );
		Assert.Equal( "tail"u8.ToArray(), second.Value.Content.ToArray() );
		Assert.True( second.Value.IsTerminated );
	}
}
