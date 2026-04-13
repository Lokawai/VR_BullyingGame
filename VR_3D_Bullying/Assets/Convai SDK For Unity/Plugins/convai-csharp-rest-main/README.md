# Convai REST API Client

Modern, type-safe C# client for Convai's REST APIs with Task-based async/await support. Built for Unity 2022.3+ and Unity 6 with WebGL compatibility.

## How the Unity SDK uses this

The Unity SDK uses this REST client internally for things like:

- API key validation in editor UI
- fetching character/narrative metadata for inspector tools
- requesting room connection details (room name + token) before joining LiveKit

If you’re integrating Convai into a Unity scene, you typically won’t call this directly — start with `Documentation~/SETUP.md`.

## Quick Start

```csharp
using Convai.RestAPI;

// Create client
var options = new ConvaiRestClientOptions("your-api-key");
using var client = new ConvaiRestClient(options);

// Get character details
var character = await client.Characters.GetDetailsAsync("character-id");

// Create a speaker for LTM
var speakerId = await client.Ltm.CreateSpeakerAsync("Player Name");

// Connect to a room
var roomRequest = new RoomConnectionRequest
{
    CharacterId = "character-id",
    CoreServiceUrl = "https://...",
    Transport = "livekit"
};
var roomDetails = await client.Rooms.ConnectAsync(roomRequest);
```

## Features

- **Task-based async/await** - Modern C# async patterns with cancellation support
- **WebGL compatible** - Dual transport: `HttpClient` for Editor/Standalone, `UnityWebRequest` for WebGL
- **Type-safe** - Strongly-typed models and exceptions
- **Instance-based** - Thread-safe, disposable client with dependency injection support
- **Service organization** - Logical grouping: Characters, Users, Ltm, Animations, Rooms, Narratives

## Installation

This SDK requires:
- Unity 2022.3+ or Unity 6
- Newtonsoft.Json (via Unity Package Manager or NuGet)

## API Reference

### ConvaiRestClient

The main entry point for all API operations.

```csharp
var options = new ConvaiRestClientOptions("your-api-key")
{
    Environment = ConvaiEnvironment.Production, // or Beta
    Timeout = TimeSpan.FromSeconds(30)
};

using var client = new ConvaiRestClient(options);
```

### Character Service

```csharp
// Get character details
CharacterDetails details = await client.Characters.GetDetailsAsync("char-id");

// Update character
await client.Characters.UpdateAsync("char-id", memoryEnabled: true);
```

### User Service

```csharp
// Validate API key
ReferralSourceStatus status = await client.Users.ValidateApiKeyAsync();

// Update referral source
await client.Users.UpdateReferralSourceAsync("source-name");

// Get usage statistics
UserUsageData usage = await client.Users.GetUsageAsync();
```

### LTM (Long-Term Memory) Service

```csharp
// Create speaker
string speakerId = await client.Ltm.CreateSpeakerAsync("Player Name");

// List speakers (speaker_id compatibility API)
List<SpeakerIDDetails> speakers = await client.Ltm.GetSpeakersAsync();

// Delete speaker
await client.Ltm.DeleteSpeakerAsync("speaker-id");

// List end users (modern end_user_id)
EndUsersListResponse endUsers = await client.Ltm.GetEndUsersAsync(limit: 100);

// Delete end user
await client.Ltm.DeleteEndUserAsync("end-user-id");

// Get/Set LTM status for a character
bool isEnabled = await client.Ltm.GetStatusAsync("char-id");
await client.Ltm.SetStatusAsync("char-id", enabled: true);
```

### Animation Service

```csharp
// Get animation list
ServerAnimationListResponse list = await client.Animations.GetListAsync(page: 1, status: "active");

// Get animation details
ServerAnimationDataResponse data = await client.Animations.GetAsync("animation-id");
```

### Room Service

```csharp
var request = new RoomConnectionRequest
{
    CharacterId = "char-id",
    CoreServiceUrl = "https://live.convai.com/connect",
    Transport = "livekit",
    ConnectionType = "conversation",
    LlmProvider = "convai",
    EndUserId = "player-uuid",  // Optional: for cross-session LTM
    TurnDetectionConfig = TurnDetectionConfig.CreateDefault()  // Optional: smart turn detection
};

RoomDetails room = await client.Rooms.ConnectAsync(request);
Console.WriteLine($"Room: {room.RoomName}, Token: {room.Token}");
```

`RoomService.ConnectAsync` automatically sends `invocation_metadata` in `/connect` payloads:
- `source` defaults to `unity_sdk`
- `client_version` defaults to `0.1.0`

You can override defaults with either:
- `ConvaiRestClientOptions.InvocationSource` / `ConvaiRestClientOptions.ClientVersion`
- `RoomConnectionRequest.InvocationMetadata` values (request-level values take precedence)

### Narrative Service

