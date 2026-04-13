using System.Runtime.CompilerServices;
using UnityEngine.Scripting;

[assembly: InternalsVisibleTo("Convai.Tests.EditMode")]

// Force IL2CPP to include this assembly in native player builds even though
// the runtime resolves its platform services via RuntimeInitializeOnLoadMethod
// and service locator discovery instead of hard compile-time references.
[assembly: AlwaysLinkAssembly]
[assembly: Preserve]