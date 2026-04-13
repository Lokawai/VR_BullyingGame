using System.Runtime.CompilerServices;
#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine.Scripting;
#endif

[assembly: InternalsVisibleTo("Convai.Tests.EditMode")]

#if UNITY_WEBGL && !UNITY_EDITOR
// Force IL2CPP to include this assembly in WebGL builds even though no other
// assembly directly references it at compile time. The Convai.Runtime assembly
// intentionally avoids a hard reference to keep the platform split clean;
// WebGLTransportBootstrap registers factories via [RuntimeInitializeOnLoadMethod]
// and ConvaiRoomManager discovers them through ConvaiServiceLocator.
//
// Without AlwaysLinkAssembly, IL2CPP's managed code linker sees zero inbound
// references and strips the entire assembly from the player build — preventing
// the bootstrap from ever running.
[assembly: AlwaysLinkAssembly]
[assembly: Preserve]
#endif
