using Nexaflow.AiHost.Provisioning;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.LocalAi;

/// <summary>A model download's lack of room, and any other failure of one, as a person reads it in their language.</summary>
internal static class ModelRoomText
{
    extension(ModelRoom room)
    {
        /// <summary>Why the fetch is refused: what it needs, and what is free where.</summary>
        public string Refusal => Str.Format("Shell.Ai.Room.ShortFormat", SizeFormatter.FormatBytes(room.Bytes), SizeFormatter.FormatBytes(room.Free ?? 0), room.Drive);
    }

    /// <summary>What a failed download says: a refusal for want of room in the person's language, else what went wrong.</summary>
    public static string MessageOf(Exception failure) => failure is ModelRoomException refused ? refused.Room.Refusal : failure.Message;
}
