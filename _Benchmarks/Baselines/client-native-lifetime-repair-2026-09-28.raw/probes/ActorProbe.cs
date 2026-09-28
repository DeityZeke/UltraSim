using System;
using System.Collections.Generic;
using System.Linq;
using UltraSim.Client.Godot;
internal static class ActorProbe
{
 static int Main()
 {
  for(int mode=0;mode<2;mode++)
  {
   Godot.GodotObject.All.Clear();Godot.SurfaceTool.Commits=0;Godot.SurfaceTool.FailAt=0;
   var presenter=new GodotActorPresenter();var root=new Godot.Node3D();
   root.AddChild(presenter.MakePrimitive());root.AddChild(presenter.MakeWeapon());root.AddChild(presenter.MakeCue());
   if(mode==1){var newParent=new Godot.Node3D();newParent.AddChild(root);root=newParent;}
   presenter.Retire(root);
   Check(presenter.OwnedCount==0,"all owner associations removed");
   Check(Godot.GodotObject.All.Where(x=>x is Godot.Material or Godot.Mesh or Godot.SurfaceTool).All(x=>x.Disposals==1),"exact wrapper disposal");
   Console.WriteLine("PASS actor ownership mode "+mode);
  }
  Godot.GodotObject.All.Clear();Godot.SurfaceTool.Commits=0;Godot.SurfaceTool.FailAt=2;
  var failed=new GodotActorPresenter();Exception? observed=null;
  try{failed.MakeWeapon();}catch(Exception e){observed=e;}
  Check(ReferenceEquals(observed,Godot.SurfaceTool.Injected),"original construction failure propagated");
  Check(Godot.GodotObject.All.Where(x=>x is Godot.Material or Godot.Mesh or Godot.SurfaceTool).All(x=>x.Disposals==1),"partial weapon rollback");
  Check(failed.OwnedCount==0,"failed candidate never published");
  Console.WriteLine("PASS weapon rollback after first mesh committed");
  Console.WriteLine("PASS 3/3 extracted production actor ownership controls (Godot doubles)");
  return 0;
 }
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
}
namespace Godot
{
 public class GodotObject:IDisposable
 {
  public static readonly List<GodotObject> All=new();public int Disposals;public bool Valid=true;
  public GodotObject(){All.Add(this);}public void Dispose(){Disposals++;Valid=false;}
  public static bool IsInstanceValid(GodotObject x)=>x.Valid;
 }
 public class Node:GodotObject
 {
  protected readonly List<Node> Children=new();public string Name="";public void AddChild(Node n)=>Children.Add(n);
  public int GetChildCount()=>Children.Count;public Node GetChild(int i)=>Children[i];
  public void Free(){foreach(var c in Children)c.Free();Children.Clear();Valid=false;}
 }
 public class Node3D:Node {public bool Visible;public Vector3 Position;public Basis Basis;}
 public class MeshInstance3D:Node3D {public Mesh? Mesh;}
 public class Mesh:GodotObject {public enum PrimitiveType{Triangles}}
 public class ArrayMesh:Mesh{}
 public class CapsuleMesh:Mesh {public float Radius,Height;public Material? Material;}
 public class Material:GodotObject{}
 public class BaseMaterial3D:Material {public enum CullModeEnum{Disabled}}
 public class StandardMaterial3D:BaseMaterial3D
 {public Color AlbedoColor,Emission;public float Metallic,Roughness,EmissionEnergyMultiplier;public bool EmissionEnabled;public CullModeEnum CullMode;}
 public readonly record struct Color(float R,float G,float B);
 public readonly record struct Basis(Vector3 X,Vector3 Y,Vector3 Z);
 public readonly record struct Vector3(float X,float Y,float Z)
 {
  public static Vector3 Up=>new(0,1,0);
  public bool IsFinite()=>float.IsFinite(X)&&float.IsFinite(Y)&&float.IsFinite(Z);
  public float Dot(Vector3 b)=>X*b.X+Y*b.Y+Z*b.Z;
  public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
  public static Vector3 operator *(Vector3 a,float b)=>new(a.X*b,a.Y*b,a.Z*b);
  public Vector3 Cross(Vector3 b)=>new(Y*b.Z-Z*b.Y,Z*b.X-X*b.Z,X*b.Y-Y*b.X);
  public Vector3 Normalized(){var l=MathF.Sqrt(Dot(this));return new(X/l,Y/l,Z/l);}
 }
 public static class Mathf {public static float Min(float a,float b)=>Math.Min(a,b);}
 public class SurfaceTool:GodotObject
 {
  public static int Commits,FailAt;public static readonly Exception Injected=new InvalidOperationException("injected Commit failure");
  public void Begin(Mesh.PrimitiveType x){} public void SetMaterial(Material x){} public void SetNormal(Vector3 x){} public void AddVertex(Vector3 x){}
  public ArrayMesh Commit(){if(++Commits==FailAt)throw Injected;return new();}
 }
}
namespace UltraSim.Client.Core
{
 public enum ClientActorVisualKind {HumanX,HumanY,Animal}
 public enum ClientPrimitiveShape {Capsule,Box}
 public readonly record struct ClientPrimitivePresentation(int WidthMillimeters,int HeightMillimeters,int DepthMillimeters,ClientPrimitiveShape Shape);
 public static class ClientPrimitivePresentationCatalog {public static ClientPrimitivePresentation ForActor(ClientActorVisualKind k)=>new(1,2,3,ClientPrimitiveShape.Capsule);}
}
