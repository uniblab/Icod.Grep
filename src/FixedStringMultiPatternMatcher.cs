namespace Icod.Grep;

/// <summary>Provides immutable multi-pattern byte matching for case-sensitive fixed strings.</summary>
internal sealed class FixedStringMultiPatternMatcher {
	private const int CancellationCheckMask = 0x0FFF;
	private readonly Node[] nodes;

	/// <summary>Initializes an immutable multi-pattern matcher.</summary>
	/// <param name="patterns">The non-empty byte patterns to compile.</param>
	public FixedStringMultiPatternMatcher( IReadOnlyList<byte[]> patterns ) {
		ArgumentNullException.ThrowIfNull( patterns );
		if ( 0 == patterns.Count ) {
			throw new ArgumentException(
				"At least one fixed-string pattern is required.",
				nameof( patterns )
			);
		}

		var builders = new List<BuilderNode> {
			new()
		};
		for ( var patternIndex = 0; patterns.Count > patternIndex; patternIndex++ ) {
			var pattern = patterns[patternIndex];
			ArgumentNullException.ThrowIfNull( pattern );
			if ( 0 == pattern.Length ) {
				throw new ArgumentException(
					"Fixed-string multi-pattern acceleration does not accept empty patterns.",
					nameof( patterns )
				);
			}

			var nodeIndex = 0;
			foreach ( var value in pattern ) {
				if ( !builders[nodeIndex].Transitions.TryGetValue( value, out var nextIndex ) ) {
					nextIndex = builders.Count;
					builders[nodeIndex].Transitions.Add( value, nextIndex );
					builders.Add( new BuilderNode() );
				}
				nodeIndex = nextIndex;
			}
			builders[nodeIndex].Outputs.Add( pattern.Length );
		}

		BuildFailureLinks( builders );
		this.nodes = new Node[builders.Count];
		for ( var index = 0; builders.Count > index; index++ ) {
			var transitions = builders[index].Transitions
				.Select(
					static pair => new Transition( pair.Key, pair.Value )
				)
				.OrderBy(
					static transition => transition.Value
				)
				.ToArray();
			var outputs = builders[index].Outputs
				.Distinct()
				.OrderByDescending(
					static length => length
				)
				.ToArray();
			this.nodes[index] = new Node(
				transitions,
				builders[index].Failure,
				outputs
			);
		}
	}

	/// <summary>Finds the leftmost match and uses the longest match when multiple patterns begin at that byte.</summary>
	/// <param name="input">The authoritative input bytes.</param>
	/// <param name="startOffset">The first byte offset at which a pattern may begin.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The selected byte span, or <see langword="null"/> when no pattern matches.</returns>
	public FixedStringMultiPatternMatch? Find(
		ReadOnlySpan<byte> input,
		int startOffset,
		CancellationToken cancellationToken
	) {
		if ( 0 > startOffset || input.Length < startOffset ) {
			return null;
		}

		cancellationToken.ThrowIfCancellationRequested();
		var state = 0;
		var bestIndex = int.MaxValue;
		var bestLength = 0;
		for ( var index = startOffset; input.Length > index; index++ ) {
			if ( 0 == ((index - startOffset) & CancellationCheckMask) ) {
				cancellationToken.ThrowIfCancellationRequested();
			}

			var value = input[index];
			while (
				0 != state
				&& !TryGetTransition( this.nodes[state].Transitions, value, out _ )
			) {
				state = this.nodes[state].Failure;
			}
			if ( TryGetTransition( this.nodes[state].Transitions, value, out var nextState ) ) {
				state = nextState;
			} else {
				state = 0;
			}

			foreach ( var length in this.nodes[state].Outputs ) {
				var candidateIndex = index + 1 - length;
				if ( candidateIndex < startOffset ) {
					continue;
				}
				if (
					candidateIndex < bestIndex
					|| (
						candidateIndex == bestIndex
						&& length > bestLength
					)
				) {
					bestIndex = candidateIndex;
					bestLength = length;
				}
			}
		}
		cancellationToken.ThrowIfCancellationRequested();
		return int.MaxValue == bestIndex
			? null
			: new FixedStringMultiPatternMatch( bestIndex, bestLength );
	}

	private static void BuildFailureLinks( List<BuilderNode> nodes ) {
		var queue = new Queue<int>();
		foreach ( var childIndex in nodes[0].Transitions.Values ) {
			nodes[childIndex].Failure = 0;
			queue.Enqueue( childIndex );
		}

		while ( 0 < queue.Count ) {
			var nodeIndex = queue.Dequeue();
			foreach ( var transition in nodes[nodeIndex].Transitions ) {
				var value = transition.Key;
				var childIndex = transition.Value;
				var failure = nodes[nodeIndex].Failure;
				while (
					0 != failure
					&& !nodes[failure].Transitions.ContainsKey( value )
				) {
					failure = nodes[failure].Failure;
				}
				if ( nodes[failure].Transitions.TryGetValue( value, out var fallback ) ) {
					nodes[childIndex].Failure = fallback;
				} else {
					nodes[childIndex].Failure = 0;
				}
				if ( 0 < nodes[nodes[childIndex].Failure].Outputs.Count ) {
					nodes[childIndex].Outputs.AddRange(
						nodes[nodes[childIndex].Failure].Outputs
					);
				}
				queue.Enqueue( childIndex );
			}
		}
	}

	private static bool TryGetTransition(
		Transition[] transitions,
		byte value,
		out int target
	) {
		for ( var index = 0; transitions.Length > index; index++ ) {
			if ( transitions[index].Value == value ) {
				target = transitions[index].Target;
				return true;
			}
			if ( transitions[index].Value > value ) {
				break;
			}
		}
		target = 0;
		return false;
	}

	private sealed class BuilderNode {
		public Dictionary<byte, int> Transitions { get; } = new();
		public List<int> Outputs { get; } = new();
		public int Failure { get; set; }
	}

	private readonly record struct Transition( byte Value, int Target );
	private readonly record struct Node(
		Transition[] Transitions,
		int Failure,
		int[] Outputs
	);
}

/// <summary>Represents one fixed-string multi-pattern byte match.</summary>
internal readonly record struct FixedStringMultiPatternMatch( int Index, int Length );
