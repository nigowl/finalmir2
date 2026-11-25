namespace Crystal.Native;

public enum ChatChannel { All, Public, Whisper, Group, Guild, Shout, System, Relationship, Mentor }

public static class ChatChannels
{
    public static string Caption(ChatChannel channel) => channel switch
    {
        ChatChannel.Public => "公共", ChatChannel.Whisper => "私信", ChatChannel.Group => "队伍", ChatChannel.Guild => "行会",
        ChatChannel.Shout => "喊话", ChatChannel.System => "系统", ChatChannel.Relationship => "夫妻", ChatChannel.Mentor => "师徒", _ => "综合"
    };

    public static bool Includes(ChatChannel channel, ChatType type) => channel == ChatChannel.All || (channel switch
    {
        ChatChannel.Public => type == ChatType.Normal,
        ChatChannel.Whisper => type is ChatType.WhisperIn or ChatType.WhisperOut,
        ChatChannel.Group => type == ChatType.Group, ChatChannel.Guild => type == ChatType.Guild,
        ChatChannel.Shout => type is ChatType.Shout or ChatType.Shout2 or ChatType.Shout3,
        ChatChannel.System => type is ChatType.Hint or ChatType.System or ChatType.System2 or ChatType.LevelUp or ChatType.Announcement or ChatType.Trainer or ChatType.LineMessage,
        ChatChannel.Relationship => type == ChatType.Relationship, ChatChannel.Mentor => type == ChatType.Mentor,
        _ => false
    });

    public static string BubbleText(string text, string sender)
    {
        foreach (var prefix in new[] { sender + ":", "?????:" })
            if (text.StartsWith(prefix, StringComparison.Ordinal)) { text = text[prefix.Length..]; break; }
        text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var elements = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        return elements.Length <= 80 ? text : text[..elements[80]] + "…";
    }

    public static string? Format(ChatChannel channel, string text, string target, bool inGroup, bool inGuild, out string? error)
    {
        error = null; text = text.Trim(); target = target.Trim();
        if (text.Length == 0) return null;
        // 手打的原协议前缀有优先权，GM命令不跟随频道改写。
        if (text[0] is not ('@' or '/' or '!') && !text.StartsWith(":)", StringComparison.Ordinal))
        {
            error = channel switch
            {
                ChatChannel.Whisper when target.Length == 0 || target.Length > Globals.MaxCharacterNameLength || target.Any(char.IsWhiteSpace) || target.Contains('/') => "请填写要私信的角色名称。",
                ChatChannel.Group when !inGroup => "加入队伍后才能发送队伍消息。",
                ChatChannel.Guild when !inGuild => "加入行会后才能发送行会消息。",
                ChatChannel.System => "系统频道仅供查看，请切换到聊天频道发送消息。", _ => null
            };
            if (error != null) return null;
            text = channel switch
            {
                ChatChannel.Whisper => $"/{target} {text}", ChatChannel.Group => $"!!{text}", ChatChannel.Guild => $"!~{text}",
                ChatChannel.Shout => $"!{text}", ChatChannel.Relationship => $":){text}", ChatChannel.Mentor => $"!#{text}", _ => text
            };
        }
        if (text.Length <= Globals.MaxChatLength) return text;
        error = "消息太长，请缩短后再发送。"; return null;
    }
}
