// Original behavior/reference: GNU grep 3.12
// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.Grep;

using System.Globalization;
using System.Text;
using Icod.CommandFramework.CommandLine;
using Icod.CommandFramework.Diagnostics;
using Icod.CommandFramework.FileSystem.Traversal;
using Icod.CommandFramework.IO;
using Icod.CommandFramework.Records;
using Icod.CommandFramework.RegularExpressions;

/// <summary>Implements GNU-compatible pattern searching over byte-preserving input records.</summary>
public static class Command {
	private const string VersionText = "grep (Icod.Grep) 1.0";
	private const int BinaryProbeLength = 98_304;
	private static readonly byte[] MatchColorStart = "\u001b[01;31m\u001b[K"u8.ToArray();
	private static readonly byte[] MatchColorEnd = "\u001b[m\u001b[K"u8.ToArray();

	private enum PatternMode {
		Basic,
		Extended,
		Fixed,
		Perl
	}

	private enum BinaryFileMode {
		Binary,
		Text,
		WithoutMatch
	}

	private enum DirectoryMode {
		Read,
		Recurse,
		Skip
	}

	private enum DeviceMode {
		Read,
		Skip
	}

	private enum FilenameMode {
		Automatic,
		Always,
		Never
	}

	private enum FileListMode {
		None,
		WithMatches,
		WithoutMatches
	}

	private enum ColorMode {
		Never,
		Auto,
		Always
	}

	private enum PatternSourceKind {
		Expression,
		File
	}

	private sealed record PatternSource( PatternSourceKind Kind, string Value );
	private sealed record MatchSpan( int Index, int Length );
	private sealed record InputSource( string AccessPath, string DisplayName, bool IsStandardInput );
	private sealed record LineRecord( byte[] Content, bool IsTerminated, long LineNumber, long ByteOffset );
	private sealed record SourceResult( bool HasSelectedRecord, bool StopCommand, bool WroteRecordOutput );
	private sealed record PathRule( bool Include, PathnamePattern Pattern );

	private sealed class GrepOptions {
		public PatternMode PatternMode { get; set; } = PatternMode.Basic;
		public bool IgnoreCase { get; set; }
		public bool InvertMatch { get; set; }
		public bool WordRegexp { get; set; }
		public bool LineRegexp { get; set; }
		public bool NullData { get; set; }
		public bool NoMessages { get; set; }
		public long? MaximumCount { get; set; }
		public bool ByteOffset { get; set; }
		public bool LineNumber { get; set; }
		public bool LineBuffered { get; set; }
		public FilenameMode FilenameMode { get; set; } = FilenameMode.Automatic;
		public string StandardInputLabel { get; set; } = "(standard input)";
		public bool OnlyMatching { get; set; }
		public bool Quiet { get; set; }
		public BinaryFileMode BinaryFileMode { get; set; } = BinaryFileMode.Binary;
		public DirectoryMode DirectoryMode { get; set; } = DirectoryMode.Read;
		public DeviceMode DeviceMode { get; set; } = DeviceMode.Read;
		public SymbolicLinkTraversalMode SymbolicLinkMode { get; set; } = SymbolicLinkTraversalMode.RootsOnly;
		public FileListMode FileListMode { get; set; }
		public bool CountOnly { get; set; }
		public bool InitialTab { get; set; }
		public bool NullFilename { get; set; }
		public int BeforeContext { get; set; }
		public int AfterContext { get; set; }
		public bool ContextRequested { get; set; }
		public string? GroupSeparator { get; set; } = "--";
		public ColorMode ColorMode { get; set; }
		public List<PatternSource> PatternSources { get; } = new();
		public List<string> Operands { get; } = new();
		public List<PathRule> FileRules { get; } = new();
		public List<PathnamePattern> ExcludeDirectoryPatterns { get; } = new();
		public bool Recursive => DirectoryMode == DirectoryMode.Recurse;
		public bool ColorEnabled { get; set; }
	}

	private sealed class PrefixReadStream : Stream {
		private readonly ReadOnlyMemory<byte> prefix;
		private readonly Stream source;
		private int prefixOffset;

		public PrefixReadStream( ReadOnlyMemory<byte> prefix, Stream source ) {
			this.prefix = prefix;
			this.source = source;
		}

		public override bool CanRead => this.source.CanRead;
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
			var copied = this.CopyPrefix( buffer );
			return copied > 0 ? copied : this.source.Read( buffer );
		}

		public override ValueTask<int> ReadAsync(
			Memory<byte> buffer,
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			var copied = this.CopyPrefix( buffer.Span );
			return copied > 0
				? ValueTask.FromResult( copied )
				: this.source.ReadAsync( buffer, cancellationToken );
		}

		public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException();
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();

