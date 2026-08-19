using System.Collections.Generic;
using NUnit.Framework;
using LoreVcs;

namespace LoreVcs.Tests
{
    /// <summary>
    /// Unit tests for LoreParse (the pure parsing / config-rewrite logic).
    /// Run via Window → General → Test Runner → EditMode. These cover the exact
    /// cases behind past bugs: doubled config quote, connection notices parsed as
    /// branches, port parsing, and transient-error detection.
    /// </summary>
    public class LoreParseTests
    {
        static int QuotesOnFirstLine(string s)
        {
            var line = s.Split('\n')[0];
            var n = 0;
            foreach (var c in line) if (c == '"') n++;
            return n;
        }

        [Test]
        public void Rewrite_Basic_ReplacesHostAndPort()
        {
            var cfg = "remote_url = \"lore://127.0.0.1:41337\"\nidentity = \"local\"\n";
            var outp = LoreParse.RewriteRemoteHostPort(cfg, "192.168.11.108", 41337, out _);
            Assert.AreEqual(
                "remote_url = \"lore://192.168.11.108:41337\"\nidentity = \"local\"\n", outp);
        }

        [Test]
        public void Rewrite_LeavesExactlyTwoQuotes() // regression: doubled quote (1.6.1)
        {
            var cfg = "remote_url = \"lore://127.0.0.1:41337\"\n";
            var outp = LoreParse.RewriteRemoteHostPort(cfg, "10.0.0.9", 41337, out _);
            Assert.AreEqual(2, QuotesOnFirstLine(outp));
        }

        [Test]
        public void Rewrite_PreservesPath()
        {
            var cfg = "remote_url = \"lore://127.0.0.1:41337/crysp\"\n";
            var outp = LoreParse.RewriteRemoteHostPort(cfg, "10.0.0.5", 5000, out _);
            Assert.AreEqual("remote_url = \"lore://10.0.0.5:5000/crysp\"\n", outp);
        }

        [Test]
        public void Rewrite_PreservesScheme()
        {
            var cfg = "remote_url = \"lores://host:41337\"\n";
            var outp = LoreParse.RewriteRemoteHostPort(cfg, "newhost", 41337, out _);
            Assert.AreEqual("remote_url = \"lores://newhost:41337\"\n", outp);
        }

        [Test]
        public void Rewrite_Twice_StaysClean()
        {
            var once = LoreParse.RewriteRemoteHostPort("remote_url = \"lore://a:1\"\n", "b", 2, out _);
            var twice = LoreParse.RewriteRemoteHostPort(once, "c", 3, out _);
            Assert.AreEqual("remote_url = \"lore://c:3\"\n", twice);
            Assert.AreEqual(2, QuotesOnFirstLine(twice));
        }

        [Test]
        public void Rewrite_RejectsInvalidInput()
        {
            Assert.IsNull(LoreParse.RewriteRemoteHostPort("remote_url = \"lore://a:1\"", "", 5, out _));
            Assert.IsNull(LoreParse.RewriteRemoteHostPort("remote_url = \"lore://a:1\"", "h", 0, out _));
            Assert.IsNull(LoreParse.RewriteRemoteHostPort("remote_url = \"lore://a:1\"", "h", 70000, out _));
            Assert.IsNull(LoreParse.RewriteRemoteHostPort("no url", "h", 5, out _));
        }

        [Test]
        public void HostAndPort_Parse()
        {
            Assert.AreEqual("1.2.3.4", LoreParse.RemoteHost("remote_url = \"lore://1.2.3.4:9\""));
            Assert.AreEqual(9, LoreParse.RemotePort("remote_url = \"lore://1.2.3.4:9\""));
            Assert.AreEqual(41337, LoreParse.RemotePort("remote_url = \"lore://1.2.3.4\"", 41337));
        }

        [Test]
        public void Host_Parses_EvenIfFileCorruptedWithDoubleQuote()
        {
            Assert.AreEqual("5.6.7.8", LoreParse.RemoteHost("remote_url = \"lore://5.6.7.8:41337\"\""));
        }

        [Test]
        public void Branches_IgnoresConnectionNotices() // regression (1.4.1)
        {
            var stdout =
                "Local branches:\n" +
                "Reconnecting to http://127.0.0.1:41337/ attempt 1 / 10\n" +
                "Reconnected to http://127.0.0.1:41337/\n" +
                "* main\n" +
                "Warning: Could not query remote branch list\n";
            var names = LoreParse.Branches(stdout, out var current);
            Assert.AreEqual(1, names.Count);
            Assert.Contains("main", names);
            Assert.AreEqual("main", current);
        }

        [Test]
        public void Branches_KeepsSlashNames()
        {
            var stdout = "Remote branches:\n  feature/beam-continuous-cut\n  main\n";
            var names = LoreParse.Branches(stdout, out _);
            Assert.Contains("feature/beam-continuous-cut", names);
            Assert.AreEqual(2, names.Count);
        }

        [Test]
        public void RepoName_SkipsNotices()
        {
            var stdout =
                "Reconnecting to http://127.0.0.1:41337/ attempt 1 / 10\n" +
                "spaceship-scavenger (01a00d4fcc757c51b16c01be71f2acbc)\n";
            Assert.AreEqual("spaceship-scavenger", LoreParse.RepoName(stdout, "fallback"));
        }

        [Test]
        public void RepoName_FallsBack()
        {
            Assert.AreEqual("fallback", LoreParse.RepoName("no id here", "fallback"));
        }

        [Test]
        public void Status_ParsesBranchRevisionSync()
        {
            var stdout =
                "On branch main revision 24 -> abc123\n" +
                "Local branch in sync with remote\n";
            LoreParse.Status(stdout, out var b, out var rev, out var sync, out var ch);
            Assert.AreEqual("main", b);
            Assert.AreEqual("24", rev);
            Assert.IsTrue(sync);
            Assert.AreEqual(0, ch.Count);
        }

        [Test]
        public void Status_CollectsChanges()
        {
            var stdout = "On branch main revision 3 -> x\nA Assets/New.cs\nM Assets/Old.cs\n";
            LoreParse.Status(stdout, out _, out _, out var sync, out var ch);
            Assert.IsFalse(sync);
            Assert.AreEqual(2, ch.Count);
        }

        [Test]
        public void History_ParsesEntries()
        {
            var stdout =
                "Revision  : 24\nSignature : abcdef\nDate      : Mon\n    Message one\nCommitter : local\n\n" +
                "Revision  : 23\nSignature : 999\n    Another\n";
            var hist = LoreParse.History(stdout);
            Assert.AreEqual(2, hist.Count);
            Assert.AreEqual("24", hist[0].Revision);
            Assert.AreEqual("abcdef", hist[0].Signature);
            Assert.AreEqual("local", hist[0].Committer);
            Assert.AreEqual("Message one", hist[0].Message);
        }

        [Test]
        public void TransientError_Detection()
        {
            Assert.IsTrue(LoreParse.IsTransientConnectionError("gRPC connection: transport error"));
            Assert.IsTrue(LoreParse.IsTransientConnectionError("Not connected to remote"));
            Assert.IsTrue(LoreParse.IsTransientConnectionError("acquiring remote: operation was canceled"));
            Assert.IsFalse(LoreParse.IsTransientConnectionError("error: file not found"));
            Assert.IsFalse(LoreParse.IsTransientConnectionError(""));
        }
    }
}
