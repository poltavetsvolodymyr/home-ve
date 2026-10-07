namespace HomeBackend.Cli;

public static class PasswordPrompt
{
    private const int MinLength = 8;

    /// <summary>Asks twice on a terminal; takes one line as is when stdin is redirected (scripts, pipes).</summary>
    public static string ReadNew()
    {
        if (Console.IsInputRedirected)
        {
            var piped = Console.ReadLine() ?? "";
            if (piped.Length < MinLength) throw new InvalidOperationException("password must be at least 8 characters");
            return piped;
        }
        while (true)
        {
            var first = ReadHidden("New password: ");
            if (first.Length < MinLength) { Console.WriteLine("At least 8 characters, please."); continue; }
            if (ReadHidden("Repeat: ") == first) return first;
            Console.WriteLine("Passwords don't match.");
        }
    }

    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
            if (!char.IsControl(key.KeyChar)) sb.Append(key.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString();
    }
}
