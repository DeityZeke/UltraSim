using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraSim.Server
{
    public sealed class ConsoleWorldPersistenceRecoveryInteraction : IWorldPersistenceRecoveryInteraction
    {
        public WorldPersistenceRecoveryMode? ResolveMissingOptionalSections(WorldPersistenceRecoveryRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (!Environment.UserInteractive || Console.IsInputRedirected)
                return null;

            Console.WriteLine("Save set is incomplete.");
            Console.WriteLine($"Root: {request.RootPath}");
            WriteMissingSection("Missing system files", request.MissingSystemFiles);
            WriteMissingSection("Missing entity files", request.MissingEntityFiles);
            Console.WriteLine("Choose recovery mode:");
            Console.WriteLine("  A - Abort");
            Console.WriteLine("  E - Continue without entities");
            Console.WriteLine("  S - Continue without missing systems");
            Console.WriteLine("  O - Continue without all missing optional sections");
            Console.Write("> ");

            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                return null;

            return char.ToUpperInvariant(input.Trim()[0]) switch
            {
                'A' => WorldPersistenceRecoveryMode.Abort,
                'E' => WorldPersistenceRecoveryMode.ContinueWithoutEntities,
                'S' => WorldPersistenceRecoveryMode.ContinueWithoutMissingSystems,
                'O' => WorldPersistenceRecoveryMode.ContinueWithoutAllMissingOptionalSections,
                _ => null,
            };
        }

        private static void WriteMissingSection(string title, IReadOnlyList<string> files)
        {
            if (files.Count == 0)
                return;

            Console.WriteLine(title + ":");
            foreach (string file in files.OrderBy(static v => v, StringComparer.Ordinal))
                Console.WriteLine("  - " + file);
        }
    }
}
