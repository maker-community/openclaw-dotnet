using System;
using System.Linq;
using System.Reflection;

namespace OpenClaw.GatewayClient;

public static class ReflectGeralt
{
  public static string Dump()
  {
    try
    {
      var asm = Assembly.Load("Geralt");
      var t = asm.GetTypes().FirstOrDefault(x => x.Name.Contains("Ed25519", StringComparison.OrdinalIgnoreCase));
      if (t is null) return "Ed25519 type not found";
      var lines = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Select(m => m.ToString())
        .ToArray();
      return t.FullName + "\n" + string.Join("\n", lines);
    }
    catch (Exception ex)
    {
      return ex.ToString();
    }
  }
}
