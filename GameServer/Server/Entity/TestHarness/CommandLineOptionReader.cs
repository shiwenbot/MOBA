using System;

namespace Fantasy;

public static class CommandLineOptionReader
{
    public static bool HasMode(string[] args, string modeName)
    {
        if (args == null || string.IsNullOrWhiteSpace(modeName))
        {
            return false;
        }

        string shortFlag = $"--{modeName}";
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.Equals(shortFlag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (TryReadOption(args, ref i, "--mode", out string modeValue) &&
                modeValue.Equals(modeName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryReadOption(string[] args, ref int index, string optionName, out string value)
    {
        string arg = args[index];
        if (arg.Equals(optionName, StringComparison.OrdinalIgnoreCase))
        {
            value = ReadRequiredValue(args, ref index, optionName);
            return true;
        }

        string inlinePrefix = optionName + "=";
        if (arg.StartsWith(inlinePrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = arg.Substring(inlinePrefix.Length);
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string ReadRequiredValue(string[] args, ref int index, string optionName)
    {
        int valueIndex = index + 1;
        if (valueIndex >= args.Length)
        {
            throw new ArgumentException($"{optionName} requires a value.");
        }

        index = valueIndex;
        return args[valueIndex];
    }
}
