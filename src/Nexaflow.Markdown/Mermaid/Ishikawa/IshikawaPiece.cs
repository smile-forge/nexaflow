namespace Nexaflow.Markdown.Mermaid.Ishikawa;

/// <summary>The pieces an ishikawa diagram's layout is made of — its layers, and what is in them.</summary>
public static class IshikawaPiece
{
    /// <summary>The fish's head, standing for the event, with what it says in it.</summary>
    public const string Head = "Head";

    /// <summary>The causes of the event, each in its box at the end of its bone.</summary>
    public const string Causes = "Causes";
    public const string Cause = "Cause";

    /// <summary>What every cause further in says, at the end of its bone.</summary>
    public const string Words = "Words";
    public const string Label = "Label";

    /// <summary>The spine, standing for the event, and a bone — standing for its cause — for every cause.</summary>
    public const string Bones = "Bones";
    public const string Spine = "Spine";
    public const string Bone = "Bone";
}
