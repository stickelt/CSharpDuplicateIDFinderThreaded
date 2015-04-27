using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DuplicateIDSearch
{
    class Program
    {
        private static string _RootProjectDirectory;
        public static Action<Action> _Measure = (body) =>
            {
                var startTime = DateTime.Now;
                body();
                Log(string.Format("Time:{0} ThreadID:{1}", DateTime.Now - startTime, Thread.CurrentThread.ManagedThreadId));
            };
        static void Main(string[] args)
        {
            Stopwatch stopwatch = new Stopwatch();
            Stopwatch totalTimeStopWatch = new Stopwatch();

            _RootProjectDirectory = @"U:\DevRoot";

            totalTimeStopWatch.Start();
            stopwatch.Start();
            var directories = GetDirectories(_RootProjectDirectory);
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
                             .Select(g => g.Key).ToList();
            var result = fileResults.Where(f => duplicates.Contains(f.Match)).OrderBy(p => p.Match);

            List<string> lines = new List<string>();
            foreach (var item in result)
            {
                string format = string.Format("ID:\t{0}\tLine:\t{1}\tColumn:\t{2}\tFile:\t{3}", item.Match, item.Line, item.Column, item.FileName);
                lines.Add(format);
                Log(format);
            }

            System.IO.File.WriteAllLines(@"C:\Users\L\Downloads\Matches.txt", lines);

            Log(string.Format("# of Duplicates: {0}", duplicates.Count()));
            Log(string.Format("duplicates: {0}", (stopwatch.ElapsedMilliseconds / 1000) == 0 ? string.Format("0.{0}s", stopwatch.ElapsedMilliseconds) : string.Format("{0}s", stopwatch.ElapsedMilliseconds / 1000)));
            stopwatch.Stop();

            Log(string.Format("Total Time: {0}", (totalTimeStopWatch.ElapsedMilliseconds / 1000) == 0 ? string.Format("{0}s", totalTimeStopWatch.ElapsedMilliseconds) : string.Format("{0}s", totalTimeStopWatch.ElapsedMilliseconds / 1000)));
            totalTimeStopWatch.Stop();

            Console.Read();
        }

        private static IEnumerable<SearchMatch> ReadFiles(IEnumerable<string> files)
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
        public static List<string> GetDirectories(string directory)
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
        public static IEnumerable<string> GetFiles(List<string> directories, string pattern)
        {
            var files = new ConcurrentBag<string>();

            Parallel.ForEach(directories, path =>
            {
                string[] directoryGetFiles = Directory.GetFiles(path, pattern, SearchOption.TopDirectoryOnly);
                Parallel.ForEach(directoryGetFiles, currentFiles => files.Add(currentFiles));
            });

            return files;
        }

        public static IEnumerable<SearchMatch> ReadFile(string fileName)
        {
            var searchMatches = new ConcurrentBag<SearchMatch>();
            var allLines = File.ReadAllLines(fileName);
            Parallel.For(0, allLines.Length, x =>
            {
                var patternMatches = ProcessFile(fileName, x + 1, allLines[x], @"id=""[a-zA-Z0-9\x2D\x2E\x3A\x5F]{1,}""");
                if(patternMatches != null)
                    Parallel.ForEach(patternMatches, p => searchMatches.Add(p)); 
            });

            return searchMatches;
        }

        public static IEnumerable<SearchMatch> ProcessFile(string fileName, int lineNumber, string text, string pattern)
        {
            var matches = Regex.Matches(text, pattern)
                .Cast<Match>()
                .Select(p => new Tuple<int, string>(p.Index, p.Value))
                .ToArray();

            if(matches != null && matches.Length > 0)
                foreach (var match in matches)
                    yield return new SearchMatch() { FileName = fileName, Line = lineNumber, Match = match.Item2, Column = match.Item1 };
                
        }

        public static bool IsAuthorized(string path)
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
        public static void Log(string message)
        {
            Console.WriteLine(message);
            
        }
    }
}
