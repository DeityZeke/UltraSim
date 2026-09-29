from pathlib import Path
import re
import sys
root=Path(sys.argv[1])/'Client/Godot/Adapter'
def method(s,name):
 m=re.search(r'^    private (?:static )?[^\n=]+\b'+name+r'\(',s,re.M)
 if not m:m=re.search(r'^    internal static [^\n=]+\b'+name+r'\(',s,re.M)
 assert m,name
 a=s.index('{',m.start()); depth=1;b=a+1
 while depth:
  if s[b]=='{':depth+=1
  if s[b]=='}':depth-=1
  b+=1
 return s[m.start():b]
s=(root/'GodotActorPresenter.cs').read_text()
constants='\n'.join(re.findall(r'^    private const float MainHand[^\n]+',s,re.M))
methods='\n\n'.join(method(s,n) for n in ['CreatePrimitive','CreateMainHandPresentation','CreateMainHandGripBasis','CreateInteractCue','FreePresentation'])
p='''using Godot;
using UltraSim.Client.Core;
namespace UltraSim.Client.Godot;
public sealed class GodotActorPresenter
{
    private readonly Dictionary<Node3D,GodotOwnedResources> _presentationResources = new(ReferenceEqualityComparer.Instance);
'''+constants+'\n'+methods+'''
    public Node3D MakeWeapon() => CreateMainHandPresentation(out _);
    public Node3D MakeCue() => CreateInteractCue();
    public Node3D MakePrimitive() => CreatePrimitive(ClientActorVisualKind.HumanX);
    public void Retire(Node3D root) => FreePresentation(root);
    public int OwnedCount => _presentationResources.Count;
}
internal static class GodotPrimitiveSubstrate
{
'''+method((root/'GodotPrimitiveSubstrate.cs').read_text(),'CreateActor')+'\n}\n'
Path(sys.argv[2]).write_text(p)
