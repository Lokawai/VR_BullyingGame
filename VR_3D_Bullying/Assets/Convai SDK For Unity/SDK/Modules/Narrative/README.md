# Narrative Module (Narrative Design)

This module adds Unity components for Convai Narrative Design (sections, decisions, triggers) so you can drive story/quest progression without hard-coded dialogue trees.

If you're integrating the SDK, start with:

- `Documentation~/SETUP.md`

Prerequisites:

- API key set in `Edit > Project Settings > Convai SDK`
- A valid Character ID on your `ConvaiCharacter`

## Overview

The Convai Narrative Design system enables goal-oriented conversation flows using sections, decisions, and triggers that guide the story forward without rigid dialogue trees. This module provides Unity integration for tracking section changes, sending triggers, and managing dynamic template keys.

## Quick Start

### Setting Up the Manager

1. Add **Convai Narrative Design Manager** component to your character GameObject (Add Component > Convai > Narrative Design Manager)
2. Assign your **ConvaiCharacter** to the Character Reference field
3. Click **"Sync with Backend"** to fetch all sections from your Convai Dashboard
4. Configure **OnSectionStart** and **OnSectionEnd** Unity Events for each section
5. Done! Events will fire automatically as the narrative progresses

### Setting Up Triggers

1. Add **Convai Narrative Design Trigger** component to any GameObject with a Collider
2. Assign your **ConvaiCharacter** to the Character field
3. Click **"Fetch Triggers"** to load available triggers from the backend
4. Select a trigger from the dropdown
5. Choose an activation mode (Collision, Proximity, Manual, or Time-based)
6. Done! The trigger fires based on your chosen activation mode

---

## Convai Narrative Design Manager

The `ConvaiNarrativeDesignManager` is a MonoBehaviour component that manages narrative section transitions and invokes Unity events on section changes.

### Auto-Fetch and Sync

The manager automatically fetches sections from the Convai backend when a character is assigned:

- **Auto-fetch on character change**: When you assign a character in the Inspector, sections are automatically fetched
- **Manual sync**: Click "Sync with Backend" button to refresh sections at any time
- **Smart sync**: Updates section names while preserving your Unity Event configurations

### Section Sync Behavior

The sync algorithm is designed to preserve your work:

| Scenario | What Happens |
|----------|--------------|
| Section exists on both client and backend | Name is updated, **Unity Events preserved** |
| New section on backend | Added to list with empty events |
| Section deleted on backend | Marked as "Orphaned" with visual indicator, **events preserved** |
| Section name changed on backend | Name updates automatically, ID stays the same, **events preserved** |

**Orphaned Sections**: When a section is deleted on the backend, it's marked as orphaned (shown in a different color in the Inspector). Your configured Unity Events are preserved so you can reference them if needed.

### Inspector Configuration

The Narrative Design Manager exposes the following in the Inspector:

- **Character Reference**: The ConvaiCharacter to send commands to
- **Sync Status**: Shows last sync time, error messages, and section counts
- **Narrative Sections**: Collapsible list of sections with:
  - Section ID and Name (read-only, synced from backend)
  - Orphaned indicator (if section was deleted on backend)
  - OnSectionStart and OnSectionEnd Unity Events
- **Template Keys**: Key-value pairs for dynamic placeholder resolution
- **Events**:
  - **On Any Section Changed**: Fires when any section changes (receives section ID)
  - **On Section Data Received**: Fires with full section data (includes BT code)
  - **On Sections Synced**: Fires after a sync operation completes

### Template Keys

Template keys allow dynamic placeholder resolution in narrative objectives. For example, `{PlayerName}` in an objective will be replaced with the player's actual name.

**In the Inspector:**
1. Expand **Template Keys** section
2. Add key-value pairs (e.g., Key: "PlayerName", Value: "John")
3. Click "Send Template Keys to Server" at runtime

**Via Code:**

```csharp
// Get reference to the manager
ConvaiNarrativeDesignManager manager = GetComponent<ConvaiNarrativeDesignManager>();

// Configure template keys
manager.UpdateTemplateKey("PlayerName", "John");
manager.UpdateTemplateKey("TimeOfDay", "Morning");

// Send all template keys to the server
manager.SendTemplateKeysUpdate();

// Or update and send in one call
manager.UpdateAndSendTemplateKey("CurrentQuest", "Dragon Slayer");
```

### Section Data Events

**Via Inspector:**
- Configure **On Any Section Changed** to react to any section change
- Configure **On Section Data Received** to access full BT data

**Via Code:**

```csharp
ConvaiNarrativeDesignManager manager = GetComponent<ConvaiNarrativeDesignManager>();

// Subscribe to Unity Event
manager.OnSectionDataReceived.AddListener((sectionData) =>
{
    Debug.Log($"Section: {sectionData.SectionId}");
    if (!string.IsNullOrEmpty(sectionData.BehaviorTreeCode))
    {
        ExecuteBehaviorTree(sectionData.BehaviorTreeCode, sectionData.BehaviorTreeConstants);
    }
});
```

