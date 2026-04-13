using Convai.Domain.EventSystem;
using Convai.Domain.Logging;

namespace Convai.Shared.Interfaces
{
    /// <summary>
    ///     Injection contract for lip sync components discovered by ConvaiCompositionRoot.
    /// </summary>
    public interface IInjectableLipSyncComponent
    {
        /// <summary>
        ///     Injects runtime services required by lip sync.
        /// </summary>
        public void Inject(IEventHub eventHub, ILogger logger = null);
    }
}
