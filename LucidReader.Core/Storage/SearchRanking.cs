namespace LucidReader.Core.Storage;

/// <summary>
/// Column weights for the bm25() ordering SearchRepository uses, kept here
/// rather than inline in the SQL so the intent is stated once and the
/// ordering they produce can be asserted directly.
///
/// Plain "ORDER BY rank" weights every column equally, so an article that
/// happens to mention a word once in paragraph nine ranked level with one
/// whose headline is that word. These weights say what the columns are worth
/// relative to each other:
///
///   title    a headline match is what the user almost always meant
///   summary  the publisher's own precis of the article, so a match there is
///            about the piece as a whole, not an aside inside it
///   author   narrow and high signal: nobody types a name by accident, but
///            the field is one or two words so it must not swamp a title
///   body     the long tail, and the only one that can match incidentally
///
/// bm25() returns a smaller (more negative) number for a better match, and
/// the weights multiply each column's contribution, so ORDER BY is ascending
/// and a bigger weight means "counts for more".
///
/// The order of these four values is the column order of items_fts as
/// created in Migrations.V5. Changing that column order without changing
/// this list would silently rank titles by the body's weight.
///
/// The numbers are not arbitrary but they are not precise either: they were
/// set against a 4000-item corpus of generated prose with one planted term
/// in a title, one in a summary and one buried in a body, and raised until
/// the three came back in that order. bm25 divides by document length, so a
/// short summary-only item scores well on its own merits; the title weight
/// has to be high enough to beat that, which is why it is 20 and not the 12
/// that ordered a title above a body but not above a short summary.
/// </summary>
public static class SearchRanking
{
    public const double TitleWeight = 20.0;
    public const double AuthorWeight = 4.0;
    public const double SummaryWeight = 6.0;
    public const double ContentWeight = 1.0;

    /// <summary>
    /// The bm25 call used in the ORDER BY clause, weights included, formatted
    /// invariantly so a comma decimal separator from the host locale cannot
    /// turn "20.0, 4.0" into "20,0, 4,0" and change the argument count.
    /// </summary>
    public static string OrderByExpression { get; } = string.Format(
        System.Globalization.CultureInfo.InvariantCulture,
        "bm25(items_fts, {0:0.0}, {1:0.0}, {2:0.0}, {3:0.0})",
        TitleWeight, AuthorWeight, SummaryWeight, ContentWeight);
}
