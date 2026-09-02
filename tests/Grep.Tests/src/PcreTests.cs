namespace Icod.Grep.Tests;

using System.Text;
using Icod.CommandFramework.Diagnostics;
using Xunit;

/// <summary>Tests GNU grep -P behavior backed by PCRE2.</summary>
public sealed class PcreTests {
	[Fact]
	public async Task SupportsLookbehindAndBackreferences() {
		using var environment = new LocaleEnvironmentScope( "C.UTF-8" );
		var lookbehind = await RunAsync( [ "-P", "(?<=foo)bar" ], "foobar\nfoobaz\n"u8.ToArray() );
		Assert.Equal( CommandExitCodes.Success, lookbehind.Status );
		Assert.Equal( "foobar\n"u8.ToArray(), lookbehind.Output );

		var backreference = await RunAsync( [ "-P", @"^(\w+)\s+\1$" ], "same same\nnot same\n"u8.ToArray() );
		Assert.Equal( CommandExitCodes.Success, backreference.Status );
		Assert.Equal( "same same\n"u8.ToArray(), backreference.Output );
	}

	[Fact]
	public async Task KeepsBackslashDAsciiUnderUtf8Ucp() {
		using var environment = new LocaleEnvironmentScope( "C.UTF-8" );
		var arabicIndicDigit = Encoding.UTF8.GetBytes( "٣\n3\n" );
		var slashD = await RunAsync( [ "-P", @"^\d$" ], arabicIndicDigit );
		Assert.Equal( "3\n"u8.ToArray(), slashD.Output );

		var posixDigit = await RunAsync( [ "-P", "^[[:digit:]]$" ], arabicIndicDigit );
		Assert.Equal( arabicIndicDigit, posixDigit.Output );
	}

	[Fact]
	public async Task SupportsUnicodePropertiesInUtf8Locale() {
		using var environment = new LocaleEnvironmentScope( "C.UTF-8" );
		var source = Encoding.UTF8.GetBytes( "é\n7\n" );
		var result = await RunAsync( [ "-P", @"^\p{L}+$" ], source );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( Encoding.UTF8.GetBytes( "é\n" ), result.Output );
	}

	[Fact]
	public async Task PreservesByteModeInCLocale() {
		using var environment = new LocaleEnvironmentScope( "C" );
		var source = new byte[] { 0xFF, (byte)'\n', (byte)'A', (byte)'\n' };
		var result = await RunAsync( [ "-P", "\\xFF" ], source );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( new byte[] { 0xFF, (byte)'\n' }, result.Output );
	}

	[Fact]
	public async Task CooperatesWithOnlyMatchingAndIgnoreCase() {
		using var environment = new LocaleEnvironmentScope( "C.UTF-8" );
		var result = await RunAsync( [ "-Pio", "(?<=foo)bar" ], "xxFOObArzz\n"u8.ToArray() );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "bAr\n"u8.ToArray(), result.Output );
	}

	[Fact]
	public async Task InvalidPatternReturnsUsageError() {
		using var environment = new LocaleEnvironmentScope( "C.UTF-8" );
		var result = await RunAsync( [ "-P", "(" ], "x\n"u8.ToArray() );
		Assert.Equal( CommandExitCodes.UsageError, result.Status );
		Assert.NotEmpty( result.Error );
	}

	private static async Task<(int Status, byte[] Output, string Error)> RunAsync( string[] args, byte[] input ) {
		using var inputStream = new MemoryStream( input, writable: false );
		using var outputStream = new MemoryStream();
		var error = new StringWriter();
		var context = new CommandContext(
			"grep",
			new StringReader( string.Empty ),
			new StringWriter(),
			error,
			inputStream,
			outputStream
		);
		var status = await Command.RunAsync( args, context );
		return ( status, outputStream.ToArray(), error.ToString() );
	}

	private sealed class LocaleEnvironmentScope : IDisposable {
		private readonly string? lcAll = Environment.GetEnvironmentVariable( "LC_ALL" );
		private readonly string? lcCtype = Environment.GetEnvironmentVariable( "LC_CTYPE" );
		private readonly string? lcCollate = Environment.GetEnvironmentVariable( "LC_COLLATE" );
		private readonly string? lang = Environment.GetEnvironmentVariable( "LANG" );

		public LocaleEnvironmentScope( string value ) {
			Environment.SetEnvironmentVariable( "LC_ALL", value );
			Environment.SetEnvironmentVariable( "LC_CTYPE", null );
			Environment.SetEnvironmentVariable( "LC_COLLATE", null );
			Environment.SetEnvironmentVariable( "LANG", null );
		}

		public void Dispose() {
			Environment.SetEnvironmentVariable( "LC_ALL", lcAll );
			Environment.SetEnvironmentVariable( "LC_CTYPE", lcCtype );
			Environment.SetEnvironmentVariable( "LC_COLLATE", lcCollate );
			Environment.SetEnvironmentVariable( "LANG", lang );
		}
	}
}
