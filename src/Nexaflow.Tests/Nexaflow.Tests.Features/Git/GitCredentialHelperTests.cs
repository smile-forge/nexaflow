using System.IO;
using LibGit2Sharp;
using Nexaflow.Features.Git.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Git;

/// <summary>
/// Unit coverage for the GCM bridge that fixes the "remote authentication required but no callback set"
/// pull failure. The subprocess protocol (parse/build) and the LibGit2Sharp provider mapping are exercised
/// without a real git via the injectable runner seam; end-to-end resolution against a live credential store
/// is verified manually.
/// </summary>
[TestClass]
[CoversNode("git-credentials")]
public class GitCredentialHelperTests
{
    private static GitCredentialHelper Helper(Func<string, string, string?> run) => new(@"C:\repo", run);

    // ── ParseCredentialOutput ─────────────────────────────────────────────────

    [TestMethod]
    public void ParseCredentialOutput_ExtractsUsernameAndPassword()
    {
        var fields = GitCredentialHelper.ParseCredentialOutput(
            "protocol=https\nhost=bitbucket.org\nusername=x-token-auth\npassword=ATBB-secret\n\n");

        Assert.AreEqual("x-token-auth", fields["username"]);
        Assert.AreEqual("ATBB-secret", fields["password"]);
    }

    [TestMethod]
    public void ParseCredentialOutput_PreservesEqualsInValue()
    {
        var fields = GitCredentialHelper.ParseCredentialOutput("password=ab=cd==ef\n\n");
        Assert.AreEqual("ab=cd==ef", fields["password"]);
    }

    [TestMethod]
    public void ParseCredentialOutput_StopsAtFirstBlankLine()
    {
        var fields = GitCredentialHelper.ParseCredentialOutput("username=u\npassword=p\n\nusername=IGNORED\n");

        Assert.AreEqual("u", fields["username"]);
        Assert.AreEqual("p", fields["password"]);
        Assert.AreEqual(2, fields.Count);   // nothing past the blank line
    }

    [TestMethod]
    public void ParseCredentialOutput_KeepsButDoesNotRequireExtraKeys()
    {
        var fields = GitCredentialHelper.ParseCredentialOutput(
            "username=u\npassword=p\npassword_expiry_utc=1789000000\noauth_refresh_token=xyz\n\n");

        Assert.AreEqual("p", fields["password"]);
        Assert.IsTrue(fields.ContainsKey("password_expiry_utc"));   // parsed, but Fill ignores it
    }

    [TestMethod]
    public void ParseCredentialOutput_HandlesCrlf()
    {
        var fields = GitCredentialHelper.ParseCredentialOutput("username=u\r\npassword=p\r\n\r\n");

        Assert.AreEqual("u", fields["username"]);
        Assert.AreEqual("p", fields["password"]);
    }

    // ── BuildFillInput / BuildStoreInput ──────────────────────────────────────

    [TestMethod]
    public void BuildFillInput_IsSingleUrlLineBlankTerminated()
    {
        Assert.AreEqual(
            "url=https://bitbucket.org/acme/widgets.git\n\n",
            GitCredentialHelper.BuildFillInput("https://bitbucket.org/acme/widgets.git"));
    }

    [TestMethod]
    public void BuildStoreInput_IncludesUrlUsernamePasswordBlankTerminated()
    {
        var input = GitCredentialHelper.BuildStoreInput(
            "https://bitbucket.org/acme/widgets.git", new GitCredential("x-token-auth", "tok"));

        StringAssert.Contains(input, "url=https://bitbucket.org/acme/widgets.git");
        StringAssert.Contains(input, "username=x-token-auth");
        StringAssert.Contains(input, "password=tok");
        Assert.IsTrue(input.EndsWith("\n\n"));
    }

    // ── Fill (via injected runner) ────────────────────────────────────────────

    [TestMethod]
    public void Fill_ParsesRunnerOutput()
    {
        var helper = Helper((sub, stdin) =>
        {
            Assert.AreEqual("fill", sub);
            StringAssert.Contains(stdin, "url=https://bitbucket.org/acme/widgets.git");
            return "username=x-token-auth\npassword=tok\n\n";
        });

        var cred = helper.Fill("https://bitbucket.org/acme/widgets.git");

        Assert.IsNotNull(cred);
        Assert.AreEqual("x-token-auth", cred!.Username);
        Assert.AreEqual("tok", cred.Password);
    }

    [TestMethod]
    public void Fill_GitAbsent_ReturnsNull()
        => Assert.IsNull(Helper((_, _) => null).Fill("https://bitbucket.org/acme/widgets.git"));

    [TestMethod]
    public void Fill_MissingPassword_ReturnsNull()
        => Assert.IsNull(Helper((_, _) => "username=only\n\n").Fill("https://bitbucket.org/acme/widgets.git"));

    // ── Provider mapping ──────────────────────────────────────────────────────

    [TestMethod]
    public void Provider_NonUsernamePasswordType_ReturnsDefaultCredentialsWithoutInvokingHelper()
    {
        var helper = Helper((_, _) => { Assert.Fail("helper must not run when UsernamePassword is unsupported"); return null; });

        var cred = helper.Provider("git@bitbucket.org:acme/widgets.git", "git", SupportedCredentialTypes.Default);

        Assert.IsInstanceOfType(cred, typeof(DefaultCredentials));
    }

    [TestMethod]
    public void Provider_UsernamePassword_NullFill_ReturnsDefaultCredentials()
    {
        var cred = Helper((_, _) => null)
            .Provider("https://bitbucket.org/acme/widgets.git", "", SupportedCredentialTypes.UsernamePassword);

        Assert.IsInstanceOfType(cred, typeof(DefaultCredentials));
    }

    [TestMethod]
    public void Provider_UsernamePassword_WithCredential_ReturnsUsernamePassword()
    {
        var cred = Helper((_, _) => "username=x-token-auth\npassword=tok\n\n")
            .Provider("https://bitbucket.org/acme/widgets.git", "", SupportedCredentialTypes.UsernamePassword);

        var up = cred as UsernamePasswordCredentials;
        Assert.IsNotNull(up);
        Assert.AreEqual("x-token-auth", up!.Username);
        Assert.AreEqual("tok", up.Password);
    }

    // ── Real subprocess: exe not on PATH ──────────────────────────────────────

    [TestMethod]
    public void RunProcessCredential_ExeNotFound_ReturnsNull()
    {
        var result = GitCredentialHelper.RunProcessCredential(
            "nexaflow-nonexistent-git-xyz", Path.GetTempPath(), "fill", "url=https://x\n\n", 2_000);

        Assert.IsNull(result);
    }
}
