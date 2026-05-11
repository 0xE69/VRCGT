using VRCGT.Core;

namespace VRCGT.Server;

/// <summary>
/// Extension methods for surfacing confirmation errors as consistent HTTP 409 responses.
/// </summary>
internal static class ConfirmationResults
{
    /// <summary>
    /// Returns HTTP 409 with a JSON body that tells the client what confirmation
    /// token it needs to send to retry the action.
    ///
    /// Body:
    /// <code>
    /// {
    ///   "error": "confirmation_required",
    ///   "action": "DELETE_GROUP",
    ///   "requiredToken": "DELETE_GROUP:grp_abc"
    /// }
    /// </code>
    /// </summary>
    internal static IResult ConfirmationRequired(string action, string resourceId)
        => Results.Json(
            new
            {
                error         = "confirmation_required",
                action,
                requiredToken = ConfirmationGuard.BuildToken(action, resourceId),
            },
            statusCode: 409);
}
