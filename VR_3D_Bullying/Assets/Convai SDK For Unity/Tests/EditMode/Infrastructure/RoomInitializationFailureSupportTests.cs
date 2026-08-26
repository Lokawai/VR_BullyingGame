using System;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Infrastructure.Networking;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Infrastructure
{
    [TestFixture]
    public class RoomInitializationFailureSupportTests
    {
        [Test]
        public void FromRequestFailure_WithHttp400InvalidStoredSession_PublishesBadRequestWithoutRetry()
        {
            RoomInitializationFailureOutcome outcome = RoomInitializationFailureSupport.FromRequestFailure(
                "stored-session",
                enableSessionResume: true,
                failureMessage: "HTTP request failed: Bad Request (Code: 400). Response: Invalid character_session_id",
                InvalidStoredSessionRecoveryPolicy.RetryWithoutStoredSessionDisallowed,
                responseCode: 400,
                sessionErrorMessage: "HTTP request failed: Bad Request (Code: 400)");

            Assert.That(outcome.Category, Is.EqualTo(RoomInitializationFailureCategory.RoomDetailsRequestFailed));
            Assert.That(outcome.RecoveryOutcome.Status,
                Is.EqualTo(RoomInitializationOutcomeStatus.InvalidStoredSession));
            Assert.That(outcome.RecoveryOutcome.ShouldRetryWithoutStoredSession, Is.False);
            Assert.That(outcome.RecoveryOutcome.ShouldClearStoredSession, Is.False);
            Assert.That(outcome.ShouldPublishSessionError, Is.True);
            Assert.That(outcome.SessionErrorCode, Is.EqualTo(SessionErrorCodes.ConnectionBadRequest));
            Assert.That(outcome.SessionErrorMessage, Is.EqualTo("HTTP request failed: Bad Request (Code: 400)"));
            Assert.That(outcome.SessionErrorIsRecoverable, Is.False);
            Assert.That(outcome.DiagnosticsMetadata, Does.Contain("responseCode=400"));
        }

        [Test]
        public void FromRequestException_WithFetchException_PreservesDetailedFailureAndRecoverableHttpCode()
        {
            var exception = new RoomInitializationFetchException(
                "HTTP request failed: Service Unavailable (Code: 503). Response: temporary outage",
                "HTTP request failed: Service Unavailable (Code: 503)",
                responseCode: 503);

            RoomInitializationFailureOutcome outcome = RoomInitializationFailureSupport.FromRequestException(
                attemptedCharacterSessionId: null,
                enableSessionResume: false,
                exception,
                InvalidStoredSessionRecoveryPolicy.RetryWithoutStoredSessionAllowed);

            Assert.That(outcome.FailureMessage,
                Is.EqualTo("HTTP request failed: Service Unavailable (Code: 503). Response: temporary outage"));
            Assert.That(outcome.SessionErrorMessage,
                Is.EqualTo("HTTP request failed: Service Unavailable (Code: 503)"));
            Assert.That(outcome.SessionErrorCode, Is.EqualTo(SessionErrorCodes.ConnectionServiceUnavailable));
            Assert.That(outcome.SessionErrorIsRecoverable, Is.True);
        }

        [Test]
        public void FromRequestException_WithGenericException_MapsConnectionFailed()
        {
            RoomInitializationFailureOutcome outcome = RoomInitializationFailureSupport.FromRequestException(
                attemptedCharacterSessionId: null,
                enableSessionResume: false,
                new Exception("boom"),
                InvalidStoredSessionRecoveryPolicy.RetryWithoutStoredSessionAllowed);

            Assert.That(outcome.Category, Is.EqualTo(RoomInitializationFailureCategory.RoomDetailsRequestFailed));
            Assert.That(outcome.SessionErrorCode, Is.EqualTo(SessionErrorCodes.ConnectionFailed));
            Assert.That(outcome.SessionErrorMessage, Is.EqualTo("boom"));
            Assert.That(outcome.ShouldPublishSessionError, Is.True);
            Assert.That(outcome.RecoveryOutcome.Status, Is.EqualTo(RoomInitializationOutcomeStatus.Failed));
        }

        [Test]
        public void FromAuthTokenFetchFailure_UsesExpectedCodeStageAndRecoverability()
        {
            var exception = new InvalidOperationException("token endpoint unavailable");

            ConnectionFailure failure = RoomInitializationFailureSupport.FromAuthTokenFetchFailure(
                "Unable to resolve a fresh credential.",
                exception);

            Assert.That(failure.Code, Is.EqualTo(SessionErrorCodes.ConnectionAuthTokenFetchFailed));
            Assert.That(failure.Message, Is.EqualTo("Unable to resolve a fresh credential."));
            Assert.That(failure.Stage, Is.EqualTo(SessionErrorStage.ConnectApi));
            Assert.That(failure.IsRecoverable, Is.True);
            Assert.That(failure.Exception, Is.SameAs(exception));
        }

        [Test]
        public void FromAuthTokenFetchFailure_WithEmptyMessage_UsesSafeFallback()
        {
            ConnectionFailure failure = RoomInitializationFailureSupport.FromAuthTokenFetchFailure("   ");

            Assert.That(failure.Code, Is.EqualTo(SessionErrorCodes.ConnectionAuthTokenFetchFailed));
            Assert.That(failure.Message, Is.EqualTo("Failed to fetch an auth token before connecting."));
            Assert.That(failure.Stage, Is.EqualTo(SessionErrorStage.ConnectApi));
            Assert.That(failure.IsRecoverable, Is.True);
            Assert.That(failure.Exception, Is.Null);
        }

        [Test]
        public void FromInvalidRoomDetails_DoesNotProducePublishableSessionError()
        {
            RoomInitializationFailureOutcome outcome = RoomInitializationFailureSupport.FromInvalidRoomDetails(
                "stored-session",
                enableSessionResume: true,
                failureMessage: "Failed to get room details",
                InvalidStoredSessionRecoveryPolicy.RetryWithoutStoredSessionDisallowed);

            Assert.That(outcome.Category, Is.EqualTo(RoomInitializationFailureCategory.InvalidRoomDetails));
            Assert.That(outcome.RecoveryOutcome.Status, Is.EqualTo(RoomInitializationOutcomeStatus.Failed));
            Assert.That(outcome.ShouldPublishSessionError, Is.True);
            Assert.That(outcome.SessionErrorCode, Is.EqualTo(SessionErrorCodes.ConnectionFailed));
            Assert.That(outcome.SessionErrorMessage, Is.EqualTo("Failed to get room details"));
        }
    }
}
