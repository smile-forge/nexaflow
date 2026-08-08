using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nexaflow.Features.Common;
using Nexaflow.Features.Hex.Buffer;
using Nexaflow.Features.Hex.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Hex;

/// <summary>
/// A file the viewer cannot read must say so. Every read path in the buffer has a "return 0" escape,
/// which is indistinguishable from a file genuinely full of <c>0x00</c> — so the buffer records why it
/// gave up, and the tab asks the user whether to retry or close rather than presenting the zeroes as
/// the file's contents.
/// </summary>
[TestClass]
[CoversNode("hex")]
public sealed class HexReadFailureTests
{
    private static readonly IShellServices _shell = Substitute.For<IShellServices>();

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"hexfail_{Guid.NewGuid():N}.bin");

    [TestMethod, TestCategory("Unit")]
    public void No_file_is_not_a_failure()
    {
        // The empty-path buffer is a legitimate empty editor, not something to prompt about.
        using var buffer = new HexBuffer(string.Empty);

        Assert.IsNull(buffer.Failure);
        Assert.AreEqual(0, buffer.FileLength);
    }

    [TestMethod, TestCategory("Unit")]
    public void A_readable_file_reports_no_failure()
    {
        var path = TempPath();
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        try
        {
            using var buffer = new HexBuffer(path);

            Assert.IsNull(buffer.Failure);
            Assert.AreEqual(4, buffer.FileLength);
            Assert.AreEqual(1, buffer.ReadByte(0));
        }
        finally { File.Delete(path); }
    }

    [TestMethod, TestCategory("Unit")]
    public void A_missing_file_is_reported_as_missing()
    {
        using var buffer = new HexBuffer(TempPath());

        Assert.IsNotNull(buffer.Failure);
        Assert.AreEqual(HexReadFault.Missing, buffer.Failure!.Fault);
        StringAssert.Contains(buffer.Failure.Message, "isn't there any more");
    }

    [TestMethod, TestCategory("Unit")]
    public void A_file_another_program_holds_exclusively_is_reported_as_locked()
    {
        var path = TempPath();
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        try
        {
            // FileShare.None is what a program that refuses to share looks like from the outside.
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                using var buffer = new HexBuffer(path);

                Assert.IsNotNull(buffer.Failure);
                Assert.AreEqual(HexReadFault.Locked, buffer.Failure!.Fault);
                StringAssert.Contains(buffer.Failure.Message, "isn't sharing it");
            }
        }
        finally { File.Delete(path); }
    }

    [TestMethod, TestCategory("Unit")]
    public void A_failed_open_leaves_the_buffer_empty_rather_than_full_of_zeroes()
    {
        using var buffer = new HexBuffer(TempPath());

        // The distinction that matters: no rows at all, not a screen of 00 bytes claiming to be the file.
        Assert.AreEqual(0, buffer.FileLength);
        Assert.AreEqual(0, buffer.VirtualLength);
        Assert.AreEqual(0, buffer.TotalRows);
    }

    [TestMethod, TestCategory("Unit")]
    public void Reload_clears_the_failure_once_the_file_can_be_read()
    {
        var path = TempPath();
        File.WriteAllBytes(path, [0xAA, 0xBB]);
        try
        {
            HexBuffer buffer;
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                buffer = new HexBuffer(path);
                Assert.IsNotNull(buffer.Failure, "locked while the exclusive handle is open");
            }

            buffer.Reload();   // the other program let go — this is what Retry does

            Assert.IsNull(buffer.Failure);
            Assert.AreEqual(2, buffer.FileLength);
            Assert.AreEqual(0xAA, buffer.ReadByte(0));
            buffer.Dispose();
        }
        finally { File.Delete(path); }
    }

    [TestMethod, TestCategory("Unit")]
    public void Reload_keeps_the_failure_when_the_file_still_cannot_be_read()
    {
        var path = TempPath();
        File.WriteAllBytes(path, [1]);
        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                using var buffer = new HexBuffer(path);
                buffer.Reload();

                Assert.IsNotNull(buffer.Failure);
                Assert.AreEqual(HexReadFault.Locked, buffer.Failure!.Fault);
            }
        }
        finally { File.Delete(path); }
    }

    [TestMethod, TestCategory("Unit")]
    public void A_file_that_stops_delivering_bytes_is_reported_rather_than_painted_as_zeroes()
    {
        // The symptom this whole path exists for: the header and size are right, every byte reads 00.
        // Reproduced by emptying the file under the buffer's open handle — the same shape as a share
        // that drops, or a file replaced while its tab sat open.
        var path = TempPath();
        File.WriteAllBytes(path, new byte[1024]);
        try
        {
            using var buffer = new HexBuffer(path);
            Assert.IsNull(buffer.Failure, "opened cleanly");
            Assert.AreEqual(1024, buffer.FileLength);

            using (var truncate = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                truncate.SetLength(0);

            buffer.EnsureWindow(0, 20);   // what a scroll or a repaint does

            Assert.IsNotNull(buffer.Failure, "a read that returns nothing must not pass as a file of 00s");
            Assert.AreEqual(HexReadFault.Unreadable, buffer.Failure!.Fault);
            StringAssert.Contains(buffer.Failure.Message, "stopped returning data");
        }
        finally { File.Delete(path); }
    }

    // ── The view-model surface the tab prompt is driven from ──────────────────

    [TestMethod, TestCategory("Unit")]
    public void The_view_model_reports_an_unreadable_file_once()
    {
        using var vm = new HexViewModel(TempPath(), _shell);
        var reported = new List<HexReadFailure>();
        vm.ReadFailed += reported.Add;

        vm.CheckReadable();
        vm.CheckReadable();   // a scroll, a re-layout — the prompt is already up
        vm.CheckReadable();

        Assert.AreEqual(1, reported.Count, "one failure episode is one prompt, not one per repaint");
        Assert.AreEqual(HexReadFault.Missing, reported[0].Fault);
    }

    [TestMethod, TestCategory("Unit")]
    public void The_view_model_stays_quiet_for_a_readable_file()
    {
        var path = TempPath();
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            using var vm = new HexViewModel(path, _shell);
            var reported = new List<HexReadFailure>();
            vm.ReadFailed += reported.Add;

            vm.CheckReadable();

            Assert.AreEqual(0, reported.Count);
            Assert.IsNull(vm.LoadFailure);
        }
        finally { File.Delete(path); }
    }

    [TestMethod, TestCategory("Unit")]
    public void Retry_reloads_the_file_and_reports_success()
    {
        var path = TempPath();
        File.WriteAllBytes(path, new byte[64]);
        try
        {
            HexViewModel vm;
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                vm = new HexViewModel(path, _shell);
                Assert.IsNotNull(vm.LoadFailure);
                Assert.AreEqual(0, vm.TotalRows);
            }

            Assert.IsTrue(vm.Retry(), "the lock is gone, so the retry should succeed");
            Assert.IsNull(vm.LoadFailure);
            Assert.AreEqual(4, vm.TotalRows, "64 bytes = 4 rows of 16");
            vm.Dispose();
        }
        finally { File.Delete(path); }
    }

    [TestMethod, TestCategory("Unit")]
    public void A_failed_retry_reports_failure_without_re_raising()
    {
        var path = TempPath();
        File.WriteAllBytes(path, [1]);
        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                using var vm = new HexViewModel(path, _shell);
                var reported = new List<HexReadFailure>();
                vm.ReadFailed += reported.Add;
                vm.CheckReadable();

                Assert.IsFalse(vm.Retry(), "still locked");

                // The caller is mid-prompt and drives the next round itself; a second event here would
                // stack a duplicate dialog behind the one already on screen.
                Assert.AreEqual(1, reported.Count);
                vm.CheckReadable();
                Assert.AreEqual(1, reported.Count);
            }
        }
        finally { File.Delete(path); }
    }

    // ── Wording ───────────────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void Failures_are_classified_from_the_exception_not_guessed()
    {
        Assert.AreEqual(HexReadFault.AccessDenied,
            HexReadFailure.For(@"C:\x\a.bin", new UnauthorizedAccessException()).Fault);
        Assert.AreEqual(HexReadFault.Missing,
            HexReadFailure.For(@"C:\x\a.bin", new FileNotFoundException()).Fault);
        Assert.AreEqual(HexReadFault.Missing,
            HexReadFailure.For(@"C:\x\a.bin", new DirectoryNotFoundException()).Fault);
        Assert.AreEqual(HexReadFault.Unreadable,
            HexReadFailure.For(@"C:\x\a.bin", new IOException("disk fell over")).Fault);
    }

    [TestMethod, TestCategory("Unit")]
    public void A_failure_message_names_the_file_and_never_leaks_a_stack_trace()
    {
        var failure = HexReadFailure.For(@"C:\tools\payload.exe", new UnauthorizedAccessException());

        StringAssert.Contains(failure.Message, "payload.exe");
        Assert.IsFalse(failure.Message.Contains("Exception"), "the user gets plain words, not a type name");
    }
}
