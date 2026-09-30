using System.Runtime.CompilerServices;

// Assembly-level attributes apply to the whole Match3 assembly rather than to one class.
// InternalsVisibleTo lets the named assembly use this one's `internal` members, which is how
// the Play Mode tests reach Board's test hooks (ConfigureForTests, LoadLayoutForTests)
// without those becoming public for the rest of the game.
[assembly: InternalsVisibleTo("Match3.Tests.PlayMode")]
