using System.Text.RegularExpressions;
using static MermaidSharp.Rendering.RenderUtils;

namespace MermaidSharp.Diagrams.Sequence;

public class SequenceRenderer : IDiagramRenderer<SequenceModel>
{
    const double ParticipantMinWidth = 100;
    const double ParticipantHeight = 40;
    const double ParticipantSpacing = 150;
    const double MessageSpacing = 50;
    const double ActivationWidth = 10;
    const double NoteWidth = 120;
    const double NoteHeight = 40;
    const double ActorHeadRadius = 15;
    const double ParticipantPadding = 20;

    public SvgDocument Render(SequenceModel model, RenderOptions options)
    {
        var theme = DiagramTheme.Resolve(options);
        var participantPositions = CalculateParticipantPositions(model, options);
        var (height, elementYPositions) = CalculateHeight(model, options);
        var width = CalculateWidth(model, options);

        var builder = new SvgBuilder()
            .Size(width, height)
            .AddArrowMarker("arrowhead", theme.TextColor)
            .AddArrowMarker("arrowhead-dotted", theme.TextColor)
            .AddCrossMarker("cross", theme.TextColor);

        // Add title if present
        var titleOffset = 0.0;
        if (!string.IsNullOrEmpty(model.Title))
        {
            titleOffset = 30;
            builder.AddText(width / 2, 20, model.Title,
                anchor: "middle",
                fontSize: "16px",
                fontFamily: options.FontFamily,
                fontWeight: "bold",
                fill: theme.TextColor);
        }

        var startY = options.Padding + titleOffset;

        // Draw participants (top)
        DrawParticipants(builder, model, participantPositions, startY, options, theme);

        // Draw lifelines
        var lifelineStartY = startY + ParticipantHeight;
        var lifelineEndY = height - options.Padding - ParticipantHeight;
        DrawLifelines(builder, model, participantPositions, lifelineStartY, lifelineEndY, theme);

        // Draw elements (messages, notes, activations)
        var activations = new Dictionary<string, List<(double startY, double endY)>>();
        DrawElements(builder, model, participantPositions, elementYPositions, options, activations, theme);

        // Draw activation boxes
        DrawActivations(builder, activations, participantPositions, theme);

        // Draw participants (bottom) - optional, mimics Mermaid behavior
        DrawParticipants(builder, model, participantPositions, lifelineEndY, options, theme);

        return builder.Build();
    }

    static Dictionary<string, double> CalculateParticipantPositions(SequenceModel model, RenderOptions options)
    {
        var positions = new Dictionary<string, double>();
        var widths = GetParticipantMinWidths(model, options);
        var x = options.Padding;

        foreach (var participant in model.Participants)
        {
            var w = widths[participant.Id];
            positions[participant.Id] = x + w / 2;
            x += Math.Max(w, ParticipantSpacing);
        }

        return positions;
    }

    static Dictionary<string, double> GetParticipantMinWidths(SequenceModel model, RenderOptions options)
    {
        var widths = new Dictionary<string, double>();
        foreach (var participant in model.Participants)
            widths[participant.Id] = GetParticipantMinWidth(participant.DisplayName, options.FontSize);
        return widths;
    }

    static (double height, Dictionary<int, double> elementYPositions) CalculateHeight(
        SequenceModel model, RenderOptions options)
    {
        var elementYPositions = new Dictionary<int, double>();
        var y = options.Padding + ParticipantHeight + MessageSpacing;
        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : 30;

        for (var i = 0; i < model.Elements.Count; i++)
        {
            elementYPositions[i] = y + titleOffset;
            y += GetElementHeight(model.Elements[i]);
        }

        var totalHeight = y + ParticipantHeight + options.Padding + titleOffset;
        return (totalHeight, elementYPositions);
    }

    static double GetElementHeight(SequenceElement element) =>
        element switch
        {
            Message => MessageSpacing,
            Note => NoteHeight + 10,
            Activation => 0, // Activations don't add height
            _ => MessageSpacing
        };

