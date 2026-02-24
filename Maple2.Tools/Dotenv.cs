using System;
using System.IO;

namespace Maple2.Tools;

public static class DotEnv {
    public static void Load() {
        string dotenv = Path.Combine(Paths.SOLUTION_DIR, ".env");

        if (!File.Exists(dotenv)) {
            Console.WriteLine($"No .env file found at path: {dotenv}");
            return;
        }

        foreach (string line in File.ReadAllLines(dotenv)) {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) {
                continue;
            }

            int index = line.IndexOf('=');
            if (index == -1) {
                Console.WriteLine($"Invalid .env line: {line}");
                continue;
            }

            string key = line[..index].Trim();
            string value = line[(index + 1)..].Trim();

            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
