using Convai.Runtime.Behaviors;

namespace Convai.Runtime.Presentation.Services
{
    /// <summary>
    ///     Service for accessing player input capabilities.
    ///     Decouples UI components from direct ConvaiPlayer reference.
    /// </summary>
    /// <remarks>
    ///     This service replaces the direct ConvaiPlayer reference that was previously
    ///     held by ConvaiTranscriptHandler. It provides a clean DI-based approach
    ///     to accessing player input functionality from transcript UIs.
    /// </remarks>
    public interface IPlayerInputService
    {
        /// <summary>
        ///     Gets the player agent interface.
        ///     May be null if no player has been registered.
        /// </summary>
        public IConvaiPlayerAgent Player { get; }

        /// <summary>
        ///     Gets whether a player agent has been registered.
        /// </summary>
        public bool HasPlayer { get; }

        /// <summary>
        ///     Sets the player agent (called during scene initialization).
        /// </summary>
        /// <param name="player">The player agent to register.</param>
        public void SetPlayer(IConvaiPlayerAgent player);

        /// <summary>
        ///     Clears the player agent reference.
        /// </summary>
        public void ClearPlayer();
    }
}
