using System;

namespace Convai.Domain.EventSystem
{
    /// <summary>
    ///     Adapter that wraps an Action delegate as an IEventSubscriber.
    ///     Internal helper for Subscribe(Action) convenience method.
    /// </summary>
    /// <typeparam name="TEvent">Type of event this subscriber handles</typeparam>
    /// <remarks>
    ///     This class is an implementation detail of the event hub. It allows the event hub
    ///     to accept both IEventSubscriber implementations and simple Action delegates.
    ///     When a user calls eventHub.Subscribe(action), the event hub internally wraps
    ///     the action in an ActionEventSubscriber and treats it like any other subscriber.
    ///     This is marked internal because users should not instantiate this directly.
    ///     They should use the IEventHub.Subscribe(Action) method instead.
    /// </remarks>
    internal sealed class ActionEventSubscriber<TEvent> : IEventSubscriber<TEvent>
    {
        private readonly Action<TEvent> _handler;

        /// <summary>
        ///     Creates a new ActionEventSubscriber wrapping the specified action.
        /// </summary>
        /// <param name="handler">Action to invoke when event is received</param>
        /// <exception cref="ArgumentNullException">Thrown if handler is null</exception>
        public ActionEventSubscriber(Action<TEvent> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>
        ///     Called when an event is published. Invokes the wrapped action.
        /// </summary>
        /// <param name="event">Event instance to pass to the action</param>
        public void OnEvent(TEvent @event) => _handler(@event);
    }
}
