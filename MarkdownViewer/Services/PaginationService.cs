namespace MarkdownViewer.Services;

public class PaginationService
{
    // Standard page dimensions at 96 DPI
    public const double PageWidthA4 = 794; // 210mm at 96 DPI
    public const double PageHeightA4 = 1123; // 297mm at 96 DPI
    public const double PageWidthLetter = 816; // 8.5" at 96 DPI
    public const double PageHeightLetter = 1056; // 11" at 96 DPI

    // Default margins
    public const double MarginTop = 48;
    public const double MarginBottom = 48;
    public const double MarginLeft = 48;
    public const double MarginRight = 48;

    public double ContentHeight { get; private set; }
    public double PageHeight { get; private set; } = PageHeightLetter;
    public double UsablePageHeight => PageHeight - MarginTop - MarginBottom;
    public int TotalPages { get; private set; } = 1;
    public int CurrentPage { get; private set; } = 1;

    public void CalculatePages(double contentHeight)
    {
        ContentHeight = contentHeight;

        if (contentHeight <= 0)
        {
            TotalPages = 1;
            return;
        }

        TotalPages = Math.Max(1, (int)Math.Ceiling(contentHeight / UsablePageHeight));
    }

    public void SetPageSize(PageSize size)
    {
        PageHeight = size switch
        {
            PageSize.A4 => PageHeightA4,
            PageSize.Letter => PageHeightLetter,
            _ => PageHeightLetter
        };
    }

    public bool GoToPage(int page)
    {
        if (page < 1 || page > TotalPages)
            return false;

        CurrentPage = page;
        return true;
    }

    public bool NextPage()
    {
        if (CurrentPage >= TotalPages)
            return false;

        CurrentPage++;
        return true;
    }

    public bool PreviousPage()
    {
        if (CurrentPage <= 1)
            return false;

        CurrentPage--;
        return true;
    }

    public double GetScrollOffsetForPage(int page)
    {
        return (page - 1) * UsablePageHeight;
    }

    public int GetPageForScrollOffset(double offset)
    {
        if (offset <= 0) return 1;
        return Math.Min(TotalPages, (int)(offset / UsablePageHeight) + 1);
    }
}

public enum PageSize
{
    Letter,
    A4
}