		private int CopyPrefix( Span<byte> destination ) {
			var remaining = this.prefix.Length - this.prefixOffset;
			if ( remaining <= 0 || destination.IsEmpty ) {
				return 0;
			}
			var count = Math.Min( remaining, destination.Length );
			this.prefix.Span.Slice( this.prefixOffset, count ).CopyTo( destination );
			this.prefixOffset += count;
			return count;
		}
	}

	private interface IGrepPattern {
		MatchSpan? Find( ReadOnlyMemory<byte> input, int startOffset, CancellationToken cancellationToken );
	}

	private sealed class RegularExpressionPattern : IGrepPattern {
		private readonly ICompiledRegularExpression expression;
		private readonly RegularExpressionInputOptions inputOptions = new();

		public RegularExpressionPattern( ICompiledRegularExpression expression ) {
			this.expression = expression;
		}

		public MatchSpan? Find( ReadOnlyMemory<byte> input, int startOffset, CancellationToken cancellationToken ) {
			var result = this.expression.Match(
				input,
				this.inputOptions,
				new RegularExpressionByteMatchOptions { StartByteOffset = startOffset },
				cancellationToken
			);
			if ( !result.IsSuccess ) {
				throw new InvalidOperationException( result.Diagnostic?.Message ?? "regular-expression matching failed" );
			}
			return result.Match is null
				? null
				: new MatchSpan( result.Match.ByteIndex, result.Match.ByteLength );
		}
	}

	private sealed class FixedPattern : IGrepPattern {
		private readonly IRegularExpressionCharacterClassProvider characterClassProvider;
		private readonly bool ignoreCase;
		private readonly byte[] pattern;
		private readonly Rune[] patternRunes;

		public FixedPattern(
			string pattern,
			bool ignoreCase,
			IRegularExpressionCharacterClassProvider characterClassProvider
		) {
			this.pattern = Encoding.UTF8.GetBytes( pattern );
			this.patternRunes = pattern.EnumerateRunes().ToArray();
			this.ignoreCase = ignoreCase;
			this.characterClassProvider = characterClassProvider;
		}

		public MatchSpan? Find( ReadOnlyMemory<byte> input, int startOffset, CancellationToken cancellationToken ) {
			cancellationToken.ThrowIfCancellationRequested();
			if ( startOffset < 0 || startOffset > input.Length ) {
				return null;
			}
			if ( this.pattern.Length == 0 ) {
				return new MatchSpan( startOffset, 0 );
			}
			return this.ignoreCase
				? this.FindIgnoringCase( input.Span, startOffset, cancellationToken )
				: this.FindOrdinal( input.Span, startOffset, cancellationToken );
		}

		private MatchSpan? FindIgnoringCase(
			ReadOnlySpan<byte> input,
			int startOffset,
			CancellationToken cancellationToken
		) {
			for ( var index = startOffset; index < input.Length; index++ ) {
				cancellationToken.ThrowIfCancellationRequested();
				var position = index;
				var matches = true;
				foreach ( var expected in this.patternRunes ) {
					if ( position >= input.Length ) {
						matches = false;
						break;
					}
					var status = Rune.DecodeFromUtf8( input[position..], out var actual, out var consumed );
					if (
						status != System.Buffers.OperationStatus.Done
						|| !this.characterClassProvider.AreCharactersEqual( actual, expected, ignoreCase: true )
					) {
						matches = false;
						break;
					}
					position += consumed;
				}
				if ( matches ) {
					return new MatchSpan( index, position - index );
				}
			}
			return null;
		}

		private MatchSpan? FindOrdinal(
			ReadOnlySpan<byte> input,
			int startOffset,
			CancellationToken cancellationToken
		) {
			for ( var index = startOffset; index <= input.Length - this.pattern.Length; index++ ) {
				cancellationToken.ThrowIfCancellationRequested();
				if ( input.Slice( index, this.pattern.Length ).SequenceEqual( this.pattern ) ) {
					return new MatchSpan( index, this.pattern.Length );
				}
			}
			return null;
		}
	}

	private sealed class PatternSet {
		private readonly IReadOnlyList<IGrepPattern> patterns;
		private readonly bool wordRegexp;
		private readonly bool lineRegexp;
		private readonly IRegularExpressionCharacterClassProvider characterClassProvider;

		public PatternSet(
			IReadOnlyList<IGrepPattern> patterns,
			bool wordRegexp,
			bool lineRegexp,
			IRegularExpressionCharacterClassProvider characterClassProvider
		) {
			this.patterns = patterns;
			this.wordRegexp = wordRegexp;
			this.lineRegexp = lineRegexp;
			this.characterClassProvider = characterClassProvider;
		}

		public bool IsEmpty => this.patterns.Count == 0;

		public MatchSpan? Find( ReadOnlyMemory<byte> input, int startOffset, CancellationToken cancellationToken ) {
			MatchSpan? best = null;
			foreach ( var pattern in this.patterns ) {
				var searchOffset = startOffset;
				while ( searchOffset <= input.Length ) {
					var candidate = pattern.Find( input, searchOffset, cancellationToken );
					if ( candidate is null ) {
						break;
					}
					if ( this.Accepts( input.Span, candidate ) ) {
						if (
							best is null
							|| candidate.Index < best.Index
							|| (candidate.Index == best.Index && candidate.Length > best.Length)
						) {
							best = candidate;
						}
						break;
					}
					if ( candidate.Index >= input.Length ) {
						break;
					}
					searchOffset = AdvanceAfter( input.Span, candidate );
				}
			}
			return best;
		}

		public IReadOnlyList<MatchSpan> FindAll( ReadOnlyMemory<byte> input, CancellationToken cancellationToken ) {
			var output = new List<MatchSpan>();
			var offset = 0;
			while ( offset <= input.Length ) {
				var match = this.Find( input, offset, cancellationToken );
				if ( match is null ) {
					break;
				}
				if ( match.Length > 0 ) {
					output.Add( match );
				}
				if ( match.Index >= input.Length ) {
					break;
				}
				offset = AdvanceAfter( input.Span, match );
			}
			return output;
		}

		private static int AdvanceAfter( ReadOnlySpan<byte> input, MatchSpan match ) {
			if ( match.Length > 0 ) {
				return match.Index + match.Length;
			}
			if ( match.Index >= input.Length ) {
				return input.Length + 1;
			}
			var length = GetUtf8SequenceLength( input, match.Index );
			return match.Index + length;
		}

		private static int GetUtf8SequenceLength( ReadOnlySpan<byte> input, int index ) {
			var status = Rune.DecodeFromUtf8( input[index..], out _, out var consumed );
			return status == System.Buffers.OperationStatus.Done ? consumed : 1;
		}

		private bool Accepts( ReadOnlySpan<byte> input, MatchSpan match ) {
			if ( this.lineRegexp && (match.Index != 0 || match.Length != input.Length) ) {
				return false;
			}
			if ( !this.wordRegexp ) {
				return true;
			}
			var beforeWord = match.Index > 0
				&& TryDecodePreviousRune( input, match.Index, out var before )
				&& this.characterClassProvider.IsWordCharacter( before );
			var afterIndex = match.Index + match.Length;
			var afterWord = afterIndex < input.Length
				&& TryDecodeNextRune( input, afterIndex, out var after )
				&& this.characterClassProvider.IsWordCharacter( after );
			return !beforeWord && !afterWord;
		}

		private static bool TryDecodePreviousRune( ReadOnlySpan<byte> input, int index, out Rune value ) =>
			Rune.DecodeLastFromUtf8( input[..index], out value, out _ ) == System.Buffers.OperationStatus.Done;

		private static bool TryDecodeNextRune( ReadOnlySpan<byte> input, int index, out Rune value ) =>
			Rune.DecodeFromUtf8( input[index..], out value, out _ ) == System.Buffers.OperationStatus.Done;
	}

	private sealed class GrepTraversalSelector : IPathTraversalSelector {
		private readonly GrepOptions options;

		public GrepTraversalSelector( GrepOptions options ) {
			this.options = options;
		}

		public PathTraversalSelection Select( PathTraversalEntry entry ) {
			if ( entry.Kind == FileSystemEntryKind.Directory ) {
				foreach ( var pattern in this.options.ExcludeDirectoryPatterns ) {
					if ( pattern.IsMatch( entry.Name ) ) {
						return PathTraversalSelection.ExcludeAll;
					}
				}
				return PathTraversalSelection.IncludeAll;
			}
			if ( entry.IsSymbolicLink && !entry.IsFollowedSymbolicLink ) {
				return PathTraversalSelection.ExcludeAll;
			}
			var selected = this.options.FileRules.Count == 0 || !this.options.FileRules[0].Include;
			foreach ( var rule in this.options.FileRules ) {
				if ( rule.Pattern.IsMatch( entry.Name ) ) {
					selected = rule.Include;
				}
			}
			return new PathTraversalSelection( selected, false );
		}
	}

	private sealed class ExecutionState {
		public bool AnyResult { get; set; }
		public bool HadError { get; set; }
		public bool HasRecordOutput { get; set; }
	}

	/// <summary>Runs <c>grep</c> synchronously with optional injected text streams.</summary>
	/// <param name="args">The command-line arguments.</param>
	/// <param name="standardInput">The standard-input reader.</param>
	/// <param name="standardOutput">The standard-output writer.</param>
	/// <param name="standardError">The standard-error writer.</param>
	/// <returns>The GNU grep exit status: 0 for a selected result, 1 for none, or 2 for an error.</returns>
	public static int Run(
		string[] args,
		TextReader? standardInput = null,
		TextWriter? standardOutput = null,
		TextWriter? standardError = null
	) => RunAsync( args, standardInput, standardOutput, standardError ).GetAwaiter().GetResult();

	/// <summary>Runs <c>grep</c> asynchronously with optional injected text streams.</summary>
	/// <param name="args">The command-line arguments.</param>
	/// <param name="standardInput">The standard-input reader.</param>
	/// <param name="standardOutput">The standard-output writer.</param>
	/// <param name="standardError">The standard-error writer.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>A task whose result is the GNU grep exit status.</returns>
	public static async Task<int> RunAsync(
		string[] args,
		TextReader? standardInput = null,
		TextWriter? standardOutput = null,
		TextWriter? standardError = null,
		CancellationToken cancellationToken = default
	) {
		standardInput ??= Console.In;
		standardOutput ??= Console.Out;
		standardError ??= Console.Error;
		using var inputAdapter = new TextReaderStream( standardInput, leaveOpen: true );
		return await RunAsync(
			args,
			new CommandContext(
				"grep",
				standardInput,
				standardOutput,
				standardError,
				inputAdapter,
				null,
				null,
				cancellationToken
			)
		).ConfigureAwait( false );
	}

	/// <summary>Runs <c>grep</c> asynchronously against a byte-capable command context.</summary>
	/// <param name="args">The command-line arguments.</param>
	/// <param name="context">The command context.</param>
	/// <returns>A task whose result is the GNU grep exit status.</returns>
	public static async Task<int> RunAsync( string[] args, CommandContext context ) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( context );
		TextReaderStream? inputAdapter = null;
		try {
			var parsed = CreateParser().Parse( args );
			if ( !parsed.IsSuccess ) {
				await context.StandardError.WriteLineAsync(
					OptionDiagnosticFormatter.Format( context.ProgramName, parsed.Errors[0] ).AsMemory(),
					context.CancellationToken
				).ConfigureAwait( false );
				return CommandExitCodes.UsageError;
			}
			if ( parsed.HasOption( "help" ) ) {
				await WriteHelpAsync( context ).ConfigureAwait( false );
				return CommandExitCodes.Success;
			}
			if ( parsed.HasOption( "version" ) ) {
				await context.StandardOutput.WriteLineAsync(
					VersionText.AsMemory(),
					context.CancellationToken
				).ConfigureAwait( false );
				return CommandExitCodes.Success;
			}

			var standardInput = context.StandardInputStream;
			if ( standardInput is null ) {
				inputAdapter = new TextReaderStream( context.StandardInput, leaveOpen: true );
				standardInput = inputAdapter;
			}
			await using var output = new ByteOutputStream( context.StandardOutput, context.StandardOutputStream );
			var optionsResult = await TryCreateOptionsAsync(
				parsed,
				standardInput,
				context
			).ConfigureAwait( false );
			if ( optionsResult.Options is null ) {
				return CommandExitCodes.UsageError;
			}
			var options = optionsResult.Options;
			if ( options.PatternMode == PatternMode.Perl ) {
				await ReportErrorAsync(
					options,
					context,
					"Perl-compatible regular expressions are not available in this managed build"
				).ConfigureAwait( false );
				return CommandExitCodes.UsageError;
			}
			var patternSet = await CompilePatternsAsync( options, standardInput, context ).ConfigureAwait( false );
			if ( patternSet is null ) {
				return CommandExitCodes.UsageError;
			}
			var status = await ExecuteAsync(
				options,
				patternSet,
				standardInput,
				output,
				context
			).ConfigureAwait( false );
			await output.CompleteAsync( context.CancellationToken ).ConfigureAwait( false );
			return status;
		} catch ( OperationCanceledException ) {
			return CommandExitCodes.Canceled;
		} catch ( Exception exception ) when (
			exception is IOException
			or UnauthorizedAccessException
			or InvalidOperationException
			or ArgumentException
			or NotSupportedException
			or OverflowException
			or System.Security.SecurityException
		) {
			try {
				await context.Diagnostics.ErrorAsync( exception.Message, CancellationToken.None ).ConfigureAwait( false );
			} catch {
				// A diagnostic failure must not replace the GNU grep error status.
			}
			return CommandExitCodes.UsageError;
		} finally {
			inputAdapter?.Dispose();
		}
	}

	private static OptionParser CreateParser() {
		var settings = new OptionParserSettings {
			AllowLongOptionAbbreviations = true,
			Ordering = OptionOrdering.Permute
		};
		settings.TokenRewriteRules.Add(
			new OptionTokenRewriteRule(
				static token => IsLegacyContextOption( token )
					? new[] { string.Concat( "--context=", token.Substring( 1 ) ) }
					: null
			)
		);

		return new OptionParser(
			new[] {
			new OptionDefinition( "extended-regexp", 'E', new[] { "extended-regexp" } ),
			new OptionDefinition( "fixed-strings", 'F', new[] { "fixed-strings" } ),
			new OptionDefinition( "basic-regexp", 'G', new[] { "basic-regexp" } ),
			new OptionDefinition( "perl-regexp", 'P', new[] { "perl-regexp" } ),
			new OptionDefinition( "regexp", 'e', new[] { "regexp" }, OptionValueArity.Required ),
			new OptionDefinition( "file", 'f', new[] { "file" }, OptionValueArity.Required ),
			new OptionDefinition( "ignore-case", 'i', new[] { "ignore-case" } ),
			new OptionDefinition( "ignore-case-obsolete", 'y' ),
			new OptionDefinition( "no-ignore-case", null, new[] { "no-ignore-case" } ),
			new OptionDefinition( "word-regexp", 'w', new[] { "word-regexp" } ),
			new OptionDefinition( "line-regexp", 'x', new[] { "line-regexp" } ),
			new OptionDefinition( "null-data", 'z', new[] { "null-data" } ),
			new OptionDefinition( "no-messages", 's', new[] { "no-messages" } ),
			new OptionDefinition( "invert-match", 'v', new[] { "invert-match" } ),
			new OptionDefinition( "max-count", 'm', new[] { "max-count" }, OptionValueArity.Required ),
			new OptionDefinition( "byte-offset", 'b', new[] { "byte-offset" } ),
			new OptionDefinition( "line-number", 'n', new[] { "line-number" } ),
			new OptionDefinition( "line-buffered", null, new[] { "line-buffered" } ),
			new OptionDefinition( "with-filename", 'H', new[] { "with-filename" } ),
			new OptionDefinition( "no-filename", 'h', new[] { "no-filename" } ),
			new OptionDefinition( "label", null, new[] { "label" }, OptionValueArity.Required ),
			new OptionDefinition( "only-matching", 'o', new[] { "only-matching" } ),
			new OptionDefinition( "quiet", 'q', new[] { "quiet", "silent" } ),
			new OptionDefinition( "binary-files", null, new[] { "binary-files" }, OptionValueArity.Required ),
			new OptionDefinition( "text", 'a', new[] { "text" } ),
			new OptionDefinition( "binary-without-match", 'I' ),
			new OptionDefinition( "directories", 'd', new[] { "directories" }, OptionValueArity.Required ),
			new OptionDefinition( "devices", 'D', new[] { "devices" }, OptionValueArity.Required ),
			new OptionDefinition( "recursive", 'r', new[] { "recursive" } ),
			new OptionDefinition( "dereference-recursive", 'R', new[] { "dereference-recursive" } ),
			new OptionDefinition( "include", null, new[] { "include" }, OptionValueArity.Required ),
			new OptionDefinition( "exclude", null, new[] { "exclude" }, OptionValueArity.Required ),
			new OptionDefinition( "exclude-from", null, new[] { "exclude-from" }, OptionValueArity.Required ),
			new OptionDefinition( "exclude-dir", null, new[] { "exclude-dir" }, OptionValueArity.Required ),
			new OptionDefinition( "files-without-match", 'L', new[] { "files-without-match" } ),
			new OptionDefinition( "files-with-matches", 'l', new[] { "files-with-matches" } ),
			new OptionDefinition( "count", 'c', new[] { "count" } ),
			new OptionDefinition( "initial-tab", 'T', new[] { "initial-tab" } ),
			new OptionDefinition( "null-filename", 'Z', new[] { "null" } ),
			new OptionDefinition( "before-context", 'B', new[] { "before-context" }, OptionValueArity.Required ),
			new OptionDefinition( "after-context", 'A', new[] { "after-context" }, OptionValueArity.Required ),
			new OptionDefinition( "context-short", 'C', null, OptionValueArity.Required ),
			new OptionDefinition( "context", null, new[] { "context" }, OptionValueArity.Required ),
			new OptionDefinition( "group-separator", null, new[] { "group-separator" }, OptionValueArity.Required ),
			new OptionDefinition( "no-group-separator", null, new[] { "no-group-separator" } ),
			new OptionDefinition( "color", null, new[] { "color", "colour" }, OptionValueArity.Optional ),
			new OptionDefinition( "binary-platform", 'U', new[] { "binary" } ),
			new OptionDefinition( "help", null, new[] { "help" } ),
			new OptionDefinition( "version", 'V', new[] { "version" } )
			},
			settings
		);
	}

	private static bool IsLegacyContextOption( string value ) {
		if ( value.Length <= 1 || value[0] != '-' ) {
			return false;
		}
		for ( var index = 1; index < value.Length; index++ ) {
			if ( value[index] < '0' || value[index] > '9' ) {
				return false;
			}
		}
		return true;
	}

	private static async Task<(GrepOptions? Options, string? Error)> TryCreateOptionsAsync(
		OptionParseResult parsed,
		Stream standardInput,
		CommandContext context
	) {
		var options = new GrepOptions();
		PatternMode? explicitPatternMode = null;
		foreach ( var occurrence in parsed.Options ) {
			var value = occurrence.Value;
			switch ( occurrence.Definition.Key ) {
				case "extended-regexp":
					if ( !TrySetPatternMode( options, ref explicitPatternMode, PatternMode.Extended ) ) {
						await ReportErrorAsync( options, context, "conflicting matchers specified" ).ConfigureAwait( false );
						return (null, "conflicting matchers");
					}
					break;
				case "fixed-strings":
					if ( !TrySetPatternMode( options, ref explicitPatternMode, PatternMode.Fixed ) ) {
						await ReportErrorAsync( options, context, "conflicting matchers specified" ).ConfigureAwait( false );
						return (null, "conflicting matchers");
					}
					break;
				case "basic-regexp":
					if ( !TrySetPatternMode( options, ref explicitPatternMode, PatternMode.Basic ) ) {
						await ReportErrorAsync( options, context, "conflicting matchers specified" ).ConfigureAwait( false );
						return (null, "conflicting matchers");
					}
					break;
				case "perl-regexp":
					if ( !TrySetPatternMode( options, ref explicitPatternMode, PatternMode.Perl ) ) {
						await ReportErrorAsync( options, context, "conflicting matchers specified" ).ConfigureAwait( false );
						return (null, "conflicting matchers");
					}
					break;
				case "regexp":
					options.PatternSources.Add( new PatternSource( PatternSourceKind.Expression, value ?? string.Empty ) );
					break;
				case "file":
					options.PatternSources.Add( new PatternSource( PatternSourceKind.File, value ?? string.Empty ) );
					break;
				case "ignore-case":
				case "ignore-case-obsolete":
					options.IgnoreCase = true;
					break;
				case "no-ignore-case":
					options.IgnoreCase = false;
					break;
				case "word-regexp":
					options.WordRegexp = true;
					break;
				case "line-regexp":
					options.LineRegexp = true;
					break;
				case "null-data":
					options.NullData = true;
					break;
				case "no-messages":
					options.NoMessages = true;
					break;
				case "invert-match":
					options.InvertMatch = true;
					break;
				case "max-count":
					if ( !TryParseMaximumCount( value, out var maximumCount ) ) {
						await ReportErrorAsync( options, context, string.Concat( "invalid max count: ", value ) ).ConfigureAwait( false );
						return (null, "invalid max count");
					}
					options.MaximumCount = maximumCount < 0 ? null : maximumCount;
					break;
				case "byte-offset":
					options.ByteOffset = true;
					break;
				case "line-number":
					options.LineNumber = true;
					break;
				case "line-buffered":
					options.LineBuffered = true;
					break;
				case "with-filename":
					options.FilenameMode = FilenameMode.Always;
					break;
				case "no-filename":
					options.FilenameMode = FilenameMode.Never;
					break;
				case "label":
					options.StandardInputLabel = value ?? string.Empty;
					break;
				case "only-matching":
					options.OnlyMatching = true;
					break;
				case "quiet":
					options.Quiet = true;
					break;
				case "binary-files":
					if ( !TryParseBinaryMode( value, out var binaryMode ) ) {
						await ReportErrorAsync( options, context, string.Concat( "unknown binary-files type: ", value ) ).ConfigureAwait( false );
						return (null, "invalid binary policy");
					}
					options.BinaryFileMode = binaryMode;
					break;
				case "text":
					options.BinaryFileMode = BinaryFileMode.Text;
					break;
				case "binary-without-match":
					options.BinaryFileMode = BinaryFileMode.WithoutMatch;
					break;
				case "directories":
					if ( !TryParseDirectoryMode( value, out var directoryMode ) ) {
						await ReportErrorAsync( options, context, string.Concat( "invalid argument for --directories: ", value ) ).ConfigureAwait( false );
						return (null, "invalid directory policy");
					}
					options.DirectoryMode = directoryMode;
					if ( directoryMode == DirectoryMode.Recurse ) {
						options.SymbolicLinkMode = SymbolicLinkTraversalMode.RootsOnly;
					}
					break;
				case "devices":
					if ( !TryParseDeviceMode( value, out var deviceMode ) ) {
						await ReportErrorAsync( options, context, string.Concat( "invalid argument for --devices: ", value ) ).ConfigureAwait( false );
						return (null, "invalid device policy");
					}
					options.DeviceMode = deviceMode;
					break;
				case "recursive":
					options.DirectoryMode = DirectoryMode.Recurse;
					options.SymbolicLinkMode = SymbolicLinkTraversalMode.RootsOnly;
					break;
				case "dereference-recursive":
					options.DirectoryMode = DirectoryMode.Recurse;
					options.SymbolicLinkMode = SymbolicLinkTraversalMode.Always;
					break;
				case "include":
					options.FileRules.Add( CreatePathRule( true, value ?? string.Empty ) );
					break;
				case "exclude":
					options.FileRules.Add( CreatePathRule( false, value ?? string.Empty ) );
					break;
				case "exclude-from": {
					var patterns = await ReadPatternLinesAsync( value ?? string.Empty, standardInput, context.CancellationToken ).ConfigureAwait( false );
					foreach ( var pattern in patterns ) {
						options.FileRules.Add( CreatePathRule( false, pattern ) );
					}
					break;
				}
				case "exclude-dir":
					options.ExcludeDirectoryPatterns.Add( CreateDirectoryPattern( value ?? string.Empty ) );
					break;
				case "files-without-match":
					options.FileListMode = FileListMode.WithoutMatches;
					break;
				case "files-with-matches":
					options.FileListMode = FileListMode.WithMatches;
					break;
				case "count":
					options.CountOnly = true;
					break;
				case "initial-tab":
					options.InitialTab = true;
					break;
				case "null-filename":
					options.NullFilename = true;
					break;
				case "before-context":
					options.ContextRequested = true;
					if ( !TryParseContext( value, out var before ) ) {
						await ReportErrorAsync( options, context, string.Concat( "invalid context length argument: ", value ) ).ConfigureAwait( false );
						return (null, "invalid context");
					}
					options.BeforeContext = before;
					break;
				case "after-context":
					options.ContextRequested = true;
					if ( !TryParseContext( value, out var after ) ) {
						await ReportErrorAsync( options, context, string.Concat( "invalid context length argument: ", value ) ).ConfigureAwait( false );
						return (null, "invalid context");
					}
					options.AfterContext = after;
					break;
				case "context-short":
				case "context": {
					options.ContextRequested = true;
					var contextValue = value ?? "2";
					if ( !TryParseContext( contextValue, out var contextLength ) ) {
						await ReportErrorAsync( options, context, string.Concat( "invalid context length argument: ", contextValue ) ).ConfigureAwait( false );
						return (null, "invalid context");
					}
					options.BeforeContext = contextLength;
					options.AfterContext = contextLength;
					break;
				}
				case "group-separator":
					options.GroupSeparator = value ?? string.Empty;
					break;
				case "no-group-separator":
					options.GroupSeparator = null;
					break;
				case "color":
					if ( !TryParseColorMode( value, out var colorMode ) ) {
						await ReportErrorAsync( options, context, string.Concat( "invalid argument for --color: ", value ) ).ConfigureAwait( false );
						return (null, "invalid color policy");
					}
					options.ColorMode = colorMode;
					break;
				case "binary-platform":
					// .NET streams are already byte-preserving on every supported platform.
					break;
			}
		}

		var operandIndex = 0;
		if ( options.PatternSources.Count == 0 ) {
			if ( parsed.Operands.Count == 0 ) {
				await ReportErrorAsync( options, context, "missing search pattern" ).ConfigureAwait( false );
				return (null, "missing search pattern");
			}
			options.PatternSources.Add( new PatternSource( PatternSourceKind.Expression, parsed.Operands[0] ) );
			operandIndex = 1;
		}
		for ( ; operandIndex < parsed.Operands.Count; operandIndex++ ) {
			options.Operands.Add( parsed.Operands[operandIndex] );
		}
		if ( options.OnlyMatching ) {
			options.BeforeContext = 0;
			options.AfterContext = 0;
		}
		options.ColorEnabled = options.ColorMode == ColorMode.Always
			|| (
				options.ColorMode == ColorMode.Auto
				&& ReferenceEquals( context.StandardOutput, Console.Out )
				&& !Console.IsOutputRedirected
			);
		return (options, null);
	}

	private static async Task<PatternSet?> CompilePatternsAsync(
		GrepOptions options,
		Stream standardInput,
		CommandContext context
	) {
		var patternTexts = new List<string>();
		foreach ( var source in options.PatternSources ) {
			if ( source.Kind == PatternSourceKind.Expression ) {
				patternTexts.AddRange( SplitExpressionPatterns( source.Value ) );
			} else {
				try {
					patternTexts.AddRange( await ReadPatternLinesAsync( source.Value, standardInput, context.CancellationToken ).ConfigureAwait( false ) );
				} catch ( Exception exception ) when (
					exception is IOException
					or UnauthorizedAccessException
					or System.Security.SecurityException
					or ArgumentException
					or NotSupportedException
				) {
					await ReportErrorAsync( options, context, string.Concat( source.Value, ": ", exception.Message ) ).ConfigureAwait( false );
					return null;
				}
			}
		}

		var characterClassProvider = UnicodeRegularExpressionCharacterClassProvider.CurrentCulture;
		var patterns = new List<IGrepPattern>();
		if ( options.PatternMode == PatternMode.Fixed ) {
			foreach ( var patternText in patternTexts ) {
				patterns.Add( new FixedPattern( patternText, options.IgnoreCase, characterClassProvider ) );
			}
			return new PatternSet(
				patterns,
				options.WordRegexp,
				options.LineRegexp,
				characterClassProvider
			);
		}

		IRegularExpressionProvider provider = options.PatternMode == PatternMode.Extended
			? new GnuExtendedRegularExpressionProvider( characterClassProvider )
			: new GnuBasicRegularExpressionProvider( characterClassProvider );
		var regularExpressionOptions = options.PatternMode == PatternMode.Extended
			? RegularExpressionOptions.GnuExtendedCompatibility with {
				IgnoreCase = options.IgnoreCase,
				NewLineSensitive = !options.NullData
			}
			: new RegularExpressionOptions {
				Syntax = GnuRegularExpressionSyntax.Basic,
				IgnoreCase = options.IgnoreCase,
				NewLineSensitive = !options.NullData,
				AllowInvalidRepetitionOperators = true
			};
		foreach ( var patternText in patternTexts ) {
			var compileResult = await provider.CompileAsync(
				patternText,
				regularExpressionOptions,
				context.CancellationToken
			).ConfigureAwait( false );
			if ( !compileResult.IsSuccess ) {
				await ReportErrorAsync(
					options,
					context,
					compileResult.Diagnostic?.Message ?? "invalid regular expression"
				).ConfigureAwait( false );
				return null;
			}
			patterns.Add( new RegularExpressionPattern( compileResult.Expression! ) );
		}
		return new PatternSet(
			patterns,
			options.WordRegexp,
			options.LineRegexp,
			characterClassProvider
		);
	}

	private static async Task<int> ExecuteAsync(
		GrepOptions options,
		PatternSet patterns,
		Stream standardInput,
		ByteOutputStream output,
		CommandContext context
	) {
		var state = new ExecutionState();
		var operands = options.Operands.Count == 0
			? options.Recursive ? new List<string> { "." } : new List<string> { "-" }
			: options.Operands;
		var automaticFilename = options.Recursive || operands.Count > 1;
		for ( var index = 0; index < operands.Count; index++ ) {
			context.CancellationToken.ThrowIfCancellationRequested();
			var operand = operands[index];
			if ( operand == "-" ) {
				var source = new InputSource( "-", options.StandardInputLabel, true );
				var result = await ProcessSourceAsync(
					source,
					standardInput,
					automaticFilename,
					options,
					patterns,
					output,
					context,
					state.HasRecordOutput
				).ConfigureAwait( false );
				ApplySourceResult( result, state );
				if ( result.StopCommand ) {
					return CommandExitCodes.Success;
				}
				continue;
			}

			if ( Directory.Exists( operand ) ) {
				if ( IsExcludedCommandLineDirectory( options, operand ) ) {
					continue;
				}
				if ( options.DirectoryMode == DirectoryMode.Skip ) {
					continue;
				}
				if ( options.DirectoryMode == DirectoryMode.Recurse ) {
					var stop = await ProcessDirectoryAsync(
						operand,
						index,
						automaticFilename,
						options,
						patterns,
						output,
						context,
						state
					).ConfigureAwait( false );
					if ( stop ) {
						return CommandExitCodes.Success;
					}
					continue;
				}
			}

			if ( !IsCommandLineFileSelected( options, operand ) ) {
				continue;
			}

			if ( await ShouldSkipDeviceAsync( operand, options, context.CancellationToken ).ConfigureAwait( false ) ) {
				continue;
			}
			try {
				await using var stream = new FileStream(
					operand,
					FileMode.Open,
					FileAccess.Read,
					FileShare.ReadWrite | FileShare.Delete,
					StreamOperations.DefaultBufferSize,
					FileOptions.Asynchronous | FileOptions.SequentialScan
				);
				var result = await ProcessSourceAsync(
					new InputSource( operand, operand, false ),
					stream,
					automaticFilename,
					options,
					patterns,
					output,
					context,
					state.HasRecordOutput
				).ConfigureAwait( false );
				ApplySourceResult( result, state );
				if ( result.StopCommand ) {
					return CommandExitCodes.Success;
				}
			} catch ( Exception exception ) when (
				exception is IOException
				or UnauthorizedAccessException
				or System.Security.SecurityException
				or ArgumentException
				or NotSupportedException
			) {
				state.HadError = true;
				await ReportInputErrorAsync( options, context, string.Concat( operand, ": ", exception.Message ) ).ConfigureAwait( false );
			}
		}
		if ( state.HadError ) {
			return CommandExitCodes.UsageError;
		}
		return state.AnyResult ? CommandExitCodes.Success : CommandExitCodes.Failure;
	}

	private static async Task<bool> ProcessDirectoryAsync(
		string operand,
		int operandIndex,
		bool automaticFilename,
		GrepOptions options,
		PatternSet patterns,
		ByteOutputStream output,
		CommandContext context,
		ExecutionState state
	) {
		var engine = new ReadOnlyPathTraversalEngine( SystemReadOnlyFileSystemProvider.Instance );
		var root = new PathTraversalRoot(
			operand,
			operandIndex,
			operandIndex,
			operand,
			operand,
			PathTraversalRootKind.Literal
		);
		var traversalOptions = new PathTraversalOptions {
			SymbolicLinkMode = options.SymbolicLinkMode,
			FileSystemBoundaryMode = FileSystemBoundaryMode.CrossFileSystems,
			ChildOrder = PathTraversalChildOrder.Provider,
			Selector = new GrepTraversalSelector( options ),
			ErrorMode = PathTraversalErrorMode.Continue
		};
		await foreach ( var item in engine.TraverseAsync(
			new[] { root },
			traversalOptions,
			context.CancellationToken
		).ConfigureAwait( false ) ) {
			switch ( item.Kind ) {
				case PathTraversalEventKind.Entry:
					if ( item.Entry is null || item.Entry.Kind == FileSystemEntryKind.Directory ) {
						break;
					}
					if ( options.DeviceMode == DeviceMode.Skip && item.Entry.Kind == FileSystemEntryKind.Other ) {
						break;
					}
					try {
						await using ( var stream = new FileStream(
							item.Entry.AccessPath,
							FileMode.Open,
							FileAccess.Read,
							FileShare.ReadWrite | FileShare.Delete,
							StreamOperations.DefaultBufferSize,
							FileOptions.Asynchronous | FileOptions.SequentialScan
						) ) {
							var result = await ProcessSourceAsync(
								new InputSource( item.Entry.AccessPath, item.Entry.DisplayPath, false ),
								stream,
								automaticFilename,
								options,
								patterns,
								output,
								context,
								state.HasRecordOutput
							).ConfigureAwait( false );
							ApplySourceResult( result, state );
							if ( result.StopCommand ) {
								return true;
							}
						}
					} catch ( Exception exception ) when (
						exception is IOException
						or UnauthorizedAccessException
						or System.Security.SecurityException
						or ArgumentException
						or NotSupportedException
					) {
						state.HadError = true;
						await ReportInputErrorAsync( options, context, string.Concat( item.Entry.DisplayPath, ": ", exception.Message ) ).ConfigureAwait( false );
					}
					break;
				case PathTraversalEventKind.Error:
					state.HadError = true;
					await ReportInputErrorAsync(
						options,
						context,
						string.Concat( item.Error?.Path ?? operand, ": ", item.Error?.Exception?.Message ?? item.Error?.Message ?? "traversal failed" )
					).ConfigureAwait( false );
					break;
				case PathTraversalEventKind.Cycle:
					state.HadError = true;
					await ReportInputErrorAsync(
						options,
						context,
						string.Concat( item.Entry?.DisplayPath ?? operand, ": recursive directory loop" )
					).ConfigureAwait( false );
					break;
			}
		}
		return false;
	}

	private static async ValueTask<(Stream Stream, bool IsBinary)> PrepareInputAsync(
		Stream stream,
		GrepOptions options,
		CancellationToken cancellationToken
	) {
		if ( options.NullData || options.BinaryFileMode == BinaryFileMode.Text ) {
			return (stream, false);
		}
		var startPosition = stream.CanSeek ? stream.Position : 0L;
		var prefix = new byte[BinaryProbeLength];
		var count = 0;
		if ( stream.CanSeek ) {
			while ( count < prefix.Length ) {
				cancellationToken.ThrowIfCancellationRequested();
				var read = await stream.ReadAsync(
					prefix.AsMemory( count, prefix.Length - count ),
					cancellationToken
				).ConfigureAwait( false );
				if ( read == 0 ) {
					break;
				}
				count += read;
			}
		} else {
			count = await stream.ReadAsync( prefix, cancellationToken ).ConfigureAwait( false );
		}
		var isBinary = prefix.AsSpan( 0, count ).Contains( (byte)0 );
		if ( stream.CanSeek ) {
			stream.Seek( startPosition, SeekOrigin.Begin );
			return (stream, isBinary);
		}
		return (
			new PrefixReadStream( prefix.AsMemory( 0, count ), stream ),
			isBinary
		);
	}

	private static async Task<SourceResult> ProcessSourceAsync(
		InputSource source,
		Stream stream,
		bool automaticFilename,
		GrepOptions options,
		PatternSet patterns,
		ByteOutputStream output,
		CommandContext context,
		bool separateBeforeRecordOutput
	) {
		var prefixFieldWidth = GetPrefixFieldWidth( stream, options );
		var preparedInput = await PrepareInputAsync(
			stream,
			options,
			context.CancellationToken
		).ConfigureAwait( false );
		stream = preparedInput.Stream;
		if ( preparedInput.IsBinary && options.BinaryFileMode == BinaryFileMode.WithoutMatch ) {
			return new SourceResult( false, false, false );
		}
		var separator = options.NullData ? (byte)0 : (byte)'\n';
		using var reader = new ByteRecordReader( stream, separator );
		var selectedCount = 0L;
		var wroteRecordOutput = false;
		var lineNumber = 0L;
		var byteOffset = 0L;
		var isBinary = preparedInput.IsBinary;
		var maximumCountReached = options.MaximumCount == 0;
		var sourceStartPosition = source.IsStandardInput && stream.CanSeek
			? stream.Position
			: 0L;
		long? maximumCountResumePosition = source.IsStandardInput
			&& stream.CanSeek
			&& options.MaximumCount.HasValue
			? sourceStartPosition
			: null;
		var previous = new Queue<LineRecord>();
		var afterRemaining = 0;
		var lastWrittenLine = 0L;
		var showFilename = options.FilenameMode switch {
			FilenameMode.Always => true,
			FilenameMode.Never => false,
			_ => automaticFilename
		};
		while ( true ) {
			context.CancellationToken.ThrowIfCancellationRequested();
			var selectionLimitReached = options.MaximumCount.HasValue
				&& selectedCount >= options.MaximumCount.Value;
			var continueForTrailingContext = selectionLimitReached
				&& afterRemaining > 0
				&& options.FileListMode == FileListMode.None
				&& !options.CountOnly
				&& !options.OnlyMatching;
			if ( selectionLimitReached && !continueForTrailingContext ) {
				break;
			}
			var record = await reader.ReadAsync( context.CancellationToken ).ConfigureAwait( false );
			if ( record is null ) {
				break;
			}
			lineNumber++;
			var line = new LineRecord( record.Content.ToArray(), record.IsTerminated, lineNumber, byteOffset );
			byteOffset += record.Content.Length + (record.IsTerminated ? 1 : 0);
			if ( !options.NullData && options.BinaryFileMode != BinaryFileMode.Text && record.Content.Span.Contains( (byte)0 ) ) {
				isBinary = true;
				if ( options.BinaryFileMode == BinaryFileMode.WithoutMatch ) {
					selectedCount = 0;
					break;
				}
			}

			var firstMatch = patterns.IsEmpty ? null : patterns.Find( record.Content, 0, context.CancellationToken );
			var lineMatches = firstMatch is not null;
			var selected = !selectionLimitReached
				&& (options.InvertMatch ? !lineMatches : lineMatches);
			if ( selected ) {
				selectedCount++;
				if ( maximumCountResumePosition.HasValue ) {
					maximumCountResumePosition = sourceStartPosition + byteOffset;
				}
				if ( options.MaximumCount.HasValue && selectedCount >= options.MaximumCount.Value ) {
					maximumCountReached = true;
				}
				if ( options.Quiet ) {
					RestoreStandardInputPosition( stream, maximumCountResumePosition );
					return new SourceResult( true, true, false );
				}
				if ( options.FileListMode == FileListMode.WithMatches ) {
					await WriteFileNameOnlyAsync( source.DisplayName, options, output, context.CancellationToken ).ConfigureAwait( false );
					RestoreStandardInputPosition( stream, maximumCountResumePosition );
					return new SourceResult( true, false, false );
				}
				if ( options.FileListMode == FileListMode.None && !options.CountOnly ) {
					if ( isBinary && options.BinaryFileMode == BinaryFileMode.Binary ) {
						await context.Diagnostics.ErrorAsync(
							string.Concat( source.DisplayName, ": binary file matches" ),
							context.CancellationToken
						).ConfigureAwait( false );
						break;
					}
					if ( options.OnlyMatching ) {
						if ( !options.InvertMatch ) {
							var spans = patterns.FindAll( record.Content, context.CancellationToken );
							if ( spans.Count > 0 && !wroteRecordOutput ) {
								await WriteInterSourceSeparatorAsync(
									separateBeforeRecordOutput,
									options,
									output,
									context.CancellationToken
								).ConfigureAwait( false );
								wroteRecordOutput = true;
							}
							foreach ( var span in spans ) {
								await WriteOnlyMatchAsync(
									source,
									line,
									span,
									showFilename,
									prefixFieldWidth,
									options,
									output,
									context.CancellationToken
								).ConfigureAwait( false );
							}
						}
					} else {
						if ( !wroteRecordOutput ) {
							await WriteInterSourceSeparatorAsync(
								separateBeforeRecordOutput,
								options,
								output,
								context.CancellationToken
							).ConfigureAwait( false );
							wroteRecordOutput = true;
						}
						foreach ( var prior in previous ) {
							if ( prior.LineNumber > lastWrittenLine ) {
								lastWrittenLine = await WriteContextAwareRecordAsync(
									source,
									prior,
									false,
									showFilename,
									prefixFieldWidth,
									patterns,
									options,
									output,
									lastWrittenLine,
									context.CancellationToken
								).ConfigureAwait( false );
							}
						}
						lastWrittenLine = await WriteContextAwareRecordAsync(
							source,
							line,
							true,
							showFilename,
							prefixFieldWidth,
							patterns,
							options,
							output,
							lastWrittenLine,
							context.CancellationToken
						).ConfigureAwait( false );
						afterRemaining = options.AfterContext;
					}
				}
			} else if (
				afterRemaining > 0
				&& options.FileListMode == FileListMode.None
				&& !options.CountOnly
				&& !options.OnlyMatching
			) {
				lastWrittenLine = await WriteContextAwareRecordAsync(
					source,
					line,
					false,
					showFilename,
					prefixFieldWidth,
					patterns,
					options,
					output,
					lastWrittenLine,
					context.CancellationToken
				).ConfigureAwait( false );
				afterRemaining--;
			}

			if ( options.BeforeContext > 0 ) {
				previous.Enqueue( line );
				while ( previous.Count > options.BeforeContext ) {
					previous.Dequeue();
				}
			}
		}

		if ( maximumCountReached ) {
			RestoreStandardInputPosition( stream, maximumCountResumePosition );
		}

		if ( options.FileListMode == FileListMode.WithoutMatches ) {
			if ( !options.Quiet && selectedCount == 0 ) {
				await WriteFileNameOnlyAsync( source.DisplayName, options, output, context.CancellationToken ).ConfigureAwait( false );
			}
			return new SourceResult( selectedCount > 0, false, wroteRecordOutput );
		}
		if ( !options.Quiet && options.CountOnly && options.FileListMode == FileListMode.None ) {
			await WriteCountAsync(
				source.DisplayName,
				selectedCount,
				showFilename,
				options,
				output,
				context.CancellationToken
			).ConfigureAwait( false );
		}
		return new SourceResult( selectedCount > 0, false, wroteRecordOutput );
	}

	private static void RestoreStandardInputPosition( Stream stream, long? position ) {
		if ( position.HasValue ) {
			stream.Seek( position.Value, SeekOrigin.Begin );
		}
	}

	private static async Task WriteInterSourceSeparatorAsync(
		bool separateBeforeRecordOutput,
		GrepOptions options,
		ByteOutputStream output,
		CancellationToken cancellationToken
	) {
		if ( !separateBeforeRecordOutput || !options.ContextRequested || options.GroupSeparator is null ) {
			return;
		}
		await output.WriteTextAsync( options.GroupSeparator, cancellationToken ).ConfigureAwait( false );
		await output.WriteAsync(
			new[] { options.NullData ? (byte)0 : (byte)'\n' },
			cancellationToken
		).ConfigureAwait( false );
	}

	private static async Task<long> WriteContextAwareRecordAsync(
		InputSource source,
		LineRecord record,
		bool selected,
		bool showFilename,
		int prefixFieldWidth,
		PatternSet patterns,
		GrepOptions options,
		ByteOutputStream output,
		long lastWrittenLine,
		CancellationToken cancellationToken
	) {
		if (
			options.ContextRequested
			&& lastWrittenLine > 0
			&& record.LineNumber > lastWrittenLine + 1
			&& options.GroupSeparator is not null
		) {
			await output.WriteTextAsync( options.GroupSeparator, cancellationToken ).ConfigureAwait( false );
			await output.WriteAsync( new[] { options.NullData ? (byte)0 : (byte)'\n' }, cancellationToken ).ConfigureAwait( false );
		}
		await WritePrefixAsync(
			source.DisplayName,
			record.LineNumber,
			record.ByteOffset,
			selected,
			showFilename,
			prefixFieldWidth,
			options,
			output,
			cancellationToken
		).ConfigureAwait( false );
		if ( selected && options.ColorEnabled && !options.InvertMatch ) {
			await WriteColoredRecordContentAsync( record.Content, patterns, output, cancellationToken ).ConfigureAwait( false );
		} else {
			await output.WriteAsync( record.Content, cancellationToken ).ConfigureAwait( false );
		}
		await output.WriteAsync(
			new[] { options.NullData ? (byte)0 : (byte)'\n' },
			cancellationToken
		).ConfigureAwait( false );
		if ( options.LineBuffered ) {
			await output.FlushAsync( cancellationToken ).ConfigureAwait( false );
		}
		return record.LineNumber;
	}

	private static async Task WriteOnlyMatchAsync(
		InputSource source,
		LineRecord record,
		MatchSpan span,
		bool showFilename,
		int prefixFieldWidth,
		GrepOptions options,
		ByteOutputStream output,
		CancellationToken cancellationToken
	) {
		await WritePrefixAsync(
			source.DisplayName,
			record.LineNumber,
			record.ByteOffset + span.Index,
			true,
			showFilename,
			prefixFieldWidth,
			options,
			output,
			cancellationToken
		).ConfigureAwait( false );
		if ( options.ColorEnabled ) {
			await output.WriteAsync( MatchColorStart, cancellationToken ).ConfigureAwait( false );
		}
		await output.WriteAsync( record.Content.AsMemory( span.Index, span.Length ), cancellationToken ).ConfigureAwait( false );
		if ( options.ColorEnabled ) {
			await output.WriteAsync( MatchColorEnd, cancellationToken ).ConfigureAwait( false );
		}
		await output.WriteAsync( new[] { options.NullData ? (byte)0 : (byte)'\n' }, cancellationToken ).ConfigureAwait( false );
		if ( options.LineBuffered ) {
			await output.FlushAsync( cancellationToken ).ConfigureAwait( false );
		}
	}

	private static async Task WriteColoredRecordContentAsync(
		byte[] content,
		PatternSet patterns,
		ByteOutputStream output,
		CancellationToken cancellationToken
	) {
		var spans = patterns.FindAll( content, cancellationToken );
		var position = 0;
		foreach ( var span in spans ) {
			if ( span.Index > position ) {
				await output.WriteAsync( content.AsMemory( position, span.Index - position ), cancellationToken ).ConfigureAwait( false );
			}
			await output.WriteAsync( MatchColorStart, cancellationToken ).ConfigureAwait( false );
			await output.WriteAsync( content.AsMemory( span.Index, span.Length ), cancellationToken ).ConfigureAwait( false );
			await output.WriteAsync( MatchColorEnd, cancellationToken ).ConfigureAwait( false );
			position = span.Index + span.Length;
		}
		if ( position < content.Length ) {
			await output.WriteAsync( content.AsMemory( position ), cancellationToken ).ConfigureAwait( false );
		}
	}

	private static async Task WritePrefixAsync(
		string displayName,
		long lineNumber,
		long byteOffset,
		bool selected,
		bool showFilename,
		int prefixFieldWidth,
		GrepOptions options,
		ByteOutputStream output,
		CancellationToken cancellationToken
	) {
		var separator = selected ? (byte)':' : (byte)'-';
		var hasPrefix = false;
		if ( showFilename ) {
			await output.WriteTextAsync( displayName, cancellationToken ).ConfigureAwait( false );
			await output.WriteAsync( new[] { options.NullFilename ? (byte)0 : separator }, cancellationToken ).ConfigureAwait( false );
			hasPrefix = true;
		}
		if ( options.LineNumber ) {
			await WriteAlignedNumberAsync(
				lineNumber,
				prefixFieldWidth,
				output,
				cancellationToken
			).ConfigureAwait( false );
			await output.WriteAsync( new[] { separator }, cancellationToken ).ConfigureAwait( false );
			hasPrefix = true;
		}
		if ( options.ByteOffset ) {
			await WriteAlignedNumberAsync(
				byteOffset,
				prefixFieldWidth,
				output,
				cancellationToken
			).ConfigureAwait( false );
			await output.WriteAsync( new[] { separator }, cancellationToken ).ConfigureAwait( false );
			hasPrefix = true;
		}
		if ( options.InitialTab && hasPrefix ) {
			await output.WriteAsync( new[] { (byte)'\t' }, cancellationToken ).ConfigureAwait( false );
		}
	}

	private static int GetPrefixFieldWidth( Stream stream, GrepOptions options ) {
		if ( !options.InitialTab || (!options.LineNumber && !options.ByteOffset) ) {
			return 0;
		}
		if ( stream.CanSeek ) {
			try {
				return Math.Max( 1L, stream.Length ).ToString( CultureInfo.InvariantCulture ).Length;
			} catch ( NotSupportedException ) {
				// Fall through to the widest possible signed stream offset.
			}
		}
		return long.MaxValue.ToString( CultureInfo.InvariantCulture ).Length;
	}

	private static async Task WriteAlignedNumberAsync(
		long value,
		int width,
		ByteOutputStream output,
		CancellationToken cancellationToken
	) {
		var text = value.ToString( CultureInfo.InvariantCulture );
		if ( width > text.Length ) {
			await output.WriteTextAsync( new string( ' ', width - text.Length ), cancellationToken ).ConfigureAwait( false );
		}
		await output.WriteTextAsync( text, cancellationToken ).ConfigureAwait( false );
	}

	private static async Task WriteFileNameOnlyAsync(
		string displayName,
		GrepOptions options,
		ByteOutputStream output,
		CancellationToken cancellationToken
	) {
		await output.WriteTextAsync( displayName, cancellationToken ).ConfigureAwait( false );
		await output.WriteAsync( new[] { options.NullFilename ? (byte)0 : (byte)'\n' }, cancellationToken ).ConfigureAwait( false );
		if ( options.LineBuffered ) {
			await output.FlushAsync( cancellationToken ).ConfigureAwait( false );
		}
	}

	private static async Task WriteCountAsync(
		string displayName,
		long count,
		bool showFilename,
		GrepOptions options,
		ByteOutputStream output,
		CancellationToken cancellationToken
	) {
		if ( showFilename ) {
			await output.WriteTextAsync( displayName, cancellationToken ).ConfigureAwait( false );
			await output.WriteAsync( new[] { options.NullFilename ? (byte)0 : (byte)':' }, cancellationToken ).ConfigureAwait( false );
		}
		await output.WriteTextAsync( count.ToString( CultureInfo.InvariantCulture ), cancellationToken ).ConfigureAwait( false );
		await output.WriteAsync( new[] { (byte)'\n' }, cancellationToken ).ConfigureAwait( false );
		if ( options.LineBuffered ) {
			await output.FlushAsync( cancellationToken ).ConfigureAwait( false );
		}
	}

	private static void ApplySourceResult( SourceResult result, ExecutionState state ) {
		state.AnyResult |= result.HasSelectedRecord;
		state.HasRecordOutput |= result.WroteRecordOutput;
	}

	private static PathRule CreatePathRule( bool include, string pattern ) => new(
		include,
		PathnamePattern.Parse( pattern )
	);

	private static PathnamePattern CreateDirectoryPattern( string pattern ) {
		var normalized = pattern.TrimEnd( '/', '\\' );
		return PathnamePattern.Parse( normalized );
	}

	private static bool IsCommandLineFileSelected( GrepOptions options, string operand ) {
		var selected = options.FileRules.Count == 0 || !options.FileRules[0].Include;
		foreach ( var rule in options.FileRules ) {
			if ( PatternMatchesCommandLineSuffix( rule.Pattern, operand ) ) {
				selected = rule.Include;
			}
		}
		return selected;
	}

	private static bool IsExcludedCommandLineDirectory( GrepOptions options, string operand ) {
		foreach ( var pattern in options.ExcludeDirectoryPatterns ) {
			if ( PatternMatchesCommandLineSuffix( pattern, operand ) ) {
				return true;
			}
		}
		return false;
	}

	private static bool PatternMatchesCommandLineSuffix( PathnamePattern pattern, string operand ) {
		var normalized = operand.TrimEnd( '/', '\\' );
		if ( pattern.IsMatch( normalized ) ) {
			return true;
		}
		for ( var index = 0; index < normalized.Length - 1; index++ ) {
			if ( normalized[index] is '/' or '\\' && pattern.IsMatch( normalized[(index + 1)..] ) ) {
				return true;
			}
		}
		return false;
	}

	private static async ValueTask<bool> ShouldSkipDeviceAsync(
		string path,
		GrepOptions options,
		CancellationToken cancellationToken
	) {
		if ( options.DeviceMode != DeviceMode.Skip ) {
			return false;
		}
		try {
			var observation = await SystemReadOnlyFileSystemProvider.Instance.ObserveAsync(
				path,
				followSymbolicLink: true,
				cancellationToken
			).ConfigureAwait( false );
			return observation.Kind == FileSystemEntryKind.Other;
		} catch ( Exception exception ) when (
			exception is IOException
				or UnauthorizedAccessException
				or System.Security.SecurityException
				or ArgumentException
				or NotSupportedException
		) {
			return false;
		}
	}

	private static bool TrySetPatternMode(
		GrepOptions options,
		ref PatternMode? explicitPatternMode,
		PatternMode mode
	) {
		if ( explicitPatternMode.HasValue && explicitPatternMode.Value != mode ) {
			return false;
		}
		explicitPatternMode = mode;
		options.PatternMode = mode;
		return true;
	}

	private static bool TryParseMaximumCount( string? value, out long result ) => long.TryParse(
		value,
		NumberStyles.AllowLeadingSign,
		CultureInfo.InvariantCulture,
		out result
	) && result >= -1;

	private static bool TryParseContext( string? value, out int result ) => int.TryParse(
		value,
		NumberStyles.None,
		CultureInfo.InvariantCulture,
		out result
	) && result >= 0;

	private static bool TryParseBinaryMode( string? value, out BinaryFileMode mode ) {
		mode = value switch {
			"binary" => BinaryFileMode.Binary,
			"text" => BinaryFileMode.Text,
			"without-match" => BinaryFileMode.WithoutMatch,
			_ => default
		};
		return value is "binary" or "text" or "without-match";
	}

	private static bool TryParseDirectoryMode( string? value, out DirectoryMode mode ) {
		mode = value switch {
			"read" => DirectoryMode.Read,
			"recurse" => DirectoryMode.Recurse,
			"skip" => DirectoryMode.Skip,
			_ => default
		};
		return value is "read" or "recurse" or "skip";
	}

	private static bool TryParseDeviceMode( string? value, out DeviceMode mode ) {
		mode = value switch {
			"read" => DeviceMode.Read,
			"skip" => DeviceMode.Skip,
			_ => default
		};
		return value is "read" or "skip";
	}

	private static bool TryParseColorMode( string? value, out ColorMode mode ) {
		var effective = value ?? "auto";
		mode = effective switch {
			"never" or "none" or "no" => ColorMode.Never,
			"always" or "yes" or "force" => ColorMode.Always,
			"auto" or "tty" or "if-tty" => ColorMode.Auto,
			_ => default
		};
		return effective is "never" or "none" or "no" or "always" or "yes" or "force" or "auto" or "tty" or "if-tty";
	}

	private static IEnumerable<string> SplitExpressionPatterns( string value ) {
		var start = 0;
		for ( var index = 0; index < value.Length; index++ ) {
			if ( value[index] != '\n' ) {
				continue;
			}
			yield return value[start..index];
			start = index + 1;
		}
		yield return value[start..];
	}

	private static async Task<IReadOnlyList<string>> ReadPatternLinesAsync(
		string path,
		Stream standardInput,
		CancellationToken cancellationToken
	) {
		Stream stream;
		var dispose = false;
		if ( path == "-" ) {
			stream = standardInput;
		} else {
			stream = new FileStream(
				path,
				FileMode.Open,
				FileAccess.Read,
				FileShare.ReadWrite | FileShare.Delete,
				StreamOperations.DefaultBufferSize,
				FileOptions.Asynchronous | FileOptions.SequentialScan
			);
			dispose = true;
		}
		try {
			using var memory = new MemoryStream();
			await stream.CopyToAsync( memory, cancellationToken ).ConfigureAwait( false );
			var text = Encoding.UTF8.GetString( memory.ToArray() );
			if ( text.Length == 0 ) {
				return Array.Empty<string>();
			}
			var parts = text.Split( '\n' ).ToList();
			if ( text.EndsWith( '\n' ) && parts.Count > 0 ) {
				parts.RemoveAt( parts.Count - 1 );
			}
			return parts;
		} finally {
			if ( dispose ) {
				await stream.DisposeAsync().ConfigureAwait( false );
			}
		}
	}

	private static ValueTask ReportErrorAsync(
		GrepOptions options,
		CommandContext context,
		string message
	) {
		_ = options;
		return context.Diagnostics.ErrorAsync( message, context.CancellationToken );
	}

	private static ValueTask ReportInputErrorAsync(
		GrepOptions options,
		CommandContext context,
		string message
	) => options.NoMessages
		? ValueTask.CompletedTask
		: context.Diagnostics.ErrorAsync( message, context.CancellationToken );

	private static async Task WriteHelpAsync( CommandContext context ) {
		const string help = """
Usage: grep [OPTION]... PATTERNS [FILE]...
Search for PATTERNS in each FILE.  With no FILE, or when FILE is -, read standard input.

Pattern selection and interpretation:
  -E, --extended-regexp       PATTERNS are extended regular expressions
  -F, --fixed-strings         PATTERNS are strings
  -G, --basic-regexp          PATTERNS are basic regular expressions
  -P, --perl-regexp           diagnose that managed PCRE support is unavailable
  -e, --regexp=PATTERNS       use PATTERNS for matching
  -f, --file=FILE             take PATTERNS from FILE
  -i, -y, --ignore-case       ignore case distinctions
      --no-ignore-case        do not ignore case distinctions
  -w, --word-regexp           match only whole words
  -x, --line-regexp           match only whole records
  -z, --null-data             a record ends in NUL rather than newline

Output control:
  -v, --invert-match          select nonmatching records
  -m, --max-count=NUM         stop after NUM selected records
  -b, --byte-offset           print the byte offset with output records
  -n, --line-number           print the record number with output records
  -H, --with-filename         print the file name for each match
  -h, --no-filename           suppress file names
      --label=LABEL           use LABEL as the standard-input name
  -o, --only-matching        show only nonempty matching parts
  -q, --quiet, --silent       suppress normal output and stop at first selection
  -c, --count                 print only a count of selected records per FILE
  -l, --files-with-matches    print only names of FILEs with selected records
  -L, --files-without-match   print only names of FILEs without selected records
  -Z, --null                  print NUL after file names
      --color[=WHEN]          surround matches with terminal color when WHEN is always

File and directory selection:
  -a, --text                  process binary data as text
  -I                          assume binary files have no match
      --binary-files=TYPE     TYPE is binary, text, or without-match
  -d, --directories=ACTION    ACTION is read, recurse, or skip
  -D, --devices=ACTION        ACTION is read or skip
  -r, --recursive             recurse, following command-line directory links
  -R, --dereference-recursive recurse, following all directory links
      --include=GLOB          search only files matching GLOB
      --exclude=GLOB          skip files matching GLOB
      --exclude-from=FILE     read exclude GLOBs from FILE
      --exclude-dir=GLOB      skip directories matching GLOB

Context control:
  -B, --before-context=NUM    print NUM records of leading context
  -A, --after-context=NUM     print NUM records of trailing context
  -C, --context=NUM           print NUM records of output context
      --group-separator=SEP   use SEP between groups
      --no-group-separator    do not separate groups

  -s, --no-messages           suppress file-error messages
      --line-buffered         flush output after each output record
  -T, --initial-tab           align record content after prefixes
  -U, --binary                retain binary platform input mode
      --help                  display this help and exit
  -V, --version               output version information and exit

Exit status is 0 if a result is selected, 1 if none is selected, and 2 on error.
""";
		await context.StandardOutput.WriteLineAsync(
			help.ReplaceLineEndings( Environment.NewLine ).AsMemory(),
			context.CancellationToken
		).ConfigureAwait( false );
	}
}
