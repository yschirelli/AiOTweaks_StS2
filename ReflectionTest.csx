using System;
using System.Linq;
using System.Reflection;

try {
    var asm = Assembly.LoadFrom("/mnt/data/SteamLibrary/steamapps/common/Slay the Spire 2/data_sts2_linuxbsd_x86_64/sts2.dll");
    var types = asm.GetTypes();
    
    // Look for a common base type for cards
    var cardBase = types.FirstOrDefault(t => t.Name == "Card" && t.Namespace != null && t.Namespace.Contains("Cards"));
    if (cardBase != null) {
        var cardTypes = types.Where(t => t.IsSubclassOf(cardBase) && !t.IsAbstract).ToList();
        Console.WriteLine($"Found {cardTypes.Count} cards. Examples: " + string.Join(", ", cardTypes.Take(5).Select(t => t.Name)));
    } else {
        Console.WriteLine("Could not find Card base class.");
    }
    
    // Look for a common base type for relics
    var relicBase = types.FirstOrDefault(t => t.Name == "Relic" && t.Namespace != null && t.Namespace.Contains("Relics"));
    if (relicBase != null) {
        var relicTypes = types.Where(t => t.IsSubclassOf(relicBase) && !t.IsAbstract).ToList();
        Console.WriteLine($"Found {relicTypes.Count} relics. Examples: " + string.Join(", ", relicTypes.Take(5).Select(t => t.Name)));
    } else {
        Console.WriteLine("Could not find Relic base class.");
    }
} catch (Exception e) {
    Console.WriteLine(e);
}