    static double CalculateWidth(SequenceModel model, RenderOptions options)
    {
        var positions = CalculateParticipantPositions(model, options);
        var widths = GetParticipantMinWidths(model, options);

        if (model.Participants.Count == 0)
            return options.Padding * 2 + ParticipantMinWidth;

        var lastP = model.Participants[model.Participants.Count - 1];
        var rightmostX = positions[lastP.Id];
        var rightmostW = widths[lastP.Id];
        var baseWidth = rightmostX + rightmostW / 2 + options.Padding;

        var maxRightExtent = baseWidth;

        foreach (var element in model.Elements)
        {
            if (element is Note { Position: NotePosition.RightOf } note &&
                positions.TryGetValue(note.ParticipantId, out var noteParticipantX))
            {
                var pW = widths.GetValueOrDefault(note.ParticipantId, ParticipantMinWidth);
                var noteRight = noteParticipantX + pW / 2 + 10 + NoteWidth + options.Padding;
                maxRightExtent = Math.Max(maxRightExtent, noteRight);
            }
            else if (element is Message msg && msg.FromId == msg.ToId &&
                     positions.TryGetValue(msg.FromId, out var selfX))
            {
                var selfRight = selfX + 40 + 80 + options.Padding;
                maxRightExtent = Math.Max(maxRightExtent, selfRight);
            }
        }

        return Math.Max(baseWidth, maxRightExtent);
    }

    static void DrawParticipants(SvgBuilder builder, SequenceModel model,
        Dictionary<string, double> positions, double y, RenderOptions options, DiagramTheme theme)
    {
        foreach (var participant in model.Participants)
        {
            var x = positions[participant.Id];

            if (participant.Type == ParticipantType.Actor)
            {
                DrawActor(builder, x, y, participant.DisplayName, options, theme);
            }
            else
            {
                DrawParticipantBox(builder, x, y, participant.DisplayName, options, theme);
            }
        }
    }

    static void DrawParticipantBox(SvgBuilder builder, double cx, double y,
        string text, RenderOptions options, DiagramTheme theme)
    {
        var w = GetParticipantMinWidth(text, options.FontSize);
        var cleaned = CleanHtml(text);
        var lines = cleaned.Split('\n');
        var h = lines.Length > 1 ? ParticipantHeight + (lines.Length - 1) * options.FontSize * 1.2 : ParticipantHeight;

        builder.AddRect(
            cx - w / 2, y,
            w, h,
            rx: 3,
            fill: theme.PrimaryFill,
            stroke: theme.PrimaryStroke,
            strokeWidth: 1);

        AddTextWithLineBreaks(builder, cx, y + h / 2, text, options, theme);
    }

    static void DrawActor(SvgBuilder builder, double cx, double y,
        string text, RenderOptions options, DiagramTheme theme)
    {
        // Stick figure
        var headY = y + ActorHeadRadius;
        var bodyTop = headY + ActorHeadRadius;
        var bodyBottom = bodyTop + 15;
        var armY = bodyTop + 5;
        var legBottom = y + ParticipantHeight;

        // Head
        builder.AddCircle(cx, headY, ActorHeadRadius,
            fill: theme.PrimaryFill,
            stroke: theme.PrimaryStroke,
            strokeWidth: 1);

        // Body
        builder.AddLine(cx, bodyTop, cx, bodyBottom,
            stroke: theme.PrimaryStroke,
            strokeWidth: 1);

        // Arms
        builder.AddLine(cx - 15, armY, cx + 15, armY,
            stroke: theme.PrimaryStroke,
            strokeWidth: 1);

        // Legs
        builder.AddLine(cx, bodyBottom, cx - 10, legBottom,
            stroke: theme.PrimaryStroke,
            strokeWidth: 1);
        builder.AddLine(cx, bodyBottom, cx + 10, legBottom,
            stroke: theme.PrimaryStroke,
            strokeWidth: 1);

        // Label below
        AddTextWithLineBreaks(builder, cx, y + ParticipantHeight + 15, text, options, theme,
            baseline: "top");
    }

