using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>One formula, as the corpus drew it and as we draw it.</summary>
/// <param name="Flagged">Whether the two are far enough apart to be worth a look.</param>
internal sealed record Pair(
    CorpusEntry Entry,
    string ReferenceImage,
    string? OurImage,
    string? Error,
    double Overlap,
    bool Flagged);

/// <summary>
/// The side-by-side pages: every pair in the corpus, ours next to theirs, split across pages a
/// browser can actually open. A quarter of a million rows in one file lays out for a minute and
/// then scrolls like treacle, so the set is paginated and each page loads its images as they come
/// into view.
/// </summary>
internal static class LatexPictureReport
{
    public const int DefaultPageSize = 400;

    private const string Style = """
        <style>
          :root { color-scheme: light dark; --line: #8884; --flag: #d33; --dim: #8888; }
          body { font: 14px/1.5 system-ui, sans-serif; margin: 0 1.5rem 3rem; }
          h1 { font-size: 1.2rem; margin: 1.2rem 0 .2rem; }
          p.note { margin: 0 0 1rem; color: var(--dim); max-width: 60rem; }
          .bar { position: sticky; top: 0; z-index: 2; background: Canvas; padding: .6rem 0;
                 border-bottom: 1px solid var(--line); display: flex; gap: .6rem; align-items: center;
                 flex-wrap: wrap; }
          .bar .grow { flex: 1; }
          a.page, button { font: inherit; padding: .25rem .6rem; border: 1px solid var(--line);
                           border-radius: 4px; background: Canvas; color: inherit;
                           text-decoration: none; cursor: pointer; }
          a.page[aria-disabled="true"] { opacity: .4; pointer-events: none; }
          table { border-collapse: collapse; width: 100%; }
          td, th { border-bottom: 1px solid var(--line); padding: .5rem .6rem; vertical-align: middle; }
          th { text-align: left; font-weight: 600; color: var(--dim); font-size: 12px;
               text-transform: uppercase; letter-spacing: .04em; }
          td.pick { width: 2rem; }
          td.img { width: 42%; }
          img { max-height: 46px; width: auto; max-width: 100%; background: #fff; padding: 3px;
                border-radius: 3px; vertical-align: middle; }
          .flag { color: var(--flag); font-weight: 700; }
          code { font: 11px/1.4 ui-monospace, monospace; white-space: pre-wrap; word-break: break-word;
                 display: block; color: var(--dim); }
          tr.picked { background: color-mix(in srgb, var(--flag) 12%, transparent); }
          .err { color: var(--flag); font-size: 12px; }
          textarea { width: 100%; height: 10rem; font: 12px ui-monospace, monospace; }
          .index { columns: 5; column-gap: 2rem; margin-top: 1rem; }
          .index a { display: block; padding: .1rem 0; }
        </style>
        """;

    private const string Script = """
        <script>
          // A pick is kept as its formula, not just its id: the point of picking one is to paste it
          // somewhere and have it mean something. The set survives moving between pages.
          const key = 'latex-corpus-picked';
          const store = new Map(JSON.parse(localStorage.getItem(key) || '[]'));
          const rows = () => [...document.querySelectorAll('tbody tr')];
          const text = () => [...store.values()].map(f => '- `' + f + '`').join('\n');
          function paint(r) {
            const on = store.has(r.dataset.id);
            r.querySelector('input').checked = on;
            r.classList.toggle('picked', on);
          }
          function save() {
            localStorage.setItem(key, JSON.stringify([...store]));
            for (const el of document.querySelectorAll('.count'))
              el.textContent = store.size + ' picked';
            const box = document.getElementById('picked');
            if (box) box.value = text();
          }
          document.addEventListener('change', e => {
            const r = e.target.closest('tr');
            if (!r) return;
            if (e.target.checked) store.set(r.dataset.id, r.dataset.formula);
            else store.delete(r.dataset.id);
            paint(r); save();
          });
          document.getElementById('copy')?.addEventListener('click', async () => {
            const b = document.getElementById('copy');
            try { await navigator.clipboard.writeText(text()); b.textContent = 'Copied'; }
            catch { document.getElementById('picked')?.select(); }
            setTimeout(() => b.textContent = 'Copy picked', 1200);
          });
          document.getElementById('clear')?.addEventListener('click', () => {
            store.clear(); rows().forEach(paint); save();
          });
          rows().forEach(paint); save();
        </script>
        """;

