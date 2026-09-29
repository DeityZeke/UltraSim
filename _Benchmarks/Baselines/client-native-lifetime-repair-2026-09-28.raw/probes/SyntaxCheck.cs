using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
class SyntaxCheck
{
 static int Main(string[] args)
 {
  int errors=0, files=0;
  foreach(var file in Directory.EnumerateFiles(args[0],"*.cs",SearchOption.AllDirectories))
  {
   files++;
   foreach(var edition in new[]{"ULTRASIM_SINGLE_PLAYER","ULTRASIM_ONLINE","ULTRASIM_COMPLETE"})
    foreach(var d in CSharpSyntaxTree.ParseText(File.ReadAllText(file),new CSharpParseOptions(LanguageVersion.Preview,preprocessorSymbols:new[]{edition}),file).GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error))
    {Console.WriteLine(edition+": "+d);errors++;}
  }
  Console.WriteLine($"Parsed {files} C# files in all three product configurations: {errors} syntax errors.");
  return errors==0?0:1;
 }
}