    static void DrawLifelines(SvgBuilder builder, SequenceModel model,
        Dictionary<string, double> positions, double startY, double endY, DiagramTheme theme)
    {
        foreach (var participant in model.Participants)
        {
            var x = positions[participant.Id];
            builder.AddLine(x, startY, x, endY,
                stroke: theme.MutedText,
                strokeWidth: 1,
                strokeDasharray: "5,5");
        }
    }

    static void DrawElements(SvgBuilder builder, SequenceModel model,
        Dictionary<string, double> positions,
        Dictionary<int, double> yPositions,
        RenderOptions options,
        Dictionary<string, List<(double startY, double endY)>> activations,
        DiagramTheme theme)
    {
        var messageNumber = 0;
        var activeLifelines = new Dictionary<string, double>(); // participantId -> activation start Y

        for (var i = 0; i < model.Elements.Count; i++)
        {
            var element = model.Elements[i];
            var y = yPositions[i];

            switch (element)
            {
                case Message msg:
                    messageNumber++;
                    DrawMessage(builder, msg, positions, y, options,
                        model.AutoNumber ? messageNumber : null, theme);

                    // Handle activation on message
                    if (msg.Activate)
                    {
                        activeLifelines[msg.ToId] = y;
                    }

                    if (msg.Deactivate && activeLifelines.TryGetValue(msg.ToId, out var startY))
                    {
                        if (!activations.ContainsKey(msg.ToId))
                            activations[msg.ToId] = [];
                        activations[msg.ToId].Add((startY, y));
                        activeLifelines.Remove(msg.ToId);
                    }

                    break;

                case Note note:
                    DrawNote(builder, note, positions, y, options, theme);
                    break;

                case Activation activation:
                    if (activation.IsActivate)
                    {
                        activeLifelines[activation.ParticipantId] = y;
                    }
                    else if (activeLifelines.TryGetValue(activation.ParticipantId, out var actStartY))
                    {
                        if (!activations.ContainsKey(activation.ParticipantId))
                            activations[activation.ParticipantId] = [];
                        activations[activation.ParticipantId].Add((actStartY, y));
                        activeLifelines.Remove(activation.ParticipantId);
                    }

                    break;
            }
        }

        // Close any remaining activations
        // ReSharper disable once UseIndexFromEndExpression
        var lastY = yPositions.Count > 0 ? yPositions[yPositions.Count - 1] + MessageSpacing : 0;
        foreach (var (participantId, startY) in activeLifelines)
        {
            if (!activations.TryGetValue(participantId, out var value))
            {
                value = [];
                activations[participantId] = value;
            }

            value.Add((startY, lastY));
        }
    }

