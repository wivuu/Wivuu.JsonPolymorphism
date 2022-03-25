using System;
using System.Text.Json.Serialization;

namespace TestNSwag.Models;

public enum PostType
{
    Comment,
    Activity,
    Reply,
}

public abstract partial record Post([JsonDiscriminator("type")] PostType Type)
{
    public Guid Id { get; internal set; } = Guid.NewGuid();
}

public record CommentPost() : Post(PostType.Comment)
{
    public string Comment { get; set; } = null!;
}

public record ActivityPost() : Post(PostType.Activity)
{
    public string Activity { get; set; } = null!;
}

public record ReplyPost() : Post(PostType.Reply)
{
    public string Reply { get; set; } = null!;
}