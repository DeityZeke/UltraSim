
using System;

using Godot;

public partial class ServerAutoLoad : Node
{
    public override void _Ready()
    {
        //AssemblyManager.Init(); // Called Every Invoke // Probably bad?


        AssemblyManager.Invoke("Configure");
        AssemblyManager.Invoke("Configure_Server");
        AssemblyManager.Invoke("Configure_Client"); // FOR TESTING SPLIT OFF TO CLIENT

        AssemblyManager.Invoke("Initialize");


        base._Ready();
    }
}