```csharp
// List sections
List<SectionData> sections = await client.Narratives.ListSectionsAsync("char-id");

// Create section
CreateSectionResponse created = await client.Narratives.CreateSectionAsync(
    "char-id", "Intro", "Greet the player");

// Get section
SectionData section = await client.Narratives.GetSectionAsync("char-id", "section-id");

// Update section
EditSectionResponse updated = await client.Narratives.UpdateSectionAsync(
    "char-id", "section-id", new NarrativeSectionUpdateData { SectionName = "Welcome" });

// Delete section
await client.Narratives.DeleteSectionAsync("char-id", "section-id");

// Toggle narrative graph
await client.Narratives.ToggleNarrativeDrivenAsync("char-id", enabled: true);

// Manage decisions
await client.Narratives.AddDecisionAsync("char-id", "from-section", "to-section", "criteria");
await client.Narratives.UpdateDecisionAsync("char-id", "from", "to", "criteria", 
    new NarrativeDecisionUpdatePayload { Priority = 1 });
await client.Narratives.DeleteDecisionAsync("char-id", "from", "to", "criteria");

// Manage triggers
TriggerData trigger = await client.Narratives.CreateTriggerAsync(
    "char-id", "StartTrigger", "Player enters", destinationSection: "intro");
List<TriggerData> triggers = await client.Narratives.ListTriggersAsync("char-id");
await client.Narratives.UpdateTriggerAsync("char-id", "trigger-id", 
    new NarrativeTriggerUpdateData { TriggerName = "NewName" });
await client.Narratives.DeleteTriggerAsync("char-id", "trigger-id");
```

## Error Handling

All errors are thrown as `ConvaiRestException` with categorized error types:

```csharp
try
{
    var character = await client.Characters.GetDetailsAsync("char-id");
}
catch (ConvaiRestException ex)
{
    switch (ex.Category)
    {
        case ErrorCategory.Authentication:
            Console.WriteLine("Invalid API key");
            break;
        case ErrorCategory.NotFound:
            Console.WriteLine("Character not found");
            break;
        case ErrorCategory.Transport:
            Console.WriteLine($"Network error: {ex.Message}");
            break;
        case ErrorCategory.ParseError:
            Console.WriteLine($"Invalid response: {ex.Message}");
            break;
        default:
            Console.WriteLine($"API error: {ex.Message}");
            break;
    }
}
```

## Cancellation Support

All async methods support `CancellationToken`:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

try
{
    var character = await client.Characters.GetDetailsAsync("char-id", cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Request timed out or was cancelled");
}
```

## WebGL Considerations

The SDK automatically selects the appropriate transport:
- **Editor/Standalone/Mobile**: Uses `HttpClientTransport` (System.Net.Http)
- **WebGL Runtime**: Uses `UnityWebRequestTransport` (UnityWebRequest)

### CORS

For WebGL builds, ensure your Convai API endpoints support CORS headers. The SDK sets headers per-request to avoid WebGL preflight issues.

## Custom Transport

You can provide a custom transport implementation:

```csharp
public class CustomTransport : IConvaiHttpTransport
{
    public Task<ConvaiHttpResponse> SendAsync(ConvaiHttpRequest request, CancellationToken ct)
    {
        // Custom implementation
    }
    
    public void Dispose() { }
}

var options = new ConvaiRestClientOptions("api-key")
{
    CustomTransport = new CustomTransport()
};
```

## Project Structure

```
convai-csharp-rest-main/
├── ConvaiRestClient.cs         # Main client with service properties
├── ConvaiRestClientOptions.cs  # Configuration options
├── ConvaiRestException.cs      # Typed exceptions
├── Services/
│   ├── ConvaiServiceBase.cs    # Base class with helpers
│   ├── CharacterService.cs     # Character operations
│   ├── UserService.cs          # User operations
│   ├── LtmService.cs           # Long-term memory operations
│   ├── AnimationService.cs     # Animation operations
│   ├── RoomService.cs          # Room connection
│   └── NarrativeService.cs     # Narrative design CRUD
├── Transport/
│   ├── IConvaiHttpTransport.cs # Transport interface
│   ├── ConvaiHttpRequest.cs    # Request model
│   ├── ConvaiHttpResponse.cs   # Response model
│   ├── HttpClientTransport.cs  # System.Net.Http impl
│   └── UnityWebRequestTransport.cs # Unity impl for WebGL
├── ConvaiRestModels.cs         # Request/response models
├── CharacterDetails.cs         # Character model
├── UserUsageData.cs            # Usage data model
└── Internal/                   # Shared response models
```

## Dependencies

- **Newtonsoft.Json** (v13.0.3+): JSON serialization
- **System.Net.Http**: HTTP client (Editor/Standalone)
- **UnityEngine.Networking**: UnityWebRequest (WebGL)

## License

Copyright Convai. All rights reserved.
