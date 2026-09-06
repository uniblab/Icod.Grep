namespace Icod.Grep.Benchmarks;

/// <summary>Owns one temporary fixture tree for a T6.8 stress scenario.</summary>
internal sealed class StressFixtureDirectory : IDisposable {
	/// <summary>Gets the temporary fixture root.</summary>
	internal string RootPath {
		get;
	}

	internal StressFixtureDirectory( string scenarioName ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( scenarioName );
		var safeName = new string(
			scenarioName.Select(
				static value => SanitizeCharacter( value )
			).ToArray()
		);
		this.RootPath = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			"Icod.Grep-Stress",
			string.Concat(
				safeName,
				"-",
				Guid.NewGuid().ToString( "N" )
			)
		);
		Directory.CreateDirectory( this.RootPath );
	}

	internal string GetPath( params string[] components ) {
		ArgumentNullException.ThrowIfNull( components );
		var path = this.RootPath;
		foreach ( var component in components ) {
			ArgumentException.ThrowIfNullOrWhiteSpace( component );
			path = System.IO.Path.Combine( path, component );
		}
		return path;
	}

	internal string CreateDirectory( params string[] components ) {
		ArgumentNullException.ThrowIfNull( components );
		var path = this.GetPath( components );
		Directory.CreateDirectory( path );
		return path;
	}

	internal string WriteBytes(
		byte[] content,
		params string[] components
	) {
		ArgumentNullException.ThrowIfNull( content );
		ArgumentNullException.ThrowIfNull( components );
		var path = this.GetPath( components );
		var parent = System.IO.Path.GetDirectoryName( path );
		if ( !string.IsNullOrEmpty( parent ) ) {
			Directory.CreateDirectory( parent );
		}
		File.WriteAllBytes( path, content );
		return path;
	}

	public void Dispose() {
		if ( Directory.Exists( this.RootPath ) ) {
			Directory.Delete(
				this.RootPath,
				recursive: true
			);
		}
	}

	private static char SanitizeCharacter( char value ) {
		if ( char.IsLetterOrDigit( value ) || value is '-' or '_' ) {
			return value;
		}
		return '-';
	}
}