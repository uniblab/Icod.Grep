namespace Icod.Grep;

using Icod.CommandFramework.Diagnostics;

/// <summary>Provides the <c>grep [OPTION]... PATTERNS [FILE]...</c> process entry point.</summary>
public static class Program {
	/// <summary>Runs the GNU-compatible pattern-search command.</summary>
	/// <param name="args">The command-line arguments.</param>
	/// <returns>A task whose result is the process exit status.</returns>
	public static Task<int> Main( string[] args ) => Command.RunAsync(
		args,
		CommandContext.CreateConsole( "grep" )
	);
}
