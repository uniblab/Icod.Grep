namespace Icod.Grep.Tests;

using Icod.CommandFramework.Diagnostics;
using Xunit;

/// <summary>Protects colored selected-record and context rendering while prepared input is reused.</summary>
public sealed class ColorPreparedInputCommandTests {
	/// <summary>Verifies inverted selection retains exact context-match highlighting.</summary>
	[Fact]
	public async Task PreservesColoredInvertedSelectionAndContextMatches() {
		var previousColors = Environment.GetEnvironmentVariable( "GREP_COLORS" );
		try {
			Environment.SetEnvironmentVariable(
				"GREP_COLORS",
				"ms=31:mc=34:sl=:cx=:fn=35:ln=32:bn=33:se=36:ne"
			);
			using var input = new MemoryStream(
				"before TARGET context\nselected ordinary record\nafter TARGET context\n"u8.ToArray(),
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
				[ "--color=always", "-v", "-B", "1", "-A", "1", "TARGET" ],
				context
			);

			Assert.Equal( CommandExitCodes.Success, status );
			Assert.Equal(
				"before \u001b[34mTARGET\u001b[m context\nselected ordinary record\nafter \u001b[34mTARGET\u001b[m context\n"u8.ToArray(),
				output.ToArray()
			);
			Assert.Empty( error.ToString() );
		} finally {
			Environment.SetEnvironmentVariable(
				"GREP_COLORS",
				previousColors
			);
		}
	}
}