    static void DrawMessage(SvgBuilder builder, Message msg,
        Dictionary<string, double> positions, double y,
        RenderOptions options, int? number, DiagramTheme theme)
    {
        var fromX = positions[msg.FromId];
        var toX = positions[msg.ToId];
        var isSelfMessage = msg.FromId == msg.ToId;

        var isDotted = msg.Type is MessageType.Dotted or MessageType.DottedArrow
            or MessageType.DottedOpen or MessageType.DottedCross or MessageType.DottedAsync;

        var markerEnd = msg.Type switch
        {
            MessageType.SolidCross or MessageType.DottedCross => "url(#cross)",
            MessageType.SolidOpen or MessageType.DottedOpen => null,
            _ => "url(#arrowhead)"
        };

        var dashArray = isDotted ? "5,5" : null;

        if (isSelfMessage)
        {
            // Self-referencing message - draw as a loop
            var loopWidth = 40;
            var loopHeight = 30;
            var path = $"M{Fmt(fromX)},{Fmt(y)} " +
                       $"L{Fmt(fromX + loopWidth)},{Fmt(y)} " +
                       $"L{Fmt(fromX + loopWidth)},{Fmt(y + loopHeight)} " +
                       $"L{Fmt(fromX)},{Fmt(y + loopHeight)}";
            builder.AddPath(path,
                fill: "none",
                stroke: theme.TextColor,
                strokeWidth: 1,
                strokeDasharray: dashArray,
                markerEnd: markerEnd);

            // Text above
            if (!string.IsNullOrEmpty(msg.Text))
            {
                var labelText = number.HasValue ? $"{number}. {msg.Text}" : msg.Text;
                AddTextWithLineBreaks(builder, fromX + loopWidth + 5, y + loopHeight / 2,
                    labelText, options, theme, anchor: "start");
            }
        }
        else
        {
            builder.AddLine(fromX, y, toX, y,
                stroke: theme.TextColor,
                strokeWidth: 1,
                strokeDasharray: dashArray);

            // Draw arrowhead manually since line doesn't support marker
            DrawArrowhead(builder, fromX, toX, y, msg.Type, theme);

            // Text above the line
            if (!string.IsNullOrEmpty(msg.Text) || number.HasValue)
            {
                var labelText = number.HasValue && !string.IsNullOrEmpty(msg.Text)
                    ? $"{number}. {msg.Text}"
                    : number.HasValue
                        ? $"{number}."
                        : msg.Text!;

                var midX = (fromX + toX) / 2;
                AddTextWithLineBreaks(builder, midX, y - 8, labelText, options, theme,
                    baseline: "bottom");
            }
        }
    }

    static void DrawArrowhead(SvgBuilder builder, double fromX, double toX, double y, MessageType type, DiagramTheme theme)
    {
        var direction = Math.Sign(toX - fromX);
        var arrowSize = 8;

        switch (type)
        {
            case MessageType.SolidArrow:
            case MessageType.DottedArrow:
            case MessageType.Solid:
            case MessageType.Dotted:
            case MessageType.SolidAsync:
            case MessageType.DottedAsync:
                // Filled arrowhead
                var tipX = toX;
                var backX = toX - direction * arrowSize;
                builder.AddPolygon([
                    new(tipX, y),
                    new(backX, y - arrowSize / 2),
                    new(backX, y + arrowSize / 2)
                ], fill: theme.TextColor);
                break;

            case MessageType.SolidOpen:
            case MessageType.DottedOpen:
                // Open arrowhead (just lines)
                builder.AddLine(toX - direction * arrowSize, y - arrowSize / 2, toX, y,
                    stroke: theme.TextColor, strokeWidth: 1);
                builder.AddLine(toX - direction * arrowSize, y + arrowSize / 2, toX, y,
                    stroke: theme.TextColor, strokeWidth: 1);
                break;

            case MessageType.SolidCross:
            case MessageType.DottedCross:
                // X mark
                builder.AddLine(toX - arrowSize / 2, y - arrowSize / 2, toX + arrowSize / 2, y + arrowSize / 2,
                    stroke: theme.TextColor, strokeWidth: 2);
                builder.AddLine(toX - arrowSize / 2, y + arrowSize / 2, toX + arrowSize / 2, y - arrowSize / 2,
                    stroke: theme.TextColor, strokeWidth: 2);
                break;
        }
    }

