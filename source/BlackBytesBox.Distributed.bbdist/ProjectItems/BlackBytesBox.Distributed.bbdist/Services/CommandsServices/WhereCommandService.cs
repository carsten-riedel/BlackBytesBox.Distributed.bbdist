using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using BlackBytesBox.Distributed.bbdist.Spectre;

using Microsoft.Extensions.Logging;

using static BlackBytesBox.Distributed.bbdist.Services.CommandsServices.WhereCommandService;

namespace BlackBytesBox.Distributed.bbdist.Services.CommandsServices
{
    public interface IWhereCommandService
    {
        Task<List<DirFileMatch>> FindFiles(string? executable, string? directories, string? skip, PeFileType filter = PeFileType.None, CancellationToken cancellationToken = default);
        Task<List<DirFilePeTypeMatch>> GetFilesAndPeTypeFromDirectoriesAsync(List<DirFileMatch> dirFileMatch, CancellationToken cancellationToken = default);
    }

    public class WhereCommandService : IWhereCommandService
    {
        private readonly ILogger<WhereCommandService> _logger;
        private readonly CancellationToken _cancelationToke;

        public WhereCommandService(ILogger<WhereCommandService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Finds all occurrences of the specified executables under the specified root directories,
        /// skipping any directories listed in the skip parameter, and optionally filtering by PE file type.
        /// &lt;implementation hidden&gt;
        /// </summary>
        /// <remarks>
        /// Splits each incoming comma-separated string into an array, expands any environment variables
        /// in each element, then calls into GetAllDirectoriesAsync. Measures and logs elapsed time,
        /// and applies the optional PE-type filter.
        /// </remarks>
        /// <param name="executable">
        /// Comma-separated list of file names (e.g. "git.exe,msbuild.exe").
        /// Entries may include environment variables (e.g. "%USERPROFILE%\\tools\\myapp.exe").
        /// Must not be null or empty.
        /// </param>
        /// <param name="directories">
        /// Comma-separated list of root directories (e.g. "C:\\,D:\\Tools").
        /// Entries may include environment variables (e.g. "%SystemRoot%").
        /// Must not be null or empty.
        /// </param>
        /// <param name="skip">
        /// Comma-separated list of directories to skip (e.g. "C:\\Windows,C:\\ProgramData").
        /// Entries may include environment variables.
        /// Must not be null or empty.
        /// </param>
        /// <param name="filter">
        /// Which PE-type to include. If <c>PeFileType.None</c>, no filtering is applied.
        /// </param>
        /// <param name="cancellationToken">Token to cancel the search.</param>
        /// <returns>List of full paths for each found executable matching the filter.</returns>
        /// <example>
        /// <code>
        /// // No filter (all files)
        /// var all = await FindExecutable(
        ///     "git.exe,msbuild.exe",
        ///     "%USERPROFILE%,%ProgramFiles%",
        ///     "%SystemRoot%",
        ///     PeFileType.None,
        ///     CancellationToken.None);
        ///
        /// // Only 64-bit PEs
        /// var x64Only = await FindExecutable(
        ///     "git.exe,msbuild.exe",
        ///     "C:\\,D:\\Tools",
        ///     "C:\\Windows",
        ///     PeFileType.Pe64,
        ///     CancellationToken.None);
        /// </code>
        /// </example>
        public async Task<List<DirFileMatch>> FindFiles(string? executable,string? directories, string? skip, PeFileType filter = PeFileType.None, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(executable))
                throw new ArgumentException("Executable list cannot be null or empty", nameof(executable));
            if (string.IsNullOrWhiteSpace(directories))
                throw new ArgumentException("Directory list cannot be null or empty", nameof(directories));

            // parse & expand inputs
            var files = executable
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Environment.ExpandEnvironmentVariables)
                .ToArray();

            var roots = directories
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Environment.ExpandEnvironmentVariables)
                .ToArray();

