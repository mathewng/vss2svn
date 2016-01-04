using SharpSvn;
using SharpSvn.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Hpdi.Vss2Svn
{
    class SvnWrapper
    {
        private readonly string trunkDirectory = "trunk";
        private readonly string tagsDirectory = "tags";
        private readonly string vssLabelDirectory = "vsslabels";

        private readonly string workingCopyPath;
        private readonly string trunkPath;
        private readonly string labelPath;
        private readonly string tagPath;
        private readonly Logger logger;
        private readonly Stopwatch stopwatch = new Stopwatch();
        private string svnExecutable = "svn.exe";
        private string svnInitialArguments = null;
        private bool shellQuoting = false;
        private Encoding commitEncoding = Encoding.UTF8;
        private readonly IWin32Window parentWindow;
        private readonly bool useSvnStandardDirStructure;

        public TimeSpan ElapsedTime
        {
            get { return stopwatch.Elapsed; }
        }

        public string SvnExecutable
        {
            get { return svnExecutable; }
            set { svnExecutable = value; }
        }

        public string SvnInitialArguments
        {
            get { return svnInitialArguments; }
            set { svnInitialArguments = value; }
        }

        public bool ShellQuoting
        {
            get { return shellQuoting; }
            set { shellQuoting = value; }
        }

        public Encoding CommitEncoding
        {
            get { return commitEncoding; }
            set { commitEncoding = value; }
        }

        public bool UseSvnStandardDirStructure
        {
            get { return useSvnStandardDirStructure; }
        }

        public SvnWrapper(string workingCopyPath, bool useSvnStandardDirStructure, Logger logger, IWin32Window parentWindow)
        {
            this.workingCopyPath = workingCopyPath;
            this.trunkPath = Path.Combine(workingCopyPath, trunkDirectory);
            this.tagPath = Path.Combine(workingCopyPath, tagsDirectory);
            this.labelPath = Path.Combine(workingCopyPath, vssLabelDirectory);
            this.useSvnStandardDirStructure = useSvnStandardDirStructure;
            this.logger = logger;
            this.parentWindow = parentWindow;
        }

        public bool FindExecutable()
        {
            /*
            string foundPath;
            if (FindInPathVar("svn.exe", out foundPath))
            {
                svnExecutable = foundPath;
                svnInitialArguments = null;
                shellQuoting = false;
                return true;
            }
            if (FindInPathVar("svn.cmd", out foundPath))
            {
                svnExecutable = "cmd.exe";
                svnInitialArguments = "/c svn";
                shellQuoting = true;
                return true;
            }
            return false;
            */
            return true;
        }

        public void Init()
        {
            //SvnExec("init");
        }

        public void SetConfig(string name, string value)
        {
            //SvnExec("config " + name + " " + Quote(value));
        }

        public bool Add(string path)
        {
            /*
            var startInfo = GetStartInfo("add -- " + Quote(path));

            // add fails if there are no files (directories don't count)
            return ExecuteUnless(startInfo, "did not match any files");
            */

            using (var client = new SvnClient())
            {
                try
                {
                    SvnUI.Bind(client, parentWindow);
                    return client.Add(path, new SvnAddArgs { AddParents = false, Depth = SvnDepth.Infinity, Force = true });

                }
                catch (SvnException e)
                {
                    MessageBox.Show(string.Format("{0} Error Code: {1}{2}", e.Message, e.SvnErrorCode, Environment.NewLine), "SVN Operation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            return false;

        }

        public bool Add(IEnumerable<string> paths)
        {

            if (CollectionUtil.IsEmpty(paths))
            {
                return false;
            }
            /*
            var args = new StringBuilder("add -- ");
            CollectionUtil.Join(args, " ", CollectionUtil.Transform<string, string>(paths, Quote));
            var startInfo = GetStartInfo(args.ToString());

            // add fails if there are no files (directories don't count)
            return ExecuteUnless(startInfo, "did not match any files");
            */
            return paths
                .Select(path => Add(path))
                .Aggregate(true, (state, val) => state &= val);

        }

        /*
         * This adds, modifies, and removes index entries to match the working tree.
         */
        public bool AddAll()
        {
            /*
            var startInfo = GetStartInfo("add -A");

            // add fails if there are no files (directories don't count)
            return ExecuteUnless(startInfo, "did not match any files");
            */
            var overallStatus = true;

            using (var client = new SvnClient())
            {
                try
                {
                    SvnUI.Bind(client, parentWindow);

                    var statusList = (Collection<SvnStatusEventArgs>)null;
                    var svnStatusArgs = new SvnStatusArgs { Depth = SvnDepth.Infinity, IgnoreExternals = false, KeepDepth = false, RetrieveIgnoredEntries = false };

                    if (client.GetStatus(useSvnStandardDirStructure ? trunkPath : workingCopyPath, svnStatusArgs, out statusList))
                    {
                        overallStatus = statusList.Select(svnStatusEventArg =>
                        {
                            switch (svnStatusEventArg.LocalNodeStatus)
                            {
                                case SvnStatus.Missing:
                                    logger.WriteLine("Commit: Deleting file {0} due to status = {1}", svnStatusEventArg.FullPath, svnStatusEventArg.LocalNodeStatus);
                                    return client.Delete(svnStatusEventArg.FullPath, new SvnDeleteArgs { KeepLocal = false, Force = false });
                                case SvnStatus.NotVersioned:
                                    logger.WriteLine("Commit: Adding file {0} due to status = {1}", svnStatusEventArg.FullPath, svnStatusEventArg.LocalNodeStatus);
                                    return client.Add(svnStatusEventArg.FullPath, new SvnAddArgs { AddParents = false, Depth = SvnDepth.Infinity, Force = false });
                                default:
                                    return true;
                            }
                        })
                        .Aggregate(true, (state, val) => state &= val);
                    }
                    else
                        overallStatus = false;
                }
                catch (SvnException e)
                {
                    MessageBox.Show(string.Format("{0} Error Code: {1}{2}", e.Message, e.SvnErrorCode, Environment.NewLine), "SVN Operation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            return overallStatus;
        }

        public void Remove(string path, bool recursive)
        {
            /*
            SvnExec("rm " + (recursive ? "-r " : "") + "-- " + Quote(path));
            */
            using (var client = new SvnClient())
            {
                try
                {
                    SvnUI.Bind(client, parentWindow);
                    var result = client.Delete(path, new SvnDeleteArgs { Force = true, KeepLocal = false });
                }
                catch (SvnException e)
                {
                    MessageBox.Show(string.Format("{0} Error Code: {1}{2}", e.Message, e.SvnErrorCode, Environment.NewLine), "SVN Operation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public void Move(string sourcePath, string destPath)
        {
            /*
            SvnExec("mv -- " + Quote(sourcePath) + " " + Quote(destPath));
            */
            using (var client = new SvnClient())
            {
                try
                {
                    SvnUI.Bind(client, parentWindow);
                    var result = client.Move(sourcePath, destPath, new SvnMoveArgs { AllowMixedRevisions = false, AlwaysMoveAsChild = false, CreateParents = true, Force = true, MetaDataOnly = false });
                }
                catch (SvnException e)
                {
                    MessageBox.Show(string.Format("{0} Error Code: {1}{2}", e.Message, e.SvnErrorCode, Environment.NewLine), "SVN Operation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        class TempFile : IDisposable
        {
            private readonly string name;
            private readonly FileStream fileStream;

            public string Name
            {
                get { return name; }
            }

            public TempFile() : this(null)
            {
            }

            public TempFile(string directory)
            {
                name = directory == null ? Path.GetTempFileName() : Path.Combine(directory, Path.GetRandomFileName());
                fileStream = new FileStream(name, FileMode.Truncate, FileAccess.Write, FileShare.Read);
            }

            public void Write(string text, Encoding encoding)
            {
                var bytes = encoding.GetBytes(text);
                fileStream.Write(bytes, 0, bytes.Length);
                fileStream.Flush();
            }

            public void Dispose()
            {
                if (fileStream != null)
                {
                    fileStream.Dispose();
                }
                if (name != null)
                {
                    File.Delete(name);
                }
            }
        }

        private void AddComment(string comment, ref string args, out TempFile tempFile)
        {
            tempFile = null;
            /*
            tempFile = null;
            if (string.IsNullOrEmpty(comment))
            {
                args += " --allow-empty-message --no-edit -m \"\"";
            }
            else
            {
                // need to use a temporary file to specify the comment when not
                // using the system default code page or it contains newlines
                if (commitEncoding.CodePage != Encoding.Default.CodePage || comment.IndexOf('\n') >= 0)
                {
                    logger.WriteLine("Generating temp file for comment: {0}", comment);
                    tempFile = new TempFile(tempDirectory);
                    tempFile.Write(comment, commitEncoding);

                    // temporary path might contain spaces (e.g. "Documents and Settings")
                    args += " -F " + Quote(tempFile.Name);
                }
                else
                {
                    args += " -m " + Quote(comment);
                }
            }
            */
        }

        public bool Commit(string authorName, string authorEmail, string comment, DateTime localTime)
        {
            /*
            TempFile commentFile;

            var args = "commit";
            AddComment(comment, ref args, out commentFile);

            using (commentFile)
            {
                var startInfo = GetStartInfo(args);
                startInfo.EnvironmentVariables["GIT_AUTHOR_NAME"] = authorName;
                startInfo.EnvironmentVariables["GIT_AUTHOR_EMAIL"] = authorEmail;
                startInfo.EnvironmentVariables["GIT_AUTHOR_DATE"] = GetUtcTimeString(localTime);

                // also setting the committer is supposedly useful for converting to Mercurial
                startInfo.EnvironmentVariables["GIT_COMMITTER_NAME"] = authorName;
                startInfo.EnvironmentVariables["GIT_COMMITTER_EMAIL"] = authorEmail;
                startInfo.EnvironmentVariables["GIT_COMMITTER_DATE"] = GetUtcTimeString(localTime);

                // ignore empty commits, since they are non-trivial to detect
                // (e.g. when renaming a directory)
                return ExecuteUnless(startInfo, "nothing to commit");
            }
            */
            if (string.IsNullOrEmpty(authorName))
            {
                return false;
            }
            using (var client = new SvnClient())
            {
                try
                {
                    SvnUI.Bind(client, parentWindow);
                    var svnCommitArgs = new SvnCommitArgs { LogMessage = comment };

                    var svnCommitResult = (SvnCommitResult)null;
                    var result = client.Commit(useSvnStandardDirStructure ? trunkPath : workingCopyPath, svnCommitArgs, out svnCommitResult);
                    // commit without files results in result=true and svnCommitResult=null
                    if (svnCommitResult != null)
                    {
                        if (result)
                        {

                            var workingCopyUri = client.GetUriFromWorkingCopy(useSvnStandardDirStructure ? trunkPath : workingCopyPath);

                            result &= client.SetRevisionProperty(svnCommitResult.RepositoryRoot, new SvnRevision(svnCommitResult.Revision), SvnPropertyNames.SvnAuthor, authorName);
                            result &= client.SetRevisionProperty(svnCommitResult.RepositoryRoot, new SvnRevision(svnCommitResult.Revision), SvnPropertyNames.SvnDate, SvnPropertyNames.FormatDate(localTime));

                            result &= client.Update(workingCopyPath, new SvnUpdateArgs { AddsAsModifications = false, AllowObstructions = false, Depth = SvnDepth.Infinity, IgnoreExternals = true, KeepDepth = true, Revision = SvnRevision.Head, UpdateParents = false });
                        }
                        else
                        {
                            MessageBox.Show(string.Format("{0} Error Code: {1}{2}", svnCommitResult.PostCommitError, "", Environment.NewLine), "SVN Commit Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    return result;

                }
                catch (SvnException e)
                {
                    MessageBox.Show(string.Format("{0} Error Code: {1}{2}", e.Message, e.SvnErrorCode, Environment.NewLine), "SVN Operation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            return false;
        }

        public bool Update(string path)
        {
            using (var client = new SvnClient())
            {
                try
                {
                    SvnUI.Bind(client, parentWindow);
                    return client.Update(path, new SvnUpdateArgs { AddsAsModifications = false, AllowObstructions = false, Depth = SvnDepth.Infinity, IgnoreExternals = true, KeepDepth = true, Revision = SvnRevision.Head, UpdateParents = false });
                }
                catch (SvnException e)
                {
                    MessageBox.Show(string.Format("{0} Error Code: {1}{2}", e.Message, e.SvnErrorCode, Environment.NewLine), "SVN Operation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            return false;
        }

        public void VssLabel(string name, string taggerName, string taggerEmail, string comment, DateTime localTime, string taggedPath)
        {
            using (var client = new SvnClient())
            {
                try
                {
                    SvnUI.Bind(client, parentWindow);

                    var codeBasePath = useSvnStandardDirStructure ? this.trunkPath : workingCopyPath;
                    var relativePath = taggedPath.StartsWith(codeBasePath) ? taggedPath.Substring(codeBasePath.Length) : null;
                    if (relativePath == null || client.GetUriFromWorkingCopy(taggedPath) == null)
                        throw new ArgumentException(string.Format("invalid path {0}", taggedPath));

                    var fullLabelPath = Path.Combine(labelPath, name + relativePath);
                    
                    Uri repositoryRootUri = client.GetUriFromWorkingCopy(workingCopyPath);

                    var codeBaseUri = client.GetUriFromWorkingCopy(codeBasePath);
                    var labelBaseUri = client.GetUriFromWorkingCopy(labelPath);

                    var relativeSourceUri = new Uri(taggedPath.Substring(workingCopyPath.Length), UriKind.Relative);
                    relativeSourceUri = repositoryRootUri.MakeRelativeUri(new Uri(repositoryRootUri, relativeSourceUri));

                    var relativeLabelUri = new Uri(fullLabelPath.Substring(workingCopyPath.Length), UriKind.Relative);
                    relativeLabelUri = repositoryRootUri.MakeRelativeUri(new Uri(repositoryRootUri, relativeLabelUri));

                    var sourceUri = client.GetUriFromWorkingCopy(taggedPath);
                    var labelUri = new Uri(labelBaseUri, name + "/" + sourceUri.ToString().Substring(codeBaseUri.ToString().Length));

                    var fullLabelPathExists = client.GetUriFromWorkingCopy(fullLabelPath) != null;

                    // check intermediate parents
                    var intermediateParentNames = labelUri.ToString().Substring(labelBaseUri.ToString().Length).Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Reverse().Skip(1).Reverse();
                    var intermediateParentRelativeUriToCreate = new List<string>();
                    {
                        var intermediatePath = labelPath;
                        var intermediateUriPath = repositoryRootUri.MakeRelativeUri(labelBaseUri).ToString();
                        foreach (var parent in intermediateParentNames)
                        {
                            intermediatePath = Path.Combine(intermediatePath, parent);
                            intermediateUriPath += parent + "/";
                            if (client.GetUriFromWorkingCopy(intermediatePath) == null)
                                intermediateParentRelativeUriToCreate.Add(intermediateUriPath.Substring(0, intermediateUriPath.Length - 1));
                        }
                    }


                    // perform svn copy or svn delete + svn copy if necessary
                    var result = true;
                    var svnCommitResult = (SvnCommitResult)null;

                    client.RepositoryOperation(repositoryRootUri, new SvnRepositoryOperationArgs { LogMessage = comment }, delegate (SvnMultiCommandClient muccClient)
                    {
                        // if label path already exists, delete first
                        if (fullLabelPathExists)
                            result &= muccClient.Delete(Uri.UnescapeDataString(relativeLabelUri.ToString()));

                        // create intermediate parents if necessary
                        foreach (var parentRelativeUri in intermediateParentRelativeUriToCreate)
                            result &= muccClient.CreateDirectory(Uri.UnescapeDataString(parentRelativeUri));

                        result &= muccClient.Copy(Uri.UnescapeDataString(relativeSourceUri.ToString()), Uri.UnescapeDataString(relativeLabelUri.ToString()));
                        

                    }, out svnCommitResult);
                    /*
                    using (var muccClient = new SvnMultiCommandClient(repositoryRootUri, new SvnRepositoryOperationArgs { LogMessage = comment }))
                    {
                        var relativeSourceUri = new Uri(sourceUri.ToString(), UriKind.Relative);
                        var relativeLabelUri = new Uri(labelUri.ToString(), UriKind.Relative);



                        // if label path already exists, delete first
                        if (client.GetUriFromWorkingCopy(fullLabelPath) != null)
                            result &= muccClient.Delete(relativeLabelUri.ToString());

                        result &= muccClient.Copy(relativeSourceUri.ToString(), relativeLabelUri.ToString());
                        result &= muccClient.Commit(out svnCommitResult);
                    }
                    */
                    if (result)
                    {
                        result &= client.SetRevisionProperty(svnCommitResult.RepositoryRoot, new SvnRevision(svnCommitResult.Revision), SvnPropertyNames.SvnAuthor, taggerName);
                        result &= client.SetRevisionProperty(svnCommitResult.RepositoryRoot, new SvnRevision(svnCommitResult.Revision), SvnPropertyNames.SvnDate, SvnPropertyNames.FormatDate(localTime));
                    }

                    result &= client.Update(workingCopyPath, new SvnUpdateArgs { AddsAsModifications = false, AllowObstructions = false, Depth = SvnDepth.Infinity, IgnoreExternals = true, KeepDepth = true, Revision = SvnRevision.Head, UpdateParents = false });


                    /*
                    var svnCommitResult = (SvnCommitResult)null;
                    // label already exists. we do a 2-step operation. delete followed by a copy.
                    if (client.GetUriFromWorkingCopy(fullLabelPath) != null)
                    {
                        result &= client.RemoteDelete(labelUri, new SvnDeleteArgs { Force = true, KeepLocal = false, LogMessage = comment }, out svnCommitResult);
                        if (result)
                        {
                            result &= client.SetRevisionProperty(svnCommitResult.RepositoryRoot, new SvnRevision(svnCommitResult.Revision), SvnPropertyNames.SvnAuthor, taggerName);
                            result &= client.SetRevisionProperty(svnCommitResult.RepositoryRoot, new SvnRevision(svnCommitResult.Revision), SvnPropertyNames.SvnDate, SvnPropertyNames.FormatDate(localTime));
                        }
                    }
                    result &= client.RemoteCopy(sourceUri, labelUri, new SvnCopyArgs { AlwaysCopyAsChild = false, CreateParents = true, IgnoreExternals = true, LogMessage = comment, MetaDataOnly = false, PinExternals = false, Revision = SvnRevision.Head }, out svnCommitResult);
                    if (result)
                    {
                        result &= client.SetRevisionProperty(svnCommitResult.RepositoryRoot, new SvnRevision(svnCommitResult.Revision), SvnPropertyNames.SvnAuthor, taggerName);
                        result &= client.SetRevisionProperty(svnCommitResult.RepositoryRoot, new SvnRevision(svnCommitResult.Revision), SvnPropertyNames.SvnDate, SvnPropertyNames.FormatDate(localTime));
                    }
                    result &= client.Update(workingCopyPath, new SvnUpdateArgs { AddsAsModifications = false, AllowObstructions = false, Depth = SvnDepth.Infinity, IgnoreExternals = true, KeepDepth = true, Revision = SvnRevision.Head, UpdateParents = false });
                    */

                }
                catch (SvnException e)
                {
                    MessageBox.Show(string.Format("{0} Error Code: {1}{2}", e.Message, e.SvnErrorCode, Environment.NewLine), "SVN Operation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public void Tag(string name, string taggerName, string taggerEmail, string comment, DateTime localTime, string taggedPath)
        {
            /*
            TempFile commentFile;

            var args = "tag";
            AddComment(comment, ref args, out commentFile);

            // tag names are not quoted because they cannot contain whitespace or quotes
            args += " -- " + name;

            using (commentFile)
            {
                var startInfo = GetStartInfo(args);
                startInfo.EnvironmentVariables["GIT_COMMITTER_NAME"] = taggerName;
                startInfo.EnvironmentVariables["GIT_COMMITTER_EMAIL"] = taggerEmail;
                startInfo.EnvironmentVariables["GIT_COMMITTER_DATE"] = GetUtcTimeString(localTime);

                ExecuteUnless(startInfo, null);
            }
            */
            using (var client = new SvnClient())
            {
                try
                {
                    SvnUI.Bind(client, parentWindow);
                    var svnCopyArgs = new SvnCopyArgs { LogMessage = comment, CreateParents = true, AlwaysCopyAsChild = false };

                    var workingCopyUri = client.GetUriFromWorkingCopy(workingCopyPath);
                    var tagsUri = client.GetUriFromWorkingCopy(tagPath);
                    var sourceUri = useSvnStandardDirStructure ? client.GetUriFromWorkingCopy(trunkPath) : workingCopyUri;
                    var tagUri = new Uri(useSvnStandardDirStructure ? tagsUri : workingCopyUri, name);

                    var svnCommitResult = (SvnCommitResult)null;
                    var result = client.RemoteCopy(sourceUri, tagUri, svnCopyArgs, out svnCommitResult);
                    if (result)
                    {
                        result &= client.SetRevisionProperty(svnCommitResult.RepositoryRoot, new SvnRevision(svnCommitResult.Revision), SvnPropertyNames.SvnAuthor, taggerName);
                        result &= client.SetRevisionProperty(svnCommitResult.RepositoryRoot, new SvnRevision(svnCommitResult.Revision), SvnPropertyNames.SvnDate, SvnPropertyNames.FormatDate(localTime));
                    }
                    result &= client.Update(workingCopyPath, new SvnUpdateArgs { AddsAsModifications = false, AllowObstructions = false, Depth = SvnDepth.Infinity, IgnoreExternals = true, KeepDepth = true, Revision = SvnRevision.Head, UpdateParents = false });
                }
                catch (SvnException e)
                {
                    MessageBox.Show(string.Format("{0} Error Code: {1}{2}", e.Message, e.SvnErrorCode, Environment.NewLine), "SVN Operation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public static bool ValidateWorkingCopy(string repositoryPath)
        {
            using (var client = new SvnClient())
            {
                try
                {
                    return client.GetUriFromWorkingCopy(repositoryPath) != null;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        private static string GetUtcTimeString(DateTime localTime)
        {
            // convert local time to UTC based on whether DST was in effect at the time
            var utcTime = TimeZoneInfo.ConvertTimeToUtc(localTime);

            // format time according to ISO 8601 (avoiding locale-dependent month/day names)
            return utcTime.ToString("yyyy'-'MM'-'dd HH':'mm':'ss +0000");
        }

        private void SvnExec(string args)
        {
            var startInfo = GetStartInfo(args);
            ExecuteUnless(startInfo, null);
        }

        private ProcessStartInfo GetStartInfo(string args)
        {
            if (!string.IsNullOrEmpty(svnInitialArguments))
            {
                args = svnInitialArguments + " " + args;
            }

            var startInfo = new ProcessStartInfo(svnExecutable, args);
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.WorkingDirectory = workingCopyPath;
            startInfo.CreateNoWindow = true;
            return startInfo;
        }

        private bool ExecuteUnless(ProcessStartInfo startInfo, string unless)
        {
            string stdout, stderr;
            int exitCode = Execute(startInfo, out stdout, out stderr);
            if (exitCode != 0)
            {
                if (string.IsNullOrEmpty(unless) ||
                    ((string.IsNullOrEmpty(stdout) || !stdout.Contains(unless)) &&
                     (string.IsNullOrEmpty(stderr) || !stderr.Contains(unless))))
                {
                    FailExitCode(startInfo.FileName, startInfo.Arguments, stdout, stderr, exitCode);
                }
            }
            return exitCode == 0;
        }

        private static void FailExitCode(string exec, string args, string stdout, string stderr, int exitCode)
        {
            throw new ProcessExitException(
                string.Format("svn returned exit code {0}", exitCode),
                exec, args, stdout, stderr);
        }

        private int Execute(ProcessStartInfo startInfo, out string stdout, out string stderr)
        {
            logger.WriteLine("Executing: {0} {1}", startInfo.FileName, startInfo.Arguments);
            stdout = "simulated okay";
            stderr = "";
            return 0;


            stopwatch.Start();
            try
            {
                using (var process = Process.Start(startInfo))
                {
                    process.StandardInput.Close();
                    var stdoutReader = new AsyncLineReader(process.StandardOutput.BaseStream);
                    var stderrReader = new AsyncLineReader(process.StandardError.BaseStream);

                    var activityEvent = new ManualResetEvent(false);
                    EventHandler activityHandler = delegate { activityEvent.Set(); };
                    process.Exited += activityHandler;
                    stdoutReader.DataReceived += activityHandler;
                    stderrReader.DataReceived += activityHandler;

                    var stdoutBuffer = new StringBuilder();
                    var stderrBuffer = new StringBuilder();
                    while (true)
                    {
                        activityEvent.Reset();

                        while (true)
                        {
                            string line = stdoutReader.ReadLine();
                            if (line != null)
                            {
                                line = line.TrimEnd();
                                if (stdoutBuffer.Length > 0)
                                {
                                    stdoutBuffer.AppendLine();
                                }
                                stdoutBuffer.Append(line);
                                logger.Write('>');
                            }
                            else
                            {
                                line = stderrReader.ReadLine();
                                if (line != null)
                                {
                                    line = line.TrimEnd();
                                    if (stderrBuffer.Length > 0)
                                    {
                                        stderrBuffer.AppendLine();
                                    }
                                    stderrBuffer.Append(line);
                                    logger.Write('!');
                                }
                                else
                                {
                                    break;
                                }
                            }
                            logger.WriteLine(line);
                        }

                        if (process.HasExited)
                        {
                            break;
                        }

                        activityEvent.WaitOne(1000);
                    }

                    stdout = stdoutBuffer.ToString();
                    stderr = stderrBuffer.ToString();
                    return process.ExitCode;
                }
            }
            catch (FileNotFoundException e)
            {
                throw new ProcessException("Executable not found.",
                    e, startInfo.FileName, startInfo.Arguments);
            }
            catch (Win32Exception e)
            {
                throw new ProcessException("Error executing external process.",
                    e, startInfo.FileName, startInfo.Arguments);
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        private bool FindInPathVar(string filename, out string foundPath)
        {
            string path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path))
            {
                return FindInPaths(filename, path.Split(Path.PathSeparator), out foundPath);
            }
            foundPath = null;
            return false;
        }

        private bool FindInPaths(string filename, IEnumerable<string> searchPaths, out string foundPath)
        {
            foreach (string searchPath in searchPaths)
            {
                string path = Path.Combine(searchPath, filename);
                if (File.Exists(path))
                {
                    foundPath = path;
                    return true;
                }
            }
            foundPath = null;
            return false;
        }

        private const char QuoteChar = '"';
        private const char EscapeChar = '\\';

        /// <summary>
        /// Puts quotes around a command-line argument if it includes whitespace
        /// or quotes.
        /// </summary>
        /// <remarks>
        /// There are two things going on in this method: quoting and escaping.
        /// Quoting puts the entire string in quotes, whereas escaping is per-
        /// character. Quoting happens only if necessary, when whitespace or a
        /// quote is encountered somewhere in the string, and escaping happens
        /// only within quoting. Spaces don't need escaping, since that's what
        /// the quotes are for. Slashes don't need escaping because apparently a
        /// backslash is only interpreted as an escape when it precedes a quote.
        /// Otherwise both slash and backslash are just interpreted as directory
        /// separators.
        /// </remarks>
        /// <param name="arg">A command-line argument to quote.</param>
        /// <returns>The given argument, possibly in quotes, with internal
        /// quotes escaped with backslashes.</returns>
        private string Quote(string arg)
        {
            if (string.IsNullOrEmpty(arg))
            {
                return "\"\"";
            }

            StringBuilder buf = null;
            for (int i = 0; i < arg.Length; ++i)
            {
                char c = arg[i];
                if (buf == null && NeedsQuoting(c))
                {
                    buf = new StringBuilder(arg.Length + 2);
                    buf.Append(QuoteChar);
                    buf.Append(arg, 0, i);
                }
                if (buf != null)
                {
                    if (c == QuoteChar)
                    {
                        buf.Append(EscapeChar);
                    }
                    buf.Append(c);
                }
            }
            if (buf != null)
            {
                buf.Append(QuoteChar);
                return buf.ToString();
            }
            return arg;
        }

        private bool NeedsQuoting(char c)
        {
            return char.IsWhiteSpace(c) || c == QuoteChar ||
                (shellQuoting && (c == '&' || c == '|' || c == '<' || c == '>' || c == '^' || c == '%'));
        }

        private string Transcode(string source, Encoding sourceEncoding, Encoding targetEncoding)
        {
            var nativeBytes = sourceEncoding.GetBytes(source);
            var targetBytes = Encoding.Convert(sourceEncoding, targetEncoding, nativeBytes);
            return targetEncoding.GetString(targetBytes);
        }
    }
}
