namespace Icod.Grep;

using Icod.CommandFramework.Diagnostics;

/// <summary>Provides the <c>grep [OPTION]... PATTERNS [FILE]...</c> process entry point.</summary>
public static class Program {
	/// <summary>Runs the GNU-compatible pattern-search command.</summary>
	/// <param name="args">The command-line arguments.</param>
	/// <returns>A task whose result is the process exit status.</returns>
	public static async Task<int> Main( string[] args ) {
		ArgumentNullException.ThrowIfNull( args );
		if ( args.Any( static value => "-V" == value || "--version" == value ) ) {
			var version = typeof( Program ).Assembly.GetName().Version?.ToString( 3 ) ?? "1.5.0";
			await Console.Out.WriteLineAsync(
				string.Concat( "grep (Icod.Grep) ", version )
			).ConfigureAwait( false );
			return CommandExitCodes.Success;
		}

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
