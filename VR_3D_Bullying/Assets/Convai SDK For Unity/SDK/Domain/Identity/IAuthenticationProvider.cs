namespace Convai.Domain.Identity
{
    /// <summary>
    ///     Provides authentication-based user identification for Convai connections.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Implementations of this interface provide authenticated user identity information,
    ///         enabling user-specific features such as:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <description>Account-based Long-Term Memory (LTM) - Characters remember users across devices</description>
    ///         </item>
    ///         <item>
    ///             <description>User-specific analytics and tracking</description>
    ///         </item>
    ///         <item>
    ///             <description>Cross-device session continuity for logged-in users</description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         When an authenticated user is present, the SDK can use the authenticated user ID
    ///         instead of the device-based ID for more reliable cross-session tracking.
    ///     </para>
    ///     <para>
    ///         <b>Integration Example:</b>
    ///     </para>
    ///     <code>
    /// public class MyAuthProvider : IAuthenticationProvider
    /// {
    ///     public bool IsAuthenticated => UserSession.Current != null;
    ///     public string GetAuthenticatedUserId() => UserSession.Current?.UserId;
    ///     public string GetAuthenticationToken() => UserSession.Current?.AccessToken;
    /// }
    /// </code>
    /// </remarks>
    public interface IAuthenticationProvider
    {
        /// <summary>
        ///     Gets a value indicating whether a user is currently authenticated.
        /// </summary>
        /// <value>
        ///     <c>true</c> if a user is authenticated and their identity can be retrieved;
        ///     <c>false</c> if no user is logged in.
        /// </value>
        public bool IsAuthenticated { get; }

        /// <summary>
        ///     Gets the unique identifier for the authenticated user.
        /// </summary>
        /// <returns>
        ///     The authenticated user's unique identifier, or <c>null</c> if no user is authenticated.
        ///     This should be a stable identifier from your authentication system (e.g., a user ID from your database).
        /// </returns>
        public string GetAuthenticatedUserId();

        /// <summary>
        ///     Gets the authentication token for the current user session.
        /// </summary>
        /// <returns>
        ///     The current authentication token (e.g., JWT, session token), or <c>null</c> if no user is authenticated.
        ///     This token may be used for server-side validation of user identity.
        /// </returns>
        public string GetAuthenticationToken();
    }
}
