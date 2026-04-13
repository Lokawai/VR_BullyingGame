using Convai.Runtime.Services.CharacterLocator;

namespace Convai.Runtime.Presentation.Services
{
    /// <summary>
    ///     Aggregate interface for transcript UI dependencies.
    ///     Simplifies DI for custom transcript implementations by bundling all required services.
    /// </summary>
    /// <remarks>
    ///     This interface reduces the complexity of TranscriptUIBase.Inject() from 5 parameters to 1.
    ///     Custom transcript UI implementations can request this single interface instead of
    ///     managing multiple service dependencies.
    /// </remarks>
    public interface ITranscriptUIServices
    {
        /// <summary>Service for tracking which characters are visible to the player.</summary>
        public IVisibleCharacterService VisibilityService { get; }

        /// <summary>Service for locating character references by ID.</summary>
        public IConvaiCharacterLocatorService CharacterLocator { get; }

        /// <summary>Service for player input access (text input, player reference).</summary>
        public IPlayerInputService PlayerInput { get; }

        /// <summary>Default fade duration for UI animations.</summary>
        public float FadeDuration { get; }
    }

    /// <summary>
    ///     Default implementation of ITranscriptUIServices.
    ///     Registered in the DI container by the ConvaiManager-managed bootstrap pipeline.
    /// </summary>
    internal class TranscriptUIServices : ITranscriptUIServices
    {
        public TranscriptUIServices(
            IVisibleCharacterService visibilityService,
            IConvaiCharacterLocatorService characterLocator,
            IPlayerInputService playerInput,
            float fadeDuration = 0.5f)
        {
            VisibilityService = visibilityService;
            CharacterLocator = characterLocator;
            PlayerInput = playerInput;
            FadeDuration = fadeDuration;
        }

        public IVisibleCharacterService VisibilityService { get; }
        public IConvaiCharacterLocatorService CharacterLocator { get; }
        public IPlayerInputService PlayerInput { get; }
        public float FadeDuration { get; }
    }
}
