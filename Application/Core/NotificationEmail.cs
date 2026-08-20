using System.Globalization;
using System.Net;
using System.Text;

namespace Application.Core;

/// <summary>
/// The two renderings of one notification email: the HTML body and the plain-text
/// alternative that ships alongside it in the same message.
/// </summary>
public sealed record NotificationEmailBody(string Html, string Text);

/// <summary>
/// Builds the notification emails this application sends — a greeting, a sentence
/// or two about what happened, optional labelled details, and a closing line.
///
/// It exists because the HTML rendering has to encode every value that came from a
/// person, and the hand-written bodies did not agree on that. One handler encoded
/// six fields; the next interpolated a free-text Reason and two display names
/// straight into the markup, so a reason containing a tag reached a manager's inbox
/// as markup rather than as text.
///
/// The shape of this API is what stops that returning. A value can only reach a
/// sentence as an interpolation argument: the method takes a
/// <see cref="FormattableString"/>, so it receives the literal text and the values
/// separately and encodes the values itself. Nothing here accepts a pre-built
/// fragment, so "forgot to encode this one" is not something a caller can express.
/// </summary>
public sealed class NotificationEmail
{
    private readonly string _recipientName;
    private readonly List<Paragraph> _paragraphs = [];
    private Paragraph? _closing;

    private NotificationEmail(string? recipientName) =>
        _recipientName = recipientName ?? string.Empty;

    /// <summary>Starts a message addressed to <paramref name="recipientName"/>.</summary>
    public static NotificationEmail To(string? recipientName) => new(recipientName);

    /// <summary>
    /// Marks an interpolated value as ordinary prose rather than one of the
    /// message's headline facts — it is still encoded, just not emphasised.
    /// </summary>
    public static object Plain(string? value) => new Unemphasised(value);

    /// <summary>
    /// A sentence about what happened. Interpolated values are encoded and
    /// rendered bold; wrap one in <see cref="Plain"/> to leave it unemphasised.
    /// </summary>
    public NotificationEmail Sentence(FormattableString sentence)
    {
        var arguments = sentence.GetArguments();

        // The literal halves of the interpolation are written here in the source,
        // so they are the only markup that reaches the output. Every argument goes
        // through Encode on the way in.
        _paragraphs.Add(new Paragraph(
            string.Format(CultureInfo.InvariantCulture, sentence.Format, [.. arguments.Select(AsHtml)]),
            string.Format(CultureInfo.InvariantCulture, sentence.Format, [.. arguments.Select(AsText)])));

        return this;
    }

    /// <summary>
    /// A labelled detail, such as the requester's reason. Skipped entirely when the
    /// value is blank, so a message never carries an empty "Reason:" line.
    /// </summary>
    public NotificationEmail Detail(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return this;
        }

        _paragraphs.Add(new Paragraph(
            $"<strong>{Encode(label)}:</strong> {Encode(value)}",
            $"{label}: {value}"));

        return this;
    }

    /// <summary>
    /// The closing line — "please log in and take a look". Literal text, set apart
    /// from the details above it; there is nothing here for a caller to encode.
    /// </summary>
    public NotificationEmail Closing(string sentence)
    {
        _closing = new Paragraph(sentence, sentence);
        return this;
    }

    public NotificationEmailBody Build()
    {
        var greeting = new Paragraph($"Hello {Encode(_recipientName)},", $"Hello {_recipientName},");

        var html = new StringBuilder();
        var text = new StringBuilder();

        html.Append(CultureInfo.InvariantCulture, $"<p>{greeting.Html}</p>\n");
        text.Append(CultureInfo.InvariantCulture, $"{greeting.Text}\n\n");

        foreach (var paragraph in _paragraphs)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p>{paragraph.Html}</p>\n");
            text.Append(CultureInfo.InvariantCulture, $"{paragraph.Text}\n");
        }

        if (_closing is not null)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p>{_closing.Html}</p>\n");
            text.Append(CultureInfo.InvariantCulture, $"\n{_closing.Text}\n");
        }

        return new NotificationEmailBody(html.ToString(), text.ToString());
    }

    private static string AsHtml(object? argument) => argument switch
    {
        Unemphasised plain => Encode(plain.Value),
        _ => $"<strong>{Encode(Format(argument))}</strong>",
    };

    private static string AsText(object? argument) => argument switch
    {
        Unemphasised plain => plain.Value ?? string.Empty,
        _ => Format(argument),
    };

    private static string Format(object? argument) =>
        argument is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : argument?.ToString() ?? string.Empty;

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private sealed record Paragraph(string Html, string Text);

    private sealed record Unemphasised(string? Value);
}
