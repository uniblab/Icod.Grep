namespace Icod.Grep;

using Icod.CommandFramework.Diagnostics;

/// <summary>Provides the <c>grep [OPTION]... PATTERNS [FILE]...</c> process entry point.</summary>
public static class Program {
	/// <summary>Runs the GNU-compatible pattern-search command.</summary>
	/// <param name="args">The command-line arguments.</param>
	/// <returns>A task whose result is the process exit status.</returns>
	public static async Task<int> Main( string[] args ) {
		ArgumentNullException.ThrowIfNull( args );
		using var platformMode = PlatformIoContext.EnterProcessMode( args );
		var standardInput = Console.OpenStandardInput();
		var standardOutput = Console.OpenStandardOutput();
		var standardError = Console.OpenStandardError();
		var context = new CommandContext(
			"grep",
			Console.In,
			Console.Out,
			Console.Error,
			PlatformIoContext.WrapStandardInput( standardInput ),
			PlatformIoContext.WrapStandardOutput( standardOutput ),
			standardError
		);
		return await Command.RunAsync( args, context ).ConfigureAwait( false );
	}
}
