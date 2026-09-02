namespace Icod.Grep.Tests;

using Icod.CommandFramework.Diagnostics;
using Xunit;

public sealed class ColorTests {
	[Fact]
	public void ParsesGnuColorCapabilities() {
		var profile = GrepColorProfile.Resolve(
			"mt=34:ms=31:sl=42:cx=43:fn=35:ln=32:bn=33:se=36:rv:ne",
			null,
			out var warning
		);
		Assert.Null( warning );
		Assert.Equal( "31", profile.SelectedMatch );
		Assert.Equal( "34", profile.ContextMatch );
		Assert.Equal( "42", profile.SelectedLine );
		Assert.Equal( "43", profile.ContextLine );
		Assert.Equal( "35", profile.FileName );
		Assert.Equal( "32", profile.LineNumber );
		Assert.Equal( "33", profile.ByteOffset );
		Assert.Equal( "36", profile.Separator );
		Assert.True( profile.ReverseSelectedContext );
		Assert.True( profile.NoErase );
	}

	[Fact]
	public void AppliesAutoColorPolicy() {
		Assert.True( GrepColorProfile.ShouldEnableAutoColor( true, null ) );
		Assert.True( GrepColorProfile.ShouldEnableAutoColor( true, "xterm-256color" ) );
		Assert.False( GrepColorProfile.ShouldEnableAutoColor( true, "dumb" ) );
		Assert.False( GrepColorProfile.ShouldEnableAutoColor( false, "xterm-256color" ) );
	}

	[Fact]
	public async Task HonorsConfiguredColorCapabilities() {
		using var environment = new EnvironmentScope( "ms=31:sl=42:fn=35:ln=32:bn=33:se=36:ne", null );
		var result = await RunAsync( [ "--color=always", "-H", "-n", "-b", "hit" ], "a hit b\n"u8.ToArray() );
		Assert.Equal(
			"\u001b[35m(standard input)\u001b[m\u001b[36m:\u001b[m\u001b[32m1\u001b[m\u001b[36m:\u001b[m\u001b[33m0\u001b[m\u001b[36m:\u001b[m\u001b[42ma \u001b[31mhit\u001b[m\u001b[42m b\u001b[m\n"u8.ToArray(),
			result.Output
		);
	}

	[Fact]
	public async Task UsesLegacyGrepColorWithWarning() {
		using var environment = new EnvironmentScope( null, "32" );
		var result = await RunAsync( [ "--color=always", "hit" ], "a hit b\n"u8.ToArray() );
		Assert.Contains( "GREP_COLOR='32' is deprecated", result.Error );
		Assert.Contains( "GREP_COLORS='mt=32'", result.Error );
	}

	private static async Task<(int Status, byte[] Output, string Error)> RunAsync( string[] args, byte[] input ) {
		using var inputStream = new MemoryStream( input, writable: false );
		using var outputStream = new MemoryStream();
		var error = new StringWriter();
		var context = new CommandContext(
			"grep", new StringReader( string.Empty ), new StringWriter(), error,
			inputStream, outputStream
		);
		var status = await Command.RunAsync( args, context );
		return ( status, outputStream.ToArray(), error.ToString() );
	}

	private sealed class EnvironmentScope : IDisposable {
		private readonly string? previousColors;
		private readonly string? previousColor;

		public EnvironmentScope( string? colors, string? color ) {
			previousColors = Environment.GetEnvironmentVariable( "GREP_COLORS" );
			previousColor = Environment.GetEnvironmentVariable( "GREP_COLOR" );
			Environment.SetEnvironmentVariable( "GREP_COLORS", colors );
			Environment.SetEnvironmentVariable( "GREP_COLOR", color );
		}

		public void Dispose() {
			Environment.SetEnvironmentVariable( "GREP_COLORS", previousColors );
			Environment.SetEnvironmentVariable( "GREP_COLOR", previousColor );
		}
	}
}
