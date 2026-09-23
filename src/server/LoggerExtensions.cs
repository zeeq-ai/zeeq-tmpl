using System.Runtime.CompilerServices;
using ILogger = Serilog.ILogger;

namespace Zeeq.Tmpl;

/// <summary>
/// Extension method to capture the call site with member name, file, and line number.
/// </summary>
public static class LoggerExtensions
{
    public static ILogger Here(
        this ILogger logger,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0
    )
    {
        var srcFile = Path.GetFileName(sourceFilePath);
        var here = $" {srcFile}:{memberName}@{sourceLineNumber}";

        return logger
            .ForContext("Here", here)
            .ForContext("MemberName", memberName)
            .ForContext("FilePath", sourceFilePath)
            .ForContext("LineNumber", sourceLineNumber);
    }
}
