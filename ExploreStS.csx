using System;
using System.Reflection;
using System.Linq;

try {
    var asm = Assembly.LoadFrom("/mnt/data/SteamLibrary/steamapps/common/Slay the Spire 2/data_sts2_linuxbsd_x86_64/sts2.dll");
    var types = asm.GetTypes();
    var registries = types.Where(t => t.Name.Contains("Registry") || t.Name.Contains("Library") || t.Name.Contains("Database")).Select(t => t.FullName).ToList();
    foreach (var r in registries) {
        Console.WriteLine(r);
    }
} catch (Exception e) {
    Console.WriteLine(e);
}