            var skipDirs = skip?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Environment.ExpandEnvironmentVariables)
                .ToArray();

            List<DirFileMatch> foundItems = await GetFilesFromDirectoriesAsync(roots, files, skipDirs, cancellationToken);

            return foundItems;
        }



        public class DirFileMatch
        {
            public string Directory { get; set; }
            public string FileName { get; set; }

            public DirFileMatch(string Directory, string FileName)
            {
                this.Directory = Directory;
                this.FileName = FileName;
            }
        }

        public class DirFilePeTypeMatch : DirFileMatch
        {
            public PeFileType PeType { get; set; }
            public DirFilePeTypeMatch(string Directory, string FileName, PeFileType PeType)
                : base(Directory, FileName)
            {
                this.PeType = PeType;
            }
        }
        

        /// <summary>
        /// Asynchronously retrieves the specified root directories and their subdirectories,
        /// skipping any specified paths, and checking for specified files.
        /// </summary>
        /// <remarks>
        /// Scans each accessible directory for the existence of any provided file names.
        /// </remarks>
        /// <param name="roots">The collection of root directories to start enumeration from.</param>
        /// <param name="fileNames">The collection of file names (e.g., "cmd.exe") to look for in each directory.</param>
        /// <param name="skipDirectories">Optional full directory paths to exclude from enumeration (nullable).</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>
        /// A <see cref="Task{List{DirFileMatch}}"/> whose result is the distinct directory/file matches
        /// found—excluding any files in skipped directories if provided.
        /// </returns>
        /// <example>
        /// <code>
        /// var roots = new[] { @"C:\Users\User1\Download", @"C:\Users\User1" };
        /// var files = new[] { "cmd.exe", "script.ps1" };
        /// var skip  = new[] { @"C:\Users\User1\AppData" };
        /// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        /// var matches = await helper.GetAllDirectoriesAsync(roots, files, skip, cts.Token);
        /// foreach (var m in matches)
        ///     Console.WriteLine($"Found {m.FileName} in {m.Directory}");
        /// </code>
        /// </example>
        public async Task<List<DirFileMatch>> GetFilesFromDirectoriesAsync(IEnumerable<string> roots, IEnumerable<string> fileNames, IEnumerable<string>? skipDirectories = null, CancellationToken cancellationToken = default)
        {
            var results = new List<DirFileMatch>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var skipSet = new HashSet<string>(
                skipDirectories ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            // Normalize and dedupe file names
            var fileSet = fileNames
                .Where(fn => !string.IsNullOrWhiteSpace(fn))
                .Select(fn => fn.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var stack = new Stack<string>();

            // Seed initial roots
            foreach (var root in roots)
            {
                if (skipSet.Contains(root))
                {
                    _logger.LogInformation("Skipping root: {Path}", root);
                    continue;
                }
                if (visited.Add(root))
                {
                    stack.Push(root);
                }
            }

            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = stack.Pop();

                // Check each file in this directory
                foreach (var fileName in fileSet)
                {
                    var filePath = Path.Combine(current, fileName);
                    if (File.Exists(filePath))
                    {
                        results.Add(new DirFileMatch(current, fileName));
                        _logger.LogInformation("Found file: {File} in {Path}", fileName, current);
                    }
                }

                string[] subDirs;
                try
                {
                    subDirs = Directory.GetDirectories(current);
                }
                catch (UnauthorizedAccessException)
                {
                    _logger.LogInformation("Access denied: {Path}", current);
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.LogInformation(ex, "Failed to enumerate {Path}", current);
                    continue;
                }

                foreach (var dir in subDirs)
                {
                    if (skipSet.Contains(dir))
                    {
                        _logger.LogInformation("Skipping directory: {Path}", dir);
                        continue;
                    }
                    if (visited.Add(dir))
                    {
                        stack.Push(dir);
                    }
                }

                await Task.Yield();
            }

            // Remove duplicate matches
            return results
                .Distinct()
                .ToList();
        }

        public async Task<List<DirFilePeTypeMatch>> GetFilesAndPeTypeFromDirectoriesAsync(List<DirFileMatch> dirFileMatch, CancellationToken cancellationToken = default)
        {
            var results = new List<DirFilePeTypeMatch>();
            foreach (var item in dirFileMatch)
            {
                var fullPath = Path.Combine(item.Directory, item.FileName);
                var type = GetPeFileType(fullPath);
                results.Add(new DirFilePeTypeMatch(item.Directory, item.FileName, type));
            }

            return results;
        }

        /// <summary>
        /// Specifies the type of a Portable Executable (PE) file, or None to apply no filter.
        /// </summary>
        public enum PeFileType
        {
            /// <summary>
            /// No filtering: include all found files regardless of PE type.
            /// </summary>
            None,
            /// <summary>
            /// 32-bit PE image.
            /// </summary>
            x32,
            /// <summary>
            /// 64-bit PE image.
            /// </summary>
            x64
        }


        /// <summary>
        /// Determines the PE file type (32-bit, 64-bit or not a PE).
        /// </summary>
        /// <remarks>
        /// &lt;implementation hidden&gt;
        /// </remarks>
        /// <param name="filePath">Full path to the file to inspect.</param>
        /// <returns>
        /// A <see cref="PeFileType"/> value indicating whether the file is PE32, PE64, or not a PE file.
        /// </returns>
        /// <example>
        /// <code>
        /// PeFileType type = ExecutableHelper.GetPeFileType(@"C:\Tools\app.exe");
        /// switch(type)
        /// {
        ///     case PeFileType.Pe64: Console.WriteLine("64-bit"); break;
        ///     case PeFileType.Pe32: Console.WriteLine("32-bit"); break;
        ///     default: Console.WriteLine("Not a PE file"); break;
        /// }
        /// </code>
        /// </example>
        public static PeFileType GetPeFileType(string filePath)
        {
            if (!File.Exists(filePath))
                return PeFileType.None;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                using var br = new BinaryReader(fs);

                long length = fs.Length;
                const int DOS_HEADER_POINTER_OFFSET = 0x3C;
                const int MIN_DOS_SIZE = DOS_HEADER_POINTER_OFFSET + sizeof(int); // 0x3C + 4 bytes

                // 0. Quick length check for DOS header
                if (length < MIN_DOS_SIZE)
                    return PeFileType.None;

                // 1. DOS header e_lfanew @ 0x3C
                fs.Seek(DOS_HEADER_POINTER_OFFSET, SeekOrigin.Begin);
                int peOffset = br.ReadInt32();

                // 1b. Ensure there's room for PE signature, COFF header, and OptionalHeader.Magic
                const int REQUIRED_AFTER_PE = sizeof(uint) /*signature*/
                                             + 20               /*COFF*/
                                             + sizeof(ushort);  /*magic*/
                if (peOffset < 0 || peOffset > length - REQUIRED_AFTER_PE)
                    return PeFileType.None;

                // 2. PE signature "PE\0\0"
                fs.Seek(peOffset, SeekOrigin.Begin);
                if (br.ReadUInt32() != 0x00004550)  // 'P'|'E'|0|0
                    return PeFileType.None;

                // 3. Skip COFF header (20 bytes) to OptionalHeader.Magic
                fs.Seek(20, SeekOrigin.Current);

                // 4. OptionalHeader.Magic
                ushort magic = br.ReadUInt16();
                const ushort IMAGE_NT_OPTIONAL_HDR32_MAGIC = 0x10b;
                const ushort IMAGE_NT_OPTIONAL_HDR64_MAGIC = 0x20b;

                return magic switch
                {
                    IMAGE_NT_OPTIONAL_HDR32_MAGIC => PeFileType.x32,
                    IMAGE_NT_OPTIONAL_HDR64_MAGIC => PeFileType.x64,
                    _ => PeFileType.None
                };
            }
            catch (IOException)
            {
                // Could not read file – treat as not PE
                return PeFileType.None;
            }
        }
    }

}



