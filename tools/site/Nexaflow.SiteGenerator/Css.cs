namespace Nexaflow.SiteGenerator;

/// <summary>
/// The site's one stylesheet. Dark first — Nexaflow is a developer's tool and the help figures are rendered in
/// the dark palette — with a light scheme for readers whose system asks for one.
/// </summary>
internal static class Css
{
    public const string Text = """
        :root {
          --bg: #0e1116;
          --surface: #161b22;
          --surface-2: #1c232c;
          --line: #2a323d;
          --text: #d7dee7;
          --muted: #8b97a6;
          --accent: #5db1ff;
          --accent-soft: #143050;
          --radius: 10px;
          --measure: 74ch;
          color-scheme: dark;
        }

        @media (prefers-color-scheme: light) {
          :root {
            --bg: #ffffff;
            --surface: #f6f8fa;
            --surface-2: #eef2f6;
            --line: #d7dee6;
            --text: #1d2530;
            --muted: #5a6675;
            --accent: #0a66c2;
            --accent-soft: #e4effb;
            color-scheme: light;
          }
        }

        * { box-sizing: border-box; }

        body {
          margin: 0;
          background: var(--bg);
          color: var(--text);
          font: 16px/1.65 ui-sans-serif, system-ui, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
          -webkit-text-size-adjust: 100%;
        }

        a { color: var(--accent); text-decoration-thickness: 1px; text-underline-offset: 2px; }
        a:hover { text-decoration-thickness: 2px; }

        header.site, footer.site {
          display: flex; flex-wrap: wrap; gap: 16px; align-items: center;
          padding: 14px 24px; border-bottom: 1px solid var(--line); background: var(--surface);
        }
        header.site nav { margin-left: auto; display: flex; gap: 20px; flex-wrap: wrap; }
        header.site nav a, .wordmark { text-decoration: none; }
        .wordmark { font-weight: 650; letter-spacing: -0.01em; font-size: 1.05rem; color: var(--text); }
        header.site nav a { color: var(--muted); }
        header.site nav a:hover { color: var(--text); }

        footer.site {
          border-bottom: 0; border-top: 1px solid var(--line); margin-top: 72px;
          display: block; color: var(--muted); font-size: 0.9rem;
        }
        footer.site p { margin: 4px 0; }

        main { max-width: 1040px; margin: 0 auto; padding: 32px 24px 0; }

        .crumbs { color: var(--muted); font-size: 0.9rem; margin-bottom: 18px; }
        .crumbs span { margin: 0 6px; opacity: 0.6; }

        .page-head { margin-bottom: 28px; }
        .page-head h1 { font-size: 2.1rem; line-height: 1.2; margin: 0 0 10px; letter-spacing: -0.02em; }
        .lede { color: var(--muted); font-size: 1.1rem; margin: 0; max-width: var(--measure); }

        /* The app puts a Topics list at the top of every help page; the site keeps the habit. */
        .topics {
          background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius);
          padding: 14px 18px; margin-bottom: 32px;
        }
        .topics h2 {
          font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.09em;
          color: var(--muted); margin: 0 0 8px; font-weight: 600;
        }
        .topics ul { list-style: none; margin: 0; padding: 0; display: flex; flex-wrap: wrap; gap: 6px 18px; }

        .prose { max-width: var(--measure); }
        .prose h2 {
          font-size: 1.4rem; margin: 44px 0 12px; padding-top: 14px;
          border-top: 1px solid var(--line); letter-spacing: -0.01em;
        }
        .prose h3 { font-size: 1.1rem; margin: 28px 0 8px; }
        .prose :is(h2, h3) a { text-decoration: none; }
        .prose p, .prose li { overflow-wrap: break-word; }
        .prose ul, .prose ol { padding-left: 22px; }
        .prose li { margin: 5px 0; }
        .prose hr { display: none; }   /* the pages rule off between sections; the h2 border already does */

        .prose img {
          display: block; max-width: 100%; height: auto; margin: 18px 0;
          border: 1px solid var(--line); border-radius: var(--radius); background: var(--surface);
        }

        .prose :not(pre) > code {
          background: var(--surface-2); border: 1px solid var(--line); border-radius: 5px;
          padding: 0.1em 0.35em; font-size: 0.88em;
        }
        .prose pre {
          background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius);
          padding: 14px 16px; overflow-x: auto; font-size: 0.87rem; line-height: 1.5;
        }
        .prose pre code { background: none; border: 0; padding: 0; }
        code, pre { font-family: ui-monospace, "Cascadia Mono", Consolas, "SF Mono", Menlo, monospace; }

        .prose blockquote {
          margin: 18px 0; padding: 2px 16px; border-left: 3px solid var(--line); color: var(--muted);
        }

        .prose table { border-collapse: collapse; width: 100%; margin: 18px 0; font-size: 0.94rem; display: block; overflow-x: auto; }
        .prose th, .prose td { border: 1px solid var(--line); padding: 7px 11px; text-align: left; }
        .prose th { background: var(--surface); }

        /* A "show me" link drives the real app; here it only says what it would have pointed at. */
        .locate {
          color: var(--muted); border-bottom: 1px dotted var(--line); cursor: help; white-space: nowrap;
        }
        .locate::before { content: "\1F4CD"; margin-right: 3px; font-size: 0.85em; }

        .hero { padding: 40px 0 16px; max-width: 46rem; }
        .hero h1 { font-size: 3rem; margin: 0 0 12px; letter-spacing: -0.03em; }
        .tagline { font-size: 1.35rem; line-height: 1.4; margin: 0 0 18px; }
        .hero-body { color: var(--muted); font-size: 1.05rem; margin: 0 0 26px; }
        .actions { display: flex; flex-wrap: wrap; gap: 12px; margin: 0 0 14px; }

        .button {
          display: inline-block; padding: 11px 20px; border-radius: var(--radius); text-decoration: none;
          background: var(--accent); color: var(--bg); font-weight: 600; border: 1px solid var(--accent);
        }
        .button.ghost { background: transparent; color: var(--accent); }
        .button:hover { filter: brightness(1.08); }

        .small { color: var(--muted); font-size: 0.88rem; }

        .group { margin: 48px 0; }
        .group h2 {
          font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.1em; color: var(--muted);
          margin: 0 0 14px; font-weight: 600;
        }

        .cards { display: grid; gap: 12px; grid-template-columns: repeat(auto-fill, minmax(258px, 1fr)); }
        .card {
          display: block; padding: 15px 17px; background: var(--surface); border: 1px solid var(--line);
          border-radius: var(--radius); text-decoration: none; color: inherit;
        }
        .card:hover { border-color: var(--accent); background: var(--surface-2); }
        .card h3 { margin: 0 0 5px; font-size: 1rem; color: var(--accent); }
        .card p { margin: 0; color: var(--muted); font-size: 0.89rem; line-height: 1.5; }

        /* The front page's proof that the drawing is real: the app's own figures, at their own size. */
        .shot { margin: 40px 0 8px; }
        .shot img {
          display: block; width: 100%; height: auto; border: 1px solid var(--line);
          border-radius: var(--radius); background: var(--surface);
        }
        .shot .small { margin: 10px 2px 0; }

        .gallery { display: grid; gap: 22px; grid-template-columns: repeat(auto-fit, minmax(330px, 1fr)); }
        .gallery figure { margin: 0; }
        .gallery img {
          display: block; width: 100%; height: auto; border: 1px solid var(--line); border-radius: var(--radius);
        }
        .gallery figcaption { margin-top: 8px; color: var(--muted); font-size: 0.9rem; }
        .gallery figcaption b { color: var(--text); }

        .unreleased {
          margin: 0 0 28px; padding: 10px 14px; border-radius: var(--radius);
          border: 1px solid #c98a2b; color: var(--text); background: rgba(201, 138, 43, 0.10);
          font-size: 0.92rem; line-height: 1.5;
        }

        .strip { margin: 56px 0; }
        .strip h2 { font-size: 1.25rem; margin: 0 0 6px; letter-spacing: -0.01em; }
        .strip .small { margin: 0 0 18px; max-width: 58ch; }
        .figures { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(230px, 1fr)); }
        .figures img {
          width: 100%; height: 165px; object-fit: contain; padding: 10px;
          border: 1px solid var(--line); border-radius: var(--radius); background: #0e1116;
        }

        .badges { display: flex; flex-wrap: wrap; gap: 6px; margin: 10px 0 0; }
        .badge {
          font-size: 0.72rem; font-weight: 600; letter-spacing: 0.02em; padding: 2px 8px;
          border-radius: 999px; border: 1px solid var(--line); color: var(--muted); background: var(--bg);
        }
        .badge code { background: none; border: 0; padding: 0; font-size: 1em; }
        .badge.ai { color: var(--accent); border-color: var(--accent); background: var(--accent-soft); }
        .badge.soon { color: #c98a2b; border-color: #c98a2b; background: transparent; }

        @media (max-width: 640px) {
          main { padding: 24px 16px 0; }
          header.site, footer.site { padding: 12px 16px; }
          .hero h1 { font-size: 2.2rem; }
          .tagline { font-size: 1.15rem; }
        }
        """;
}
