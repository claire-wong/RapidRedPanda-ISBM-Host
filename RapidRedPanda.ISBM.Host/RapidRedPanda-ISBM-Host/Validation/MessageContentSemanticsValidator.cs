using RapidRedPanda.ISBM.Host.Messaging;
using System.Text.Json;

namespace RapidRedPanda.ISBM.Host.Validation;

internal enum MessageContentKind
{
    Json,
    String,
    Encoded
}

internal sealed record MessageContentValidationResult(
    bool Succeeded,
    MessageContentKind? Kind,
    string? PayloadMediaType,
    string? ContentEncoding)
{
    public static MessageContentValidationResult Invalid { get; } = new(false, null, null, null);

    public static MessageContentValidationResult Valid(
        MessageContentKind kind,
        string? payloadMediaType,
        string? contentEncoding = null)
    {
        return new MessageContentValidationResult(true, kind, payloadMediaType, contentEncoding);
    }
}

internal static class MessageContentSemanticsValidator
{
    public static MessageContentValidationResult Validate(JsonElement messageContent)
    {
        if (!RestMessageContentMapper.TryFromRest(messageContent, out InternalMessageContent? content))
        {
            return MessageContentValidationResult.Invalid;
        }

        return content!.Kind switch
        {
            InternalMessageContentKind.Json =>
                MessageContentValidationResult.Valid(MessageContentKind.Json, null),

            InternalMessageContentKind.String =>
                MessageContentValidationResult.Valid(MessageContentKind.String, content.MediaType),

            InternalMessageContentKind.Binary =>
                MessageContentValidationResult.Valid(MessageContentKind.Encoded, content.MediaType, content.ContentEncoding),

            _ => MessageContentValidationResult.Invalid
        };
    }
}
