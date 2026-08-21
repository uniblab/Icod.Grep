namespace Icod.Grep.Tests;

using System.Text;
using Icod.CommandFramework.Diagnostics;
using Xunit;

/// <summary>Tests GNU grep pattern modes, byte records, traversal, output controls, diagnostics, and status semantics.</summary>
public sealed class CommandTests {
	/// <summary>Verifies Basic regular expressions select records and return the required zero-or-one result status.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task SearchesWithBasicRegularExpressionsAndConventionalStatuses() {
		var matched = await RunAsync( [ "a.*a" ], "alpha\nbeta\n"u8.ToArray() );
		var unmatched = await RunAsync( [ "missing" ], "alpha\n"u8.ToArray() );
		Assert.Equal( CommandExitCodes.Success, matched.Status );
		Assert.Equal( "alpha\n"u8.ToArray(), matched.Output );
		Assert.Equal( CommandExitCodes.Failure, unmatched.Status );
		Assert.Empty( unmatched.Output );
	}

	/// <summary>Verifies Extended and fixed-string pattern modes retain their distinct grammars.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task SupportsExtendedAndFixedPatternModes() {
		var extended = await RunAsync( [ "-E", "alpha|beta" ], "beta\ngamma\n"u8.ToArray() );
		var fixedStrings = await RunAsync( [ "-F", "a.b" ], "axb\na.b\n"u8.ToArray() );
		Assert.Equal( "beta\n"u8.ToArray(), extended.Output );
		Assert.Equal( "a.b\n"u8.ToArray(), fixedStrings.Output );
	}

	/// <summary>Verifies a numeric-looking value supplied to an option remains that option's value rather than legacy context syntax.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task PreservesNumericLookingOptionValues() {
		var shortOption = await RunAsync( [ "-e", "-1" ], "-1\nother\n"u8.ToArray() );
		Assert.Equal( "-1\n"u8.ToArray(), shortOption.Output );

		var abbreviatedLongOption = await RunAsync( [ "--reg", "-1" ], "-1\nother\n"u8.ToArray() );
		Assert.Equal( "-1\n"u8.ToArray(), abbreviatedLongOption.Output );
	}

	/// <summary>Verifies an attached short-option value does not hide a following legacy numeric context option.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task RecognizesLegacyContextAfterAttachedPatternValue() {
		var result = await RunAsync( [ "-efoo", "-1" ], "foo\nbar\n"u8.ToArray() );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "foo\nbar\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies multiple expression and pattern-file sources are combined in encounter order.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task CombinesExpressionAndPatternFileSources() {
		var directory = CreateTemporaryDirectory();
		try {
			var patternFile = System.IO.Path.Combine( directory, "patterns.txt" );
			await File.WriteAllTextAsync( patternFile, "gamma\n" );
			var result = await RunAsync(
				[ "-e", "alpha", "-f", patternFile ],
				"beta\ngamma\nalpha\n"u8.ToArray()
			);
			Assert.Equal( "gamma\nalpha\n"u8.ToArray(), result.Output );
		} finally {
			Directory.Delete( directory, recursive: true );
		}
	}

	/// <summary>Verifies a UTF-8 byte-order mark in a pattern file remains part of the first GNU pattern.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task PreservesPatternFileByteOrderMarksAsPatternData() {
		var directory = CreateTemporaryDirectory();
		try {
			var patternFile = System.IO.Path.Combine( directory, "patterns.txt" );
			await File.WriteAllBytesAsync( patternFile, [ 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i', (byte)'t', (byte)'\n' ] );
			var result = await RunAsync(
				[ "-f", patternFile ],
				[ (byte)'h', (byte)'i', (byte)'t', (byte)'\n', 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i', (byte)'t', (byte)'\n' ]
			);
			Assert.Equal( new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i', (byte)'t', (byte)'\n' }, result.Output );
		} finally {
			Directory.Delete( directory, recursive: true );
		}
	}

	/// <summary>Verifies an empty pattern file supplies no patterns while an explicitly empty expression selects every record.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task DistinguishesEmptyPatternFilesFromEmptyExpressions() {
		var directory = CreateTemporaryDirectory();
		try {
			var patternFile = System.IO.Path.Combine( directory, "empty.txt" );
			await File.WriteAllBytesAsync( patternFile, [] );
			var fromFile = await RunAsync( [ "-f", patternFile ], "alpha\n"u8.ToArray() );
			var fromExpression = await RunAsync( [ "-e", string.Empty ], "alpha\n"u8.ToArray() );
			Assert.Equal( CommandExitCodes.Failure, fromFile.Status );
			Assert.Empty( fromFile.Output );
			Assert.Equal( CommandExitCodes.Success, fromExpression.Status );
			Assert.Equal( "alpha\n"u8.ToArray(), fromExpression.Output );
		} finally {
			Directory.Delete( directory, recursive: true );
		}
	}

	/// <summary>Verifies case folding, whole-word matching, whole-record matching, and inversion.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task AppliesRecordSelectionModifiers() {
		var words = await RunAsync( [ "-yw", "cat" ], "Cat\nscatter\ncat!\n"u8.ToArray() );
		var whole = await RunAsync( [ "-x", "cat" ], "cat\ncat!\n"u8.ToArray() );
		var inverted = await RunAsync( [ "-v", "cat" ], "cat\ndog\n"u8.ToArray() );
		Assert.Equal( "Cat\ncat!\n"u8.ToArray(), words.Output );
		Assert.Equal( "cat\n"u8.ToArray(), whole.Output );
		Assert.Equal( "dog\n"u8.ToArray(), inverted.Output );
	}

	/// <summary>Verifies only-matching output carries record numbers and source-byte offsets.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task ReportsLineAndByteOffsetsForOnlyMatches() {
		var result = await RunAsync( [ "-nbo", "foo" ], "zero\nfoo foo\n"u8.ToArray() );
		Assert.Equal( "2:5:foo\n2:9:foo\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies only-matching combined with inversion selects records for status without printing whole records.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task InvertedOnlyMatchingSuppressesRecordOutput() {
		var result = await RunAsync( [ "-ov", "x" ], "x\ny\n"u8.ToArray() );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Empty( result.Output );
	}

	/// <summary>Verifies count, maximum-count, and quiet modes suppress ordinary selected-record output.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task SupportsCountMaximumAndQuietModes() {
		var count = await RunAsync( [ "-c", "-m", "2", "x" ], "x\nx\nx\n"u8.ToArray() );
		var quiet = await RunAsync( [ "-q", "x" ], "x\ny\n"u8.ToArray() );
		var quietCount = await RunAsync( [ "-q", "-c", "x" ], "y\n"u8.ToArray() );
		var quietWithoutMatch = await RunAsync( [ "-q", "-L", "x" ], "y\n"u8.ToArray() );
		Assert.Equal( "2\n"u8.ToArray(), count.Output );
		Assert.Equal( CommandExitCodes.Success, quiet.Status );
		Assert.Empty( quiet.Output );
		Assert.Equal( CommandExitCodes.Failure, quietCount.Status );
		Assert.Empty( quietCount.Output );
		Assert.Equal( CommandExitCodes.Failure, quietWithoutMatch.Status );
		Assert.Empty( quietWithoutMatch.Output );
	}

	/// <summary>Verifies a maximum count of negative one is accepted as unlimited.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task AcceptsNegativeOneMaximumCountAsUnlimited() {
		var result = await RunAsync( [ "-m", "-1", "x" ], "x\nx\nx\n"u8.ToArray() );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "x\nx\nx\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies a seekable standard input is repositioned after the last selected record when maximum-count stops early.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task MaximumCountRestoresSeekableStandardInputPosition() {
		using var input = new MemoryStream( "x\none\ntwo\nthree\n"u8.ToArray(), writable: false );
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
		var status = await Command.RunAsync( [ "-m", "1", "-A", "2", "x" ], context );
		Assert.Equal( CommandExitCodes.Success, status );
		Assert.Equal( "x\none\ntwo\n"u8.ToArray(), output.ToArray() );
		Assert.Equal( 2L, input.Position );
	}

	/// <summary>Verifies file-list modes print only the files selected by their inverse result policies.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task ListsFilesWithOrWithoutSelectedRecords() {
		var directory = CreateTemporaryDirectory();
		try {
			var matching = System.IO.Path.Combine( directory, "matching.txt" );
			var missing = System.IO.Path.Combine( directory, "missing.txt" );
			await File.WriteAllTextAsync( matching, "hit\n" );
			await File.WriteAllTextAsync( missing, "miss\n" );
			var withMatches = await RunAsync( [ "-l", "hit", matching, missing ], [] );
			var withoutMatches = await RunAsync( [ "-L", "hit", matching, missing ], [] );
			Assert.Equal( CommandExitCodes.Success, withMatches.Status );
			Assert.Equal( string.Concat( matching, "\n" ), Encoding.UTF8.GetString( withMatches.Output ) );
			Assert.Equal( CommandExitCodes.Success, withoutMatches.Status );
			Assert.Equal( string.Concat( missing, "\n" ), Encoding.UTF8.GetString( withoutMatches.Output ) );
		} finally {
			Directory.Delete( directory, recursive: true );
		}
	}

	/// <summary>Verifies files-without-match preserves line-selection status independently of its inverse filename output.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task FilesWithoutMatchPreservesSelectionStatus() {
		var directory = CreateTemporaryDirectory();
		try {
			var matching = System.IO.Path.Combine( directory, "matching.txt" );
			var missing = System.IO.Path.Combine( directory, "missing.txt" );
			await File.WriteAllTextAsync( matching, "hit\n" );
			await File.WriteAllTextAsync( missing, "miss\n" );
			var selected = await RunAsync( [ "-L", "hit", matching ], [] );
			var unselected = await RunAsync( [ "-L", "hit", missing ], [] );
			Assert.Equal( CommandExitCodes.Success, selected.Status );
			Assert.Empty( selected.Output );
			Assert.Equal( CommandExitCodes.Failure, unselected.Status );
			Assert.Equal( string.Concat( missing, "\n" ), Encoding.UTF8.GetString( unselected.Output ) );
		} finally {
			Directory.Delete( directory, recursive: true );
		}
	}

	/// <summary>Verifies NUL-delimited records preserve their delimiter and treat embedded newlines as ordinary regex data.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task SearchesNullDelimitedRecords() {
		var literal = await RunAsync( [ "-z", "hit" ], "one\0hit\0tail"u8.ToArray() );
		var multiline = await RunAsync( [ "-z", "one.two" ], "one\ntwo\0other\0"u8.ToArray() );
		Assert.Equal( "hit\0"u8.ToArray(), literal.Output );
		Assert.Equal( "one\ntwo\0"u8.ToArray(), multiline.Output );
	}

	/// <summary>Verifies binary, text, and without-match policies handle NUL-bearing data distinctly.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task AppliesBinaryInputPolicies() {
		var input = new byte[] { (byte)'a', 0, (byte)'h', (byte)'i', (byte)'t', (byte)'\n' };
		var binary = await RunAsync( [ "hit" ], input );
		var text = await RunAsync( [ "-a", "hit" ], input );
		var ignored = await RunAsync( [ "-I", "hit" ], input );
		Assert.Empty( binary.Output );
		Assert.Contains( "grep: (standard input): binary file matches", binary.Error );
		Assert.Equal( input, text.Output );
		Assert.Equal( CommandExitCodes.Failure, ignored.Status );
		Assert.Empty( ignored.Output );
	}

	/// <summary>Verifies binary probing suppresses text selected earlier in the same initial input buffer.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task DetectsBinaryInputBeforeWritingInitialTextMatches() {
		var input = "hit\nlater\0miss\n"u8.ToArray();
		var result = await RunAsync( [ "hit" ], input );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Empty( result.Output );
		Assert.Contains( "grep: (standard input): binary file matches", result.Error );
	}

	/// <summary>Verifies selected unterminated input records receive the configured output record delimiter.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task TerminatesSelectedUnterminatedRecords() {
		var line = await RunAsync( [ "hit" ], "hit"u8.ToArray() );
		var nul = await RunAsync( [ "-z", "hit" ], "hit"u8.ToArray() );
		Assert.Equal( "hit\n"u8.ToArray(), line.Output );
		Assert.Equal( "hit\0"u8.ToArray(), nul.Output );
	}

	/// <summary>Verifies leading and trailing context groups use selected-versus-context prefixes and separators.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task EmitsContextGroups() {
		var result = await RunAsync(
			[ "-n", "-B", "1", "-A", "1", "hit" ],
			"a\nbefore\nhit\nafter\nx\ny\nhit\nz\n"u8.ToArray()
		);
		Assert.Equal(
			"2-before\n3:hit\n4-after\n--\n6-y\n7:hit\n8-z\n"u8.ToArray(),
			result.Output
		);
	}

	/// <summary>Verifies context mode separates matching output groups from distinct files.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task SeparatesContextOutputAcrossFiles() {
		var directory = CreateTemporaryDirectory();
		try {
			var first = System.IO.Path.Combine( directory, "first.txt" );
			var second = System.IO.Path.Combine( directory, "second.txt" );
			await File.WriteAllTextAsync( first, "hit\n" );
			await File.WriteAllTextAsync( second, "hit\n" );
			var result = await RunAsync( [ "-n", "-C", "0", "hit", first, second ], [] );
			var onlyMatching = await RunAsync( [ "-o", "-C", "0", "hit", first, second ], [] );
			Assert.Equal(
				string.Concat( first, ":1:hit\n--\n", second, ":1:hit\n" ),
				Encoding.UTF8.GetString( result.Output )
			);
			Assert.Equal(
				string.Concat( first, ":hit\n--\n", second, ":hit\n" ),
				Encoding.UTF8.GetString( onlyMatching.Output )
			);
		} finally {
			Directory.Delete( directory, recursive: true );
		}
	}

	/// <summary>Verifies maximum-count still emits the requested trailing context without selecting later matches.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task MaximumCountRetainsTrailingContext() {
		var result = await RunAsync(
			[ "-n", "-m", "1", "-A", "2", "x" ],
			"x\nx\ny\nz\n"u8.ToArray()
		);
		Assert.Equal( "1:x\n2-x\n3-y\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies legacy numeric context syntax and a custom group separator.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task SupportsLegacyNumericContextSyntax() {
		var result = await RunAsync(
			[ "-1", "--group-separator=SEP", "hit" ],
			"a\nhit\nb\nc\nd\nhit\ne\n"u8.ToArray()
		);
		Assert.Equal( "a\nhit\nb\nSEP\nd\nhit\ne\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies filename prefixes, labels, and NUL filename delimiters are byte-preserving.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task ControlsFilenameMetadata() {
		var labeled = await RunAsync( [ "-H", "--label=input", "hit" ], "hit\n"u8.ToArray() );
		var nul = await RunAsync( [ "-H", "-Z", "hit" ], "hit\n"u8.ToArray() );
		var aligned = await RunAsync( [ "-nT", "hit" ], "hit\nmissx\n"u8.ToArray() );
		Assert.Equal( "input:hit\n"u8.ToArray(), labeled.Output );
		Assert.Equal( "(standard input)\0hit\n"u8.ToArray(), nul.Output );
		Assert.Equal( " 1:\thit\n"u8.ToArray(), aligned.Output );
	}

	/// <summary>Verifies recursive traversal consumes include, exclude, and directory-pruning rules.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task RecursesWithIncludeExcludeAndDirectoryPruning() {
		var directory = CreateTemporaryDirectory();
		try {
			var skipped = System.IO.Path.Combine( directory, "skip" );
			Directory.CreateDirectory( skipped );
			await File.WriteAllTextAsync( System.IO.Path.Combine( directory, "alpha.txt" ), "hit\n" );
			await File.WriteAllTextAsync( System.IO.Path.Combine( directory, "beta.log" ), "hit\n" );
			await File.WriteAllTextAsync( System.IO.Path.Combine( skipped, "inside.txt" ), "hit\n" );
			var result = await RunAsync(
				[ "-r", "--include=*.txt", "--exclude-dir=skip", "hit", directory ],
				[]
			);
			var output = Encoding.UTF8.GetString( result.Output );
			Assert.Equal( CommandExitCodes.Success, result.Status );
			Assert.Contains( "alpha.txt", output );
			Assert.DoesNotContain( "beta.log", output );
			Assert.DoesNotContain( "inside.txt", output );
		} finally {
			Directory.Delete( directory, recursive: true );
		}
	}

	/// <summary>Verifies ordered include and exclude rules apply to command-line file operands by pathname suffix.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task AppliesOrderedFileRulesToCommandLineOperands() {
		var directory = CreateTemporaryDirectory();
		try {
			var log = System.IO.Path.Combine( directory, "note.log" );
			var temporary = System.IO.Path.Combine( directory, "cache.tmp" );
			var text = System.IO.Path.Combine( directory, "keep.txt" );
			await File.WriteAllTextAsync( log, "hit\n" );
			await File.WriteAllTextAsync( temporary, "hit\n" );
			await File.WriteAllTextAsync( text, "hit\n" );
			var result = await RunAsync(
				[ "--exclude=*.tmp", "--include=*.txt", "hit", log, temporary, text ],
				[]
			);
			Assert.Equal( CommandExitCodes.Success, result.Status );
			Assert.Equal(
				string.Concat( log, ":hit\n", text, ":hit\n" ),
				Encoding.UTF8.GetString( result.Output )
			);
		} finally {
			Directory.Delete( directory, recursive: true );
		}
	}

	/// <summary>Verifies exclude-directory patterns apply to command-line directory operands and ignore trailing separators.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task ExcludesCommandLineDirectoryOperandsBySuffix() {
		var directory = CreateTemporaryDirectory();
		try {
			var skipped = System.IO.Path.Combine( directory, "skip" );
			Directory.CreateDirectory( skipped );
			await File.WriteAllTextAsync( System.IO.Path.Combine( skipped, "inside.txt" ), "hit\n" );
			var operand = string.Concat( skipped, System.IO.Path.DirectorySeparatorChar );
			var result = await RunAsync( [ "-r", "--exclude-dir=skip/", "hit", operand ], [] );
			Assert.Equal( CommandExitCodes.Failure, result.Status );
			Assert.Empty( result.Output );
			Assert.Empty( result.Error );
		} finally {
			Directory.Delete( directory, recursive: true );
		}
	}

	/// <summary>Verifies a skipped directory operand is neither opened nor diagnosed.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task SkipsDirectoryOperandsOnRequest() {
		var directory = CreateTemporaryDirectory();
		try {
			var result = await RunAsync( [ "-d", "skip", "hit", directory ], [] );
			Assert.Equal( CommandExitCodes.Failure, result.Status );
			Assert.Empty( result.Error );
		} finally {
			Directory.Delete( directory, recursive: true );
		}
	}

	/// <summary>Verifies missing inputs produce status two and no-messages suppresses only their diagnostic.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task DiagnosesOrSuppressesInputErrors() {
		var path = System.IO.Path.Combine( System.IO.Path.GetTempPath(), string.Concat( "grep-missing-", Guid.NewGuid().ToString( "N" ) ) );
		var diagnosed = await RunAsync( [ "hit", path ], [] );
		var suppressed = await RunAsync( [ "-s", "hit", path ], [] );
		Assert.Equal( CommandExitCodes.UsageError, diagnosed.Status );
		Assert.NotEmpty( diagnosed.Error );
		Assert.Equal( CommandExitCodes.UsageError, suppressed.Status );
		Assert.Empty( suppressed.Error );
	}

	/// <summary>Verifies distinct matcher selectors conflict while repeated selection of one matcher remains valid.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task RejectsConflictingPatternModes() {
		var conflicting = await RunAsync( [ "-E", "-F", "x" ], "x\n"u8.ToArray() );
		var repeated = await RunAsync( [ "-E", "-E", "x" ], "x\n"u8.ToArray() );
		Assert.Equal( CommandExitCodes.UsageError, conflicting.Status );
		Assert.Contains( "conflicting matchers specified", conflicting.Error );
		Assert.Equal( CommandExitCodes.Success, repeated.Status );
	}

	/// <summary>Verifies malformed managed regex syntax and unavailable Perl mode receive controlled status-two diagnostics.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task DiagnosesInvalidOrUnavailablePatternModes() {
		var invalid = await RunAsync( [ "[" ], [] );
		var perl = await RunAsync( [ "-P", "x" ], [] );
		Assert.Equal( CommandExitCodes.UsageError, invalid.Status );
		Assert.NotEmpty( invalid.Error );
		Assert.Equal( CommandExitCodes.UsageError, perl.Status );
		Assert.Contains( "Perl-compatible", perl.Error );
	}

	/// <summary>Verifies forced color highlights only the matched byte ranges.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task ColorsSelectedMatchRangesWhenForced() {
		var result = await RunAsync( [ "--color=always", "hit" ], "a hit b\n"u8.ToArray() );
		Assert.Equal( "a \u001b[01;31m\u001b[Khit\u001b[m\u001b[K b\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies help and version use textual output and successful control-path statuses.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task ControlPathsHaveConventionalStatuses() {
		var help = await RunAsync( [ "--help" ], [] );
		var version = await RunAsync( [ "--version" ], [] );
		Assert.Equal( CommandExitCodes.Success, help.Status );
		Assert.Contains( "Usage: grep", help.TextOutput );
		Assert.Equal( CommandExitCodes.Success, version.Status );
		Assert.Contains( "grep (Icod.Grep)", version.TextOutput );
	}

	/// <summary>Verifies cancellation returns the shared canceled status.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task HonorsCancellation() {
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var result = await RunAsync( [ "hit" ], "hit\n"u8.ToArray(), cancellation.Token );
		Assert.Equal( CommandExitCodes.Canceled, result.Status );
	}

	/// <summary>Verifies output failures become controlled status-two diagnostics.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task ReportsOutputFailures() {
		using var input = new MemoryStream( "hit\n"u8.ToArray(), writable: false );
		using var output = new MemoryStream();
		output.Dispose();
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
		var status = await Command.RunAsync( [ "hit" ], context );
		Assert.Equal( CommandExitCodes.UsageError, status );
		Assert.Contains( "writable", error.ToString() );
	}

	private static string CreateTemporaryDirectory() {
		var path = System.IO.Path.Combine( System.IO.Path.GetTempPath(), string.Concat( "Icod.Grep.Tests-", Guid.NewGuid().ToString( "N" ) ) );
		Directory.CreateDirectory( path );
		return path;
	}

	private static async Task<(int Status, byte[] Output, string TextOutput, string Error)> RunAsync(
		string[] args,
		byte[] input,
		CancellationToken cancellationToken = default
	) {
		using var inputStream = new MemoryStream( input, writable: false );
		using var outputStream = new MemoryStream();
		var textOutput = new StringWriter();
		var error = new StringWriter();
		var context = new CommandContext(
			"grep",
			new StringReader( string.Empty ),
			textOutput,
			error,
			inputStream,
			outputStream,
			null,
			cancellationToken
		);
		var status = await Command.RunAsync( args, context );
		return ( status, outputStream.ToArray(), textOutput.ToString(), error.ToString() );
	}
}
