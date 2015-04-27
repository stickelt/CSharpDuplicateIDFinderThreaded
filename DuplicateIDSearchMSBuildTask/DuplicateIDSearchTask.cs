using Microsoft.Build.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Concurrent;
using DuplicateIDSearch;

namespace DuplicateIDSearchMSBuildTask
{
    public class DuplicateIDSearchTask : ITask
    {
        public IBuildEngine BuildEngine { get; set; }
        public ITaskHost HostObject { get; set; }
        [Required]
        public string RootProjectDirectory { get; set; }
        [Output]
        public List<SearchMatch> Results { get; set; }

        public bool Execute()
        {
            Stopwatch stopwatch = new Stopwatch();
            Stopwatch totalTimeStopWatch = new Stopwatch();

            //var rootProjectDirectory = @"U:\DevRoot";

            totalTimeStopWatch.Start();
            stopwatch.Start();
            var directories = GetDirectories(RootProjectDirectory);
            Log(string.Format("Get Directories: {0}", (stopwatch.ElapsedMilliseconds / 1000) == 0 ? string.Format("0.{0}s", stopwatch.ElapsedMilliseconds) : string.Format("{0}s", stopwatch.ElapsedMilliseconds / 1000)));
            stopwatch.Stop();
            stopwatch.Reset();
            stopwatch.Start();
            var files = GetFiles(directories, "*.cshtml");
            Log(string.Format("Get Files: {0}", (stopwatch.ElapsedMilliseconds / 1000) == 0 ? string.Format("0.{0}s", stopwatch.ElapsedMilliseconds) : string.Format("{0}s", stopwatch.ElapsedMilliseconds / 1000)));
            Log(string.Format("# of Files: {0}", files.Count()));
            stopwatch.Stop();
            stopwatch.Reset();
            stopwatch.Start();
            var fileResults = ReadFiles(files);
            Log(string.Format("Patterns Matched In Files: {0}", fileResults.Count()));
            Log(string.Format("ReadFiles: {0}", (stopwatch.ElapsedMilliseconds / 1000) == 0 ? string.Format("0.{0}s", stopwatch.ElapsedMilliseconds) : string.Format("{0}s", stopwatch.ElapsedMilliseconds / 1000)));
            stopwatch.Stop();
            stopwatch.Reset();
            stopwatch.Start();
            List<String> duplicates = fileResults.GroupBy(x => x.Match)
                             .Where(g => g.Count() > 1)
                             .Select(g => g.Key)
                             .ToList();
            Log(string.Format("# of Duplicates: {0}", duplicates.Count()));
            Log(string.Format("duplicates: {0}", (stopwatch.ElapsedMilliseconds / 1000) == 0 ? string.Format("0.{0}s", stopwatch.ElapsedMilliseconds) : string.Format("{0}s", stopwatch.ElapsedMilliseconds / 1000)));
            stopwatch.Stop();

            Log(string.Format("Total Time: {0}", (totalTimeStopWatch.ElapsedMilliseconds / 1000) == 0 ? string.Format("{0}s", totalTimeStopWatch.ElapsedMilliseconds) : string.Format("{0}s", totalTimeStopWatch.ElapsedMilliseconds / 1000)));
            totalTimeStopWatch.Stop();

            Console.Read();

            return false;
        }

        public Action<Action,Action<string>> _Measure = (body, log) =>
        {
            var startTime = DateTime.Now;
            body();
            log(string.Format("Time:{0} ThreadID:{1}", DateTime.Now - startTime, Thread.CurrentThread.ManagedThreadId));
        };

