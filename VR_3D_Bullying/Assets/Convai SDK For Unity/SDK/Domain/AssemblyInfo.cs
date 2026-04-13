using System.Runtime.CompilerServices;

// Allow Application layer to access internal members for cross-layer orchestration
[assembly: InternalsVisibleTo("Convai.Application")]

// Allow test assemblies to access internal members for unit testing
[assembly: InternalsVisibleTo("Convai.Tests.EditMode")]
[assembly: InternalsVisibleTo("Convai.Tests.PlayMode")]