---

## Convai Narrative Design Trigger

The `ConvaiNarrativeDesignTrigger` component provides flexible trigger activation with multiple modes.

### Activation Modes

| Mode | Description |
|------|-------------|
| **Collision** | Triggers when a player enters the collider (OnTriggerEnter) |
| **Proximity** | Triggers when player is within a configurable radius |
| **Manual** | Only triggers when `InvokeTrigger()` is called from code |
| **Time-Based** | Triggers after player enters zone + configurable delay |

### Setup

1. Add the component to any GameObject
2. For Collision/Time-Based modes: Add a Collider and enable "Is Trigger"
3. Assign a ConvaiCharacter
4. Select a trigger from the dropdown (auto-fetched from backend)
5. Configure the activation mode and its settings

### Mode-Specific Settings

**Collision Mode:**
- Player Tag: Tag to identify player objects
- Player Layer: Layer mask for player detection
- Trigger Once: If enabled, only fires once until reset

**Proximity Mode:**
- Detection Radius: Distance within which the trigger activates
- Visual gizmo shown in Scene view
- Continuous checking in Update loop

**Time-Based Mode:**
- Delay (seconds): Time to wait after player enters zone
- Cancels if player leaves before delay completes

**Manual Mode:**
- No automatic activation
- Call `InvokeTrigger()` from code or Unity Events

### Events

- **On Trigger Activated**: Fires when the trigger successfully sends
- **On Player Enter Zone**: Fires when player enters the trigger zone
- **On Player Exit Zone**: Fires when player exits the trigger zone

### Programmatic Usage

```csharp
ConvaiNarrativeDesignTrigger trigger = GetComponent<ConvaiNarrativeDesignTrigger>();

// Configure trigger
trigger.SetTrigger("trigger-id", "EnterCastle", "Player entered the castle");
trigger.SetActivationMode(TriggerActivationMode.Manual);

// Invoke manually
trigger.InvokeTrigger();

// Reset to allow triggering again
trigger.ResetTrigger();

// Or use directly on character
character.SendTrigger("EnterCastle", "Player entered the castle");
```

---

## Dynamic Info

Send runtime context to the character using Dynamic Info:

```csharp
// Inject context that affects the character's responses
character.SendDynamicInfo("The player just picked up the magic sword");
```

This context is processed by the backend and influences the character's next response.

---

## Fetching Narrative Data

Use `NarrativeDesignFetcher` for programmatic access to narrative data:

```csharp
using Convai.Modules.Narrative;

// Fetch sections
var sectionsResult = await NarrativeDesignFetcher.FetchSectionsAsync(characterId);
if (sectionsResult.Success)
{
    foreach (var section in sectionsResult.Data)
    {
        Debug.Log($"Section: {section.SectionName} ({section.SectionId})");
    }
}

// Fetch triggers
var triggersResult = await NarrativeDesignFetcher.FetchTriggersAsync(characterId);
if (triggersResult.Success)
{
    foreach (var trigger in triggersResult.Data)
    {
        Debug.Log($"Trigger: {trigger.TriggerName} -> {trigger.DestinationSection}");
    }
}

// Fetch both at once
var (sections, triggers) = await NarrativeDesignFetcher.FetchAllAsync(characterId);
```

---

## Domain Events (EventHub Integration)

The narrative design system integrates with the Convai EventHub for decoupled event handling:

### NarrativeSectionChanged Event

```csharp
using Convai.Domain.DomainEvents.Narrative;
using Convai.Domain.EventSystem;

public class NarrativeEventHandler : MonoBehaviour
{
    private IEventHub _eventHub;
    private SubscriptionToken _token;

    void OnEnable()
    {
        _token = _eventHub.Subscribe<NarrativeSectionChanged>(OnSectionChanged);
    }

    void OnDisable()
    {
        _eventHub.Unsubscribe(_token);
    }

    private void OnSectionChanged(NarrativeSectionChanged evt)
    {
        Debug.Log($"Section: {evt.SectionId}, Character: {evt.CharacterId}");

        if (!string.IsNullOrEmpty(evt.BehaviorTreeCode))
        {
            // Handle behavior tree code
        }
    }
}
```

---

## Best Practices

1. **Use Auto-Fetch**: Let the manager automatically fetch sections when you assign a character
2. **Don't Delete Sections in Inspector**: Use the backend to manage sections; the sync will handle updates
3. **Check Orphaned Status**: Orphaned sections indicate backend changes - review and clean up as needed
4. **Use Appropriate Activation Mode**: Choose the simplest mode that meets your needs
5. **Configure Events in Inspector**: Use Inspector for simple reactions (sounds, animations)
6. **Use Code for Complex Logic**: Subscribe to events programmatically for complex behavior
7. **Test with Multiple Characters**: Each manager syncs independently with its assigned character
