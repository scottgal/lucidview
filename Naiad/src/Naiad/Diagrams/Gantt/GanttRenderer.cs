using static MermaidSharp.Rendering.RenderUtils;

namespace MermaidSharp.Diagrams.Gantt;

public class GanttRenderer : IDiagramRenderer<GanttModel>
{
    const double RowHeight = 30;
    const double TaskBarHeight = 20;
    const double SectionHeaderHeight = 25;
    const double AxisHeight = 40;
    const double LeftMargin = 150;
    const double DayWidth = 20;
    const double MilestoneSize = 12;

    public SvgDocument Render(GanttModel model, RenderOptions options)
    {
        var theme = DiagramTheme.Resolve(options);

        // Compute task dates
        var tasks = ComputeTaskDates(model);

        if (tasks.Count == 0)
        {
            // Empty chart
            var emptyBuilder = new SvgBuilder().Size(200, 100);
            emptyBuilder.AddText(100, 50, "No tasks", anchor: "middle", baseline: "middle",
                fontSize: $"{options.FontSize}px", fontFamily: options.FontFamily);
            return emptyBuilder.Build();
        }

        // Calculate date range
        var minDate = tasks.Min(t => t.ComputedStart);
        var maxDate = tasks.Max(t => t.ComputedEnd);
        var totalDays = (maxDate - minDate).Days + 1;

        // Calculate dimensions
        var totalRows = 0;
        foreach (var section in model.Sections)
        {
            if (!string.IsNullOrEmpty(section.Name))
                totalRows++; // Section header
            totalRows += section.Tasks.Count;
        }

        // Adaptive day width: cap chart to a reasonable max width
        const double MaxChartWidth = 1200;
        var dayWidth = totalDays > 0 ? Math.Min(DayWidth, MaxChartWidth / totalDays) : DayWidth;
        dayWidth = Math.Max(dayWidth, 2); // minimum visible width per day

        var chartWidth = totalDays * dayWidth;
        var chartHeight = totalRows * RowHeight + AxisHeight;

        var width = LeftMargin + chartWidth + options.Padding * 2;
        var height = chartHeight + options.Padding * 2;

        var builder = new SvgBuilder().Size(width, height);

        var offsetX = options.Padding + LeftMargin;
        var offsetY = options.Padding;

        // Draw title
        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(width / 2, offsetY + 15, model.Title,
                anchor: "middle",
                baseline: "middle",
                fontSize: $"{options.FontSize + 2}px",
                fontFamily: options.FontFamily);
            offsetY += 30;
        }

        // Draw axis
        DrawAxis(builder, minDate, totalDays, offsetX, offsetY, chartWidth, dayWidth, options, theme);
        offsetY += AxisHeight;

        // Draw grid lines
        DrawGridLines(builder, minDate, totalDays, offsetX, offsetY, chartWidth, totalRows * RowHeight, dayWidth, theme);

        // Draw sections and tasks
        var currentRow = 0;
        foreach (var section in model.Sections)
        {
            // Section header
            if (!string.IsNullOrEmpty(section.Name))
            {
                var sectionY = offsetY + currentRow * RowHeight;
                builder.AddRect(options.Padding, sectionY, LeftMargin + chartWidth, SectionHeaderHeight,
                    fill: theme.PrimaryFill, stroke: "none");
                builder.AddText(options.Padding + 10, sectionY + SectionHeaderHeight / 2, section.Name,
                    anchor: "start",
                    baseline: "middle",
                    fontSize: $"{options.FontSize}px",
                    fontFamily: options.FontFamily,
                    fontWeight: "bold");
                currentRow++;
            }

            // Tasks
            foreach (var task in section.Tasks)
            {
                DrawTask(builder, task, minDate, currentRow, offsetX, offsetY, dayWidth, options, theme);
                currentRow++;
            }
        }