    /// <summary>Writes the index and every page into <paramref name="folder"/>.</summary>
    /// <returns>The path of the index.</returns>
    public static string Write(string folder, IReadOnlyList<Pair> pairs, int pageSize)
    {
        Directory.CreateDirectory(folder);
        var pageCount = Math.Max(1, (pairs.Count + pageSize - 1) / pageSize);

        for (var page = 0; page < pageCount; page++)
        {
            var rows = pairs.Skip(page * pageSize).Take(pageSize).ToList();
            File.WriteAllText(
                Path.Combine(folder, PageName(page)),
                Page(rows, page, pageCount, pairs.Count),
                new UTF8Encoding(false));
        }

        var index = Path.Combine(folder, "index.html");
        File.WriteAllText(index, Index(pairs, pageCount, pageSize), new UTF8Encoding(false));
        return index;
    }

    private static string PageName(int page) => $"page-{page + 1:D4}.html";

    private static string Page(IReadOnlyList<Pair> rows, int page, int pageCount, int total)
    {
        var html = new StringBuilder();
        var first = (page * DefaultPageSize) + 1;
        html.AppendLine("<!doctype html><meta charset=\"utf-8\">");
        html.AppendLine($"<title>Corpus {first}… — page {page + 1} of {pageCount}</title>");
        html.AppendLine(Style);
        html.Append($"""
            <div class="bar">
              {Link("index.html", "Index", false)}
              {Link(PageName(page - 1), "‹ Previous", page == 0)}
              <span>Page {page + 1:N0} of {pageCount:N0}</span>
              {Link(PageName(page + 1), "Next ›", page >= pageCount - 1)}
              <span class="grow"></span>
              <span class="count">0 picked</span>
              <button id="copy" type="button">Copy picked</button>
            </div>
            <table>
              <thead><tr><th></th><th>The corpus</th><th>Nexaflow</th><th>Formula</th></tr></thead>
              <tbody>

            """);

        foreach (var row in rows)
        {
            var flag = row.Error is not null
                ? """<span class="flag" title="did not render">✕</span>"""
                : row.Flagged
                    ? $"""<span class="flag" title="overlap {row.Overlap:F2}">▲</span>"""
                    : "";
            html.Append($"""
                    <tr data-id="{row.Entry.Id}" data-formula="{Escape(row.Entry.Formula)}">
                      <td class="pick"><input type="checkbox">{flag}</td>
                      <td class="img"><img loading="lazy" src="{row.ReferenceImage}" alt=""></td>
                      <td class="img">{Ours(row)}</td>
                      <td><code>{Escape(row.Entry.Formula)}</code></td>
                    </tr>

                """);
        }

        html.AppendLine("  </tbody>");
        html.AppendLine("</table>");
        html.Append($"""
            <div class="bar">
              {Link(PageName(page - 1), "‹ Previous", page == 0)}
              {Link(PageName(page + 1), "Next ›", page >= pageCount - 1)}
              <span class="grow"></span>
              <span>{total:N0} formulas in all</span>
            </div>

            """);
        html.AppendLine(Script);
        return html.ToString();
    }

    private static string Ours(Pair row) =>
        row.OurImage is { } file
            ? $"""<img loading="lazy" src="{file}" alt="">"""
            : $"""<div class="err">{Escape(row.Error ?? "no rendering")}</div>""";

    private static string Link(string href, string text, bool disabled) =>
        disabled
            ? $"""<a class="page" aria-disabled="true">{text}</a>"""
            : $"""<a class="page" href="{href}">{text}</a>""";

    private static string Index(IReadOnlyList<Pair> pairs, int pageCount, int pageSize)
    {
        var flagged = pairs.Count(p => p.Flagged || p.Error is not null);
        var html = new StringBuilder();
        html.AppendLine("<!doctype html><meta charset=\"utf-8\">");
        html.AppendLine("<title>LaTeX corpus, side by side</title>");
        html.AppendLine(Style);
        html.Append($"""
            <h1>LaTeX corpus, side by side</h1>
            <p class="note">
              {pairs.Count:N0} formulas: the corpus rendering on the left, ours on the right, most
              different first. {flagged:N0} carry a ▲ or ✕ - the rest are already close. Tick anything
              that looks wrong; the ticks are kept across pages, and <em>Copy picked</em> puts the list
              on the clipboard.
            </p>
            <div class="bar">
              <span class="count">0 picked</span>
              <button id="copy" type="button">Copy picked</button>
              <button id="clear" type="button">Clear</button>
            </div>
            <p class="note">
              Ticks are kept as you move between pages. <em>Copy picked</em> puts the formulas on the
              clipboard as a list, ready to paste back.
            </p>
            <textarea id="picked" readonly></textarea>
            <h1>Pages</h1>
            <div class="index">

            """);

        for (var page = 0; page < pageCount; page++)
        {
            var from = (page * pageSize) + 1;
            var to = Math.Min((page + 1) * pageSize, pairs.Count);
            html.AppendLine($"""  <a href="{PageName(page)}">{from:N0} – {to:N0}</a>""");
        }

        html.AppendLine("</div>");
        html.AppendLine(Script);
        return html.ToString();
    }

    private static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
