using System.Collections.Generic;
using Convai.Runtime.Behaviors;

namespace Convai.Runtime.Services.CharacterLocator
{
    /// <summary>
    ///     Unity-layer, agent-based character locator abstraction.
    /// </summary>
    /// <remarks>
    ///     Exposes Convai Characters and players as IConvaiCharacterAgent/IConvaiPlayerAgent so
    ///     Unity-facing systems do not need to reference legacy ConvaiCharacter/ConvaiPlayer
    ///     implementations in Convai.Scripts.
    /// </remarks>
    public interface IConvaiCharacterLocatorService
    {
        /// <summary>
        ///     Gets the currently known Character agents.
        /// </summary>
        public IReadOnlyList<IConvaiCharacterAgent> GetCharacterAgents();

        /// <summary>
        ///     Gets the currently known player agents.
        /// </summary>
        public IReadOnlyList<IConvaiPlayerAgent> GetPlayerAgents();

        /// <summary>
        ///     Attempts to resolve a Character agent by its character identifier.
        /// </summary>
        /// <param name="characterId">The character identifier.</param>
        /// <param name="agent">The resolved Character agent, if found.</param>
        /// <returns><c>true</c> if a Character with the given identifier was found; otherwise, <c>false</c>.</returns>
        public bool TryGetCharacter(string characterId, out IConvaiCharacterAgent agent);

        /// <summary>
        ///     Attempts to resolve a player agent by its speaker identifier.
        /// </summary>
        /// <param name="speakerId">The speaker identifier associated with the player.</param>
        /// <param name="agent">The resolved player agent, if found.</param>
        /// <returns><c>true</c> if a player with the given speaker identifier was found; otherwise, <c>false</c>.</returns>
        public bool TryGetPlayer(string speakerId, out IConvaiPlayerAgent agent);

        /// <summary>
        ///     Registers a Character agent with the locator.
        /// </summary>
        /// <param name="character">The Character agent to register.</param>
        public void AddCharacter(IConvaiCharacterAgent character);

        /// <summary>
        ///     Unregisters a Character agent from the locator.
        /// </summary>
        /// <param name="character">The Character agent to unregister.</param>
        public void RemoveCharacter(IConvaiCharacterAgent character);

        /// <summary>
        ///     Registers a player agent with the locator.
        /// </summary>
        /// <param name="player">The player agent to register.</param>
        public void AddPlayer(IConvaiPlayerAgent player);

        /// <summary>
        ///     Unregisters a player agent from the locator.
        /// </summary>
        /// <param name="player">The player agent to unregister.</param>
        public void RemovePlayer(IConvaiPlayerAgent player);
    }
}