        private IEnumerable<SearchMatch> ReadFiles(IEnumerable<string> files)
        {
            var searchMatches = new ConcurrentBag<SearchMatch>();
            Parallel.ForEach(files, p =>
            {
                var searchMatchesInFile = ReadFile(p);
                Parallel.ForEach(searchMatchesInFile, i =>
                {
                    searchMatches.Add(i);
                });
            });

            return searchMatches;
        }
        public List<string> GetDirectories(string directory)
        {
            var directories = new List<string>();
            Func<string, List<string>, List<string>> getAccessiableDirectories =  (tDirectory, tDirectories) =>  {
                var unFilteredDirectories = Directory.GetDirectories(tDirectory);
                
                var accessiableDirectories = unFilteredDirectories.Where(d => IsAuthorized(d)).ToList();

                if (accessiableDirectories.Count() > 0)
                    tDirectories.AddRange(accessiableDirectories);

                return tDirectories;
            };

            directories = getAccessiableDirectories(directory, directories);

            for (int i = 0; i < directories.Count; i++)
                directories = getAccessiableDirectories(directories[i], directories);

            return directories;
        }
        public IEnumerable<string> GetFiles(List<string> directories, string pattern)
        {
            var files = new ConcurrentBag<string>();

            Parallel.ForEach(directories, path =>
            {
                string[] directoryGetFiles = Directory.GetFiles(path, pattern, SearchOption.TopDirectoryOnly);
                Parallel.ForEach(directoryGetFiles, currentFiles => files.Add(currentFiles));
            });

            return files;
        }

        public IEnumerable<SearchMatch> ReadFile(string fileName)
        {
            var searchMatches = new ConcurrentBag<SearchMatch>();
            var allLines = File.ReadAllLines(fileName);
            Parallel.For(0, allLines.Length, x =>
            {
                var patternMatches = ProcessFile(fileName, x, allLines[x], @"id=""[a-zA-Z0-9\x2D\x2E\x3A\x5F]{1,}""");
                if(patternMatches != null)
                    Parallel.ForEach(patternMatches, p => searchMatches.Add(p)); 
            });

            return searchMatches;
        }

        public IEnumerable<SearchMatch> ProcessFile(string fileName, int lineNumber, string text, string pattern)
        {
            var matches = Regex.Matches(text, pattern)
                .Cast<Match>()
                .Select(p => new Tuple<int, string>(p.Index, p.Value))
                .ToArray();

            if (matches != null && matches.Length > 0)
                foreach (var match in matches)
                    yield return new SearchMatch() { FileName = fileName, Line = lineNumber, Match = match.Item2, Column = match.Item1 };
                
        }

        public bool IsAuthorized(string path)
        {
            bool isAuthorized = false;

            try
            {
                var fileSecuirty = new FileSecurity(path, AccessControlSections.Access);
                AuthorizationRuleCollection acl = fileSecuirty.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier));
                AuthorizationRule[] authorizationRuleCollection = new AuthorizationRule[acl.Count];
                acl.CopyTo(authorizationRuleCollection, 0);

                isAuthorized = authorizationRuleCollection.ToList().Where(rc =>
                {
                    var fileSystemAccessRule = (FileSystemAccessRule)rc;
                    return fileSystemAccessRule.AccessControlType == AccessControlType.Allow && (fileSystemAccessRule.FileSystemRights & FileSystemRights.ListDirectory) == FileSystemRights.ListDirectory;
                }).Count() > 0;
            }
            catch (UnauthorizedAccessException)
            {
                Log(string.Format("Attempted to perform an unauthorized operation for {0}", path));
            }
                
            return isAuthorized;
        }
        private void Log(string match)
        {
            BuildEngine.LogMessageEvent(new BuildMessageEventArgs("", string.Empty, "DuplicateIDSearchTask", MessageImportance.Low));
        }
        public void Log(string match, string file, int lineNumber, int columnNumber)
        {
            string subcategory = "Duplicate HTML ID";
            string code = "007";
            int endLineNumber = lineNumber;
            int endColumnNumber = columnNumber + (match.Length - 1);
            var importance = MessageImportance.High;
            var helpKeyword = string.Empty;
            var senderName = string.Empty;
            var message = string.Format("'{0}' was found to be a duplicate ID.", match);

            Console.WriteLine(message);
            BuildEngine.LogMessageEvent(new BuildMessageEventArgs(subcategory, 
                                            code, 
                                            file,
                                            lineNumber,
                                            columnNumber, 
                                            endColumnNumber,
                                            endLineNumber,
                                            message, 
                                            helpKeyword, 
                                            senderName, 
                                            importance));
        }
    }
}