        return builder.Build();
    }

    static void DrawAxis(SvgBuilder builder, DateTime startDate, int totalDays, double offsetX, double offsetY,
        double chartWidth, double dayWidth, RenderOptions options, DiagramTheme theme)
    {
        // Axis line
        builder.AddLine(offsetX, offsetY + AxisHeight - 5, offsetX + chartWidth, offsetY + AxisHeight - 5,
            stroke: theme.AxisLine, strokeWidth: 1);

        // Date labels - adaptive interval based on available space per label
        var minLabelWidth = 50.0; // minimum pixels between date labels
        var interval = Math.Max(1, (int)Math.Ceiling(minLabelWidth / dayWidth));
        if (totalDays > 60) interval = Math.Max(interval, 7);
        for (var i = 0; i < totalDays; i += interval)
        {
            var x = offsetX + i * dayWidth;
            var date = startDate.AddDays(i);

            // Tick mark
            builder.AddLine(x, offsetY + AxisHeight - 10, x, offsetY + AxisHeight - 5,
                stroke: theme.AxisLine, strokeWidth: 1);

            // Date label
            var label = date.ToString("MM/dd", CultureInfo.InvariantCulture);
            builder.AddText(x, offsetY + AxisHeight - 20, label,
                anchor: "middle",
                baseline: "middle",
                fontSize: $"{options.FontSize - 2}px",
                fontFamily: options.FontFamily,
                fill: theme.MutedText);
        }
    }

    static void DrawGridLines(SvgBuilder builder, DateTime startDate, int totalDays, double offsetX, double offsetY,
        double chartWidth, double chartHeight, double dayWidth, DiagramTheme theme)
    {
        // Vertical grid lines (weekly)
        for (var i = 0; i < totalDays; i++)
        {
            var date = startDate.AddDays(i);
            if (date.DayOfWeek == DayOfWeek.Monday || i == 0)
            {
                var x = offsetX + i * dayWidth;
                builder.AddLine(x, offsetY, x, offsetY + chartHeight,
                    stroke: theme.GridLine, strokeWidth: 1);
            }
        }

        // Horizontal grid lines
        var numRows = (int)(chartHeight / RowHeight);
        for (var i = 0; i <= numRows; i++)
        {
            var y = offsetY + i * RowHeight;
            builder.AddLine(offsetX, y, offsetX + chartWidth, y,
                stroke: theme.GridLine, strokeWidth: 1);
        }
    }

    static void DrawTask(SvgBuilder builder, GanttTask task, DateTime startDate, int row,
        double offsetX, double offsetY, double dayWidth, RenderOptions options, DiagramTheme theme)
    {
        var y = offsetY + row * RowHeight;
        var startDays = (task.ComputedStart - startDate).Days;
        var durationDays = Math.Max(1, (task.ComputedEnd - task.ComputedStart).Days);

        var taskX = offsetX + startDays * dayWidth;
        var taskWidth = durationDays * dayWidth;

        // Task name on the left
        builder.AddText(options.Padding + 10, y + RowHeight / 2, task.Name,
            anchor: "start",
            baseline: "middle",
            fontSize: $"{options.FontSize}px",
            fontFamily: options.FontFamily,
            fill: theme.TextColor);

        // Determine task color from vivid palette
        var palette = theme.VividPalette;
        var taskDoneColor = theme.MutedText;
        var taskActiveColor = palette[1 % palette.Length];
        var taskCritColor = palette[4 % palette.Length];
        var taskColor = palette[0];
        var milestoneColor = palette[3 % palette.Length];

        var color = task.Status switch
        {
            GanttTaskStatus.Done => taskDoneColor,
            GanttTaskStatus.Active => taskActiveColor,
            _ => task.IsCritical ? taskCritColor : taskColor
        };

        if (task.IsMilestone)
        {
            // Draw milestone as diamond
            var cx = taskX;
            var cy = y + RowHeight / 2;
            var path = $"M {Fmt(cx)} {Fmt(cy - MilestoneSize)} " +
                       $"L {Fmt(cx + MilestoneSize)} {Fmt(cy)} " +
                       $"L {Fmt(cx)} {Fmt(cy + MilestoneSize)} " +
                       $"L {Fmt(cx - MilestoneSize)} {Fmt(cy)} Z";
            builder.AddPath(path, fill: milestoneColor, stroke: theme.PrimaryStroke, strokeWidth: 1);
        }
        else
        {
            // Draw task bar
            var barY = y + (RowHeight - TaskBarHeight) / 2;
            builder.AddRect(taskX, barY, taskWidth, TaskBarHeight,
                rx: 3,
                fill: color,
                stroke: theme.PrimaryStroke,
                strokeWidth: 1);

            // Task ID or duration inside bar if fits
            if (task.Id != null && taskWidth > 40)
            {
                builder.AddText(taskX + taskWidth / 2, barY + TaskBarHeight / 2, task.Id,
                    anchor: "middle",
                    baseline: "middle",
                    fontSize: $"{options.FontSize - 2}px",
                    fontFamily: options.FontFamily,
                    fill: theme.IsDark ? theme.TextColor : "#fff");
            }
        }
    }

    static List<GanttTask> ComputeTaskDates(GanttModel model)
    {
        var allTasks = model.Sections.SelectMany(s => s.Tasks).ToList();
        var taskMap = allTasks.Where(t => t.Id != null).ToDictionary(t => t.Id!, t => t);

        // Default start date if none specified
        var defaultStart = DateTime.Today;

        // First pass: compute tasks without dependencies
        foreach (var task in allTasks)
        {
            if (task.StartDate.HasValue)
            {
                task.ComputedStart = task.StartDate.Value;
            }
            else if (string.IsNullOrEmpty(task.AfterTaskId))
            {
                task.ComputedStart = defaultStart;
            }

            if (task.EndDate.HasValue)
            {
                task.ComputedEnd = task.EndDate.Value;
            }
            else if (task.Duration.HasValue)
            {
                task.ComputedEnd = task.ComputedStart.Add(task.Duration.Value);
            }
            else
            {
                task.ComputedEnd = task.ComputedStart.AddDays(1);
            }
        }

        // Second pass: resolve dependencies
        var changed = true;
        var maxIterations = 100;
        while (changed && maxIterations-- > 0)
        {
            changed = false;
            foreach (var task in allTasks)
            {
                if (!string.IsNullOrEmpty(task.AfterTaskId))
                {
                    if (taskMap.TryGetValue(task.AfterTaskId, out var dependsOn))
                    {
                        var newStart = dependsOn.ComputedEnd;
                        if (newStart != task.ComputedStart)
                        {
                            task.ComputedStart = newStart;
                            if (task.Duration.HasValue)
                            {
                                task.ComputedEnd = task.ComputedStart.Add(task.Duration.Value);
                            }
                            else if (!task.EndDate.HasValue)
                            {
                                task.ComputedEnd = task.ComputedStart.AddDays(1);
                            }
                            changed = true;
                        }
                    }
                }
            }
        }

        return allTasks;
    }

}