    static void DrawNote(SvgBuilder builder, Note note,
        Dictionary<string, double> positions, double y, RenderOptions options, DiagramTheme theme)
    {
        var participantX = positions[note.ParticipantId];
        double noteX;

        switch (note.Position)
        {
            case NotePosition.RightOf:
                noteX = participantX + ParticipantMinWidth / 2 + 10;
                break;
            case NotePosition.LeftOf:
                noteX = participantX - ParticipantMinWidth / 2 - NoteWidth - 10;
                break;
            case NotePosition.Over:
            default:
                if (!string.IsNullOrEmpty(note.OverParticipantId2) &&
                    positions.TryGetValue(note.OverParticipantId2, out var participant2X))
                {
                    noteX = (participantX + participant2X) / 2 - NoteWidth / 2;
                }
                else
                {
                    noteX = participantX - NoteWidth / 2;
                }

                break;
        }

        // Note box (folded corner style)
        var foldSize = 8;
        var path = $"M{Fmt(noteX)},{Fmt(y)} " +
                   $"L{Fmt(noteX + NoteWidth - foldSize)},{Fmt(y)} " +
                   $"L{Fmt(noteX + NoteWidth)},{Fmt(y + foldSize)} " +
                   $"L{Fmt(noteX + NoteWidth)},{Fmt(y + NoteHeight)} " +
                   $"L{Fmt(noteX)},{Fmt(y + NoteHeight)} Z";

        builder.AddPath(path, fill: theme.SecondaryFill, stroke: theme.SecondaryStroke, strokeWidth: 1);

        // Fold line
        builder.AddLine(noteX + NoteWidth - foldSize, y,
            noteX + NoteWidth - foldSize, y + foldSize,
            stroke: theme.SecondaryStroke, strokeWidth: 1);
        builder.AddLine(noteX + NoteWidth - foldSize, y + foldSize,
            noteX + NoteWidth, y + foldSize,
            stroke: theme.SecondaryStroke, strokeWidth: 1);

        // Note text
        AddTextWithLineBreaks(builder, noteX + NoteWidth / 2, y + NoteHeight / 2,
            note.Text, options, theme);
    }

    static void DrawActivations(SvgBuilder builder,
        Dictionary<string, List<(double startY, double endY)>> activations,
        Dictionary<string, double> positions, DiagramTheme theme)
    {
        foreach (var (participantId, ranges) in activations)
        {
            var x = positions[participantId];
            foreach (var (startY, endY) in ranges)
            {
                builder.AddRect(
                    x - ActivationWidth / 2, startY,
                    ActivationWidth, endY - startY,
                    fill: theme.LabelBackground,
                    stroke: theme.MutedText,
                    strokeWidth: 1);
            }
        }
    }

    /// <summary>
    /// Convert HTML line breaks to newlines and strip remaining HTML tags.
    /// </summary>
    static string CleanHtml(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", "");
        return text.Replace("&quot;", "\"")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&apos;", "'")
            .Replace("&#39;", "'")
            .Trim();
    }

    /// <summary>
    /// Calculate the width needed for a participant box based on its display name.
    /// </summary>
    static double GetParticipantMinWidth(string displayName, double fontSize)
    {
        var cleaned = CleanHtml(displayName);
        var lines = cleaned.Split('\n');
        var maxWidth = 0.0;
        foreach (var line in lines)
            maxWidth = Math.Max(maxWidth, MeasureTextWidth(line, fontSize));
        return Math.Max(ParticipantMinWidth, maxWidth + ParticipantPadding * 2);
    }

    /// <summary>
    /// Add text to SVG, handling multi-line text with tspan elements when the text contains newlines.
    /// </summary>
    static void AddTextWithLineBreaks(SvgBuilder builder, double x, double y, string text,
        RenderOptions options, DiagramTheme theme, string anchor = "middle", string baseline = "middle",
        string? fontWeight = null)
    {
        var cleaned = CleanHtml(text);
        var lines = cleaned.Split('\n');
        if (lines.Length <= 1)
        {
            builder.AddText(x, y, cleaned,
                anchor: anchor, baseline: baseline,
                fontSize: $"{options.FontSize}px", fontFamily: options.FontFamily,
                fontWeight: fontWeight, fill: theme.TextColor);
        }
        else
        {
            // Center the multi-line block vertically around y
            var lineHeight = options.FontSize * 1.2;
            var totalHeight = lines.Length * lineHeight;
            var startY = y - totalHeight / 2 + lineHeight / 2;
            builder.AddMultiLineText(x, startY, lineHeight, lines, anchor: anchor, fill: theme.TextColor);
        }
    }

}
