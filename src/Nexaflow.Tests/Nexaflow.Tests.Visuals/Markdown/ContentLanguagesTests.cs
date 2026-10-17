using System;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The one table of languages, asked from every thread that lays content out while languages are still being added to it.
/// </summary>
[TestClass]
[CoversNode("content-languages-lookup")]
public class ContentLanguagesTests
{
    /// <summary>
    /// A language's own test of whether it reads a word is code from wherever the language came from, and it can wait on
    /// anything: a type initialiser registering languages of its own is the case that hung whole runs. So a language can be
    /// added while another is still deciding about a word, rather than each waiting on the other for ever.
    /// </summary>
    [TestMethod]
    public void ALanguageCanBeAddedWhileAnotherIsStillDecidingWhetherItReadsAWord()
    {
        var asked = Guid.NewGuid().ToString("N");
        using var deciding = new ManualResetEventSlim();
        using var added = new ManualResetEventSlim();

        ContentLanguages.Register(Language(word =>
        {
            if (word != asked) return false;

            deciding.Set();
            return added.Wait(TimeSpan.FromSeconds(10));
        }));

        var lookup = Task.Run(() => ContentLanguages.For(asked));
        Assert.IsTrue(deciding.Wait(TimeSpan.FromSeconds(10)), "the table never asked the language about the word");

        var adding = Task.Run(() => ContentLanguages.Register(Language(static _ => false)));
        Assert.IsTrue(adding.Wait(TimeSpan.FromSeconds(10)), "adding a language waited on one still deciding about a word");
        added.Set();

        Assert.IsNotNull(lookup.Result, "the word is read by the language that was asked about it");
    }

    private static ContentLanguage Language(Func<string?, bool> reads) =>
        new(reads, static () => static source => ContentParse.Of(ContentNode.Shown(source)), static (_, _) => [], typeof(object));
}
