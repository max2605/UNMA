using System.Collections.Generic;

namespace Mafi;

internal static class Log
{
    public static List<string> Warnings { get; } = new();

    public static void Warning(string message)
    {
        Warnings.Add(message ?? "");
    }
}
