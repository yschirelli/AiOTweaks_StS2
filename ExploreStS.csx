using System;
using System.Reflection;
using System.Linq;

try {
    var asm = Assembly.LoadFrom("/mnt/data/SteamLibrary/steamapps/common/Slay the Spire 2/data_sts2_linuxbsd_x86_64/sts2.dll");
    var types = asm.GetTypes();

    Console.WriteLine("--- COMBAT STATE MEMBERS ---");
    var combatStateType = types.FirstOrDefault(t => t.Name == "CombatState");
    if (combatStateType != null) {
        foreach (var p in combatStateType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
            Console.WriteLine($"  Prop: {p.PropertyType.Name} {p.Name}");
        }
        foreach (var f in combatStateType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
            Console.WriteLine($"  Field: {f.FieldType.Name} {f.Name}");
        }
    }

    Console.WriteLine("\n--- CREATURE / MONSTER METHODS ---");
    var creatureType = types.FirstOrDefault(t => t.Name == "Creature");
    if (creatureType != null) {
        foreach (var m in creatureType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(m => m.Name.Contains("Die") || m.Name.Contains("Damage") || m.Name.Contains("Block") || m.Name.Contains("Hp"))) {
            Console.WriteLine($"  Creature Method: {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
        }
    }

    Console.WriteLine("\n--- MAP GENERATION / TRAVEL TYPES ---");
    var mapTypes = types.Where(t => t.Namespace != null && t.Namespace.Contains("Map")).ToList();
    foreach (var mt in mapTypes) {
        Console.WriteLine($"  Map Type: {mt.FullName}");
    }

    Console.WriteLine("\n--- DRAW CARD METHODS ---");
    var cardCmdType = types.FirstOrDefault(t => t.Name == "CardCmd" || t.Name == "CardPileCmd");
    if (cardCmdType != null) {
        foreach (var m in cardCmdType.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
            Console.WriteLine($"  {cardCmdType.Name}.{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
        }
    }

} catch (Exception e) {
    Console.WriteLine(e);
}
