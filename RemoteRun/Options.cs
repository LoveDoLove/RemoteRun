namespace RemoteRun;

/// <summary>
/// Parsed command-line options.
/// </summary>
internal sealed class Options
{
    /// <summary>Full path (or name) of the program to run.</summary>
    public string Program { get; init; } = string.Empty;

    /// <summary>Arguments to pass to the program (may be null).</summary>
    public string? Arguments { get; init; }

    /// <summary>Working directory for the launched process (may be null → inherit).</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Remote computer name or IP address (e.g. "192.168.1.100" or "MYSERVER").
    /// Null means run locally.
    /// </summary>
    public string? RemoteComputer { get; init; }

    /// <summary>When true, do not wait for the process to exit.</summary>
    public bool NoWait { get; init; }

    /// <summary>Timeout in seconds to wait for the process (0 = unlimited).</summary>
    public int TimeoutSeconds { get; init; } = 60;

    /// <summary>When true, print usage and exit.</summary>
    public bool ShowHelp { get; init; }

    // ── Internal service mode ──────────────────────────────────────────────
    /// <summary>
    /// Internal flag: the exe is being invoked by the Windows Service Control
    /// Manager (SCM). Users should never pass this flag directly.
    /// </summary>
    public bool ServiceMode { get; init; }

    /// <summary>
    /// Temporary working directory used for inter-process communication when
    /// running in service mode.
    /// </summary>
    public string? ServiceWorkDir { get; init; }

    // ── Parser ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse the raw <paramref name="args"/> array.
    /// Returns <c>null</c> and writes an error message when parsing fails.
    /// </summary>
    public static Options? Parse(string[] args)
    {
        // No arguments: default to opening an interactive SYSTEM command prompt.
        if (args.Length == 0)
            return new Options { Program = "cmd.exe" };

        // Internal service-mode invocation (not for end users)
        if (args[0] == "--service")
        {
            string? workDir = null;
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == "--workdir")
                    workDir = args[i + 1];
            }
            return new Options { ServiceMode = true, ServiceWorkDir = workDir };
        }

        string? remote      = null;
        string? program     = null;
        string? arguments   = null;
        string? workDir2    = null;
        bool    noWait      = false;
        int     timeout     = 60;

        int idx = 0;
        while (idx < args.Length)
        {
            string a = args[idx];

            if (a is "-h" or "--help" or "/?")
                return new Options { ShowHelp = true };

            if (a == "-d") { noWait = true; idx++; continue; }

            if (a == "-w")
            {
                if (idx + 1 >= args.Length)
                { Console.Error.WriteLine("Error: -w requires a directory argument."); return null; }
                workDir2 = args[++idx]; idx++; continue;
            }

            if (a == "-t")
            {
                if (idx + 1 >= args.Length || !int.TryParse(args[idx + 1], out timeout))
                { Console.Error.WriteLine("Error: -t requires an integer seconds argument."); return null; }
                idx += 2; continue;
            }

            // Remote computer: \\machine or \\IP
            if (a.StartsWith(@"\\", StringComparison.Ordinal))
            {
                remote = a.TrimStart('\\');
                idx++; continue;
            }

            // First non-flag token is the program
            if (program == null)
            {
                program = a; idx++;
                // Everything remaining is arguments
                if (idx < args.Length)
                    arguments = string.Join(" ", args[idx..]);
                break;
            }

            idx++;
        }

        if (string.IsNullOrWhiteSpace(program))
        {
            Console.Error.WriteLine("Error: no program specified.");
            return null;
        }

        return new Options
        {
            Program          = program,
            Arguments        = arguments,
            WorkingDirectory = workDir2,
            RemoteComputer   = remote,
            NoWait           = noWait,
            TimeoutSeconds   = timeout,
        };
    }
}
