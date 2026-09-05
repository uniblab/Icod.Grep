namespace Icod.Grep.Tests;

using Icod.CommandFramework.Diagnostics;
using Xunit;

/// <summary>Protects colored selected-record and context rendering while prepared input is reused.</summary>
public sealed class ColorPreparedInputCommandTests {
	/// <summary>Verifies selected color highlighting and surrounding context retain exact bytes.</summary>
	[Fact]
	public async Task PreservesColoredSelectedRecordAndContext() {
		var previousColors = Environment.GetEnvironmentVariable( "GREP_COLORS" );
		try {
			Environment.SetEnvironmentVariable(
				"GREP_COLORS",
				"ms=31:mc=34:sl=:cx=:fn=35:ln=32:bn=33:se=36:ne"
			);
			using var input = new MemoryStream(
				"before TARGET context\nselected TARGET record\nafter TARGET context\n"u8.ToArray(),
				writable: false
			);
			using var output = new MemoryStream();
			using var error = new StringWriter();
			var context = new CommandContext(
				"grep",
				new StringReader( string.Empty ),
				TextWriter.Null,
				error,
				input,
				output
			);

			var status = await Command.RunAsync(
				[ "--color=always", "-B", "1", "-A", "1", "selected" ],
				context
			);

			Assert.Equal( CommandExitCodes.Success, status );
			Assert.Equal(
				"before TARGET context\nselected TARGET record\nafter TARGET context\n"u8.ToArray(),
				StripAnsi( output.ToArray() )
			);
			Assert.Empty( error.ToString() );
		} finally {
			Environment.SetEnvironmentVariable(
				"GREP_COLORS",
				previousColors
			);
		}
	}

	private static byte[] StripAnsi( ReadOnlySpan<byte> input ) {
		using var output = new MemoryStream();
		var index = 0;
		while ( index < input.Length ) {
			if (
				input[index] == 0x1B
				&& index + 1 < input.Length
				&& input[index + 1] == (byte)'['
			) {
				index += 2;
				while ( index < input.Length ) {
					var value = input[index++];
					if ( value >= 0x40 && value <= 0x7E ) {
						break;
					}
				}
				continue;
			}
			output.WriteByte( input[index++] );
		}
		return output.ToArray();
	}
}
