namespace Icod.Grep;

/// <summary>Represents GNU grep color capabilities resolved from GREP_COLORS and GREP_COLOR.</summary>
internal sealed record GrepColorProfile(
	string SelectedLine,
	string ContextLine,
	string SelectedMatch,
	string ContextMatch,
	string FileName,
	string LineNumber,
	string ByteOffset,
	string Separator,
	bool ReverseSelectedContext,
	bool NoErase
) {
	public static GrepColorProfile Default { get; } = new(
		string.Empty,
		string.Empty,
		"01;31",
		"01;31",
		"35",
		"32",
		"32",
		"36",
		false,
		false
	);

	public static GrepColorProfile Resolve(
		string? grepColors,
		string? grepColor,
		out string? warning
	) {
		warning = null;
		var profile = Default;
		if ( !string.IsNullOrEmpty( grepColors ) ) {
			foreach ( var token in grepColors.Split( ':' ) ) {
				if ( token == "rv" ) {
					profile = profile with { ReverseSelectedContext = true };
					continue;
				}
				if ( token == "ne" ) {
					profile = profile with { NoErase = true };
					continue;
				}
				var equals = token.IndexOf( '=' );
				if ( equals < 0 ) {
					continue;
				}
				var key = token[..equals];
				var value = token[(equals + 1)..];
				profile = key switch {
					"sl" => profile with { SelectedLine = value },
					"cx" => profile with { ContextLine = value },
					"ms" => profile with { SelectedMatch = value },
					"mc" => profile with { ContextMatch = value },
					"mt" => profile with { SelectedMatch = value, ContextMatch = value },
					"fn" => profile with { FileName = value },
					"ln" => profile with { LineNumber = value },
					"bn" => profile with { ByteOffset = value },
					"se" => profile with { Separator = value },
					_ => profile
				};
			}
			return profile;
		}
		if ( !string.IsNullOrEmpty( grepColor ) ) {
			profile = profile with { SelectedMatch = grepColor, ContextMatch = grepColor };
			warning = string.Concat(
				"GREP_COLOR='", grepColor,
				"' is deprecated; use GREP_COLORS='mt=", grepColor, "'"
			);
		}
		return profile;
	}

	public static bool ShouldEnableAutoColor( bool stdoutIsTerminal, string? term ) =>
		stdoutIsTerminal && !string.Equals( term, "dumb", StringComparison.Ordinal );

	public string GetLineStyle( bool selected, bool invertMatch ) {
		var useSelectedStyle = invertMatch && ReverseSelectedContext ? !selected : selected;
		return useSelectedStyle ? SelectedLine : ContextLine;
	}

	public string GetMatchStyle( bool invertMatch ) => invertMatch ? ContextMatch : SelectedMatch;
}
