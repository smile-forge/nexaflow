using Nexaflow.IO.Documents.Processing;

namespace Nexaflow.Tests.IO.Documents;

/// <summary>
/// What a model is handed at once, and how many such questions are asked at once: a model whose answers each have their context
/// to itself is asked a few at a time, each as large as a question is made; one whose answers share one context is asked as many
/// at once as it writes answers, less one for anyone else, and fits them in its context as their sizes allow — none of them ever
/// larger than most of it.
/// </summary>
[TestClass]
public class DocumentBudgetTests
{
    [TestMethod]
    public void AModelWhoseAnswersEachHaveTheirOwnContext_IsAskedAFewAtATime_EachAsLargeAsAQuestionIsMade()
    {
        var budget = DocumentBudget.Of(ScriptedModel.Plain(context: 200_000));

        Assert.AreEqual(DocumentBudget.Separately, budget.AtOnce);
        Assert.AreEqual(DocumentBudget.MostTokens * 3 * 9 / 10, budget.Characters, "no larger than a question is made, however much it reads");
    }

    [TestMethod]
    public void AModelSharingItsContext_IsAskedAsManyAtOnceAsItWritesAnswers_LessOne()
    {
        var budget = DocumentBudget.Of(ScriptedModel.Plain(context: 262_144, repliesAtOnce: 32));

        Assert.AreEqual(31, budget.AtOnce, "one answer left for whoever else asks it meanwhile");
        Assert.AreEqual(DocumentBudget.MostTokens * 3 * 9 / 10, budget.Characters, "each no larger than a question is made");
    }

    [TestMethod]
    public void AModelSharingASmallContext_IsAskedNothingLargerThanMostOfIt()
    {
        var budget = DocumentBudget.Of(ScriptedModel.Plain(context: 8_192, repliesAtOnce: 32));

        Assert.AreEqual(31, budget.AtOnce, "the model fits what it can and has the rest wait");
        Assert.AreEqual(8_192 * 3 / 4 * 3 * 9 / 10, budget.Characters, "three quarters of what it holds, never all of it");
    }

    [TestMethod]
    public void AModelGivingEachAnswerAContextOfItsOwn_IsAskedQuestionsThatFillIt_AsManyAtOnceAsItWrites_LessOne()
    {
        var budget = DocumentBudget.Of(new ScriptedModel((_, _) => new("", DocumentReplyEnd.Finished), context: 8_192, repliesAtOnce: 32, sharesContext: false));

        Assert.AreEqual(31, budget.AtOnce);
        Assert.AreEqual(8_192 * 3 * 9 / 10, budget.Characters, "all of its own context, not three quarters of one shared");
    }
}
