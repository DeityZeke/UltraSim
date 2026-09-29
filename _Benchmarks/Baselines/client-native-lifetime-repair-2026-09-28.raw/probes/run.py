#!/usr/bin/env python3
"""Managed diagnostic controls only. No Godot runtime, restore, or network needed."""
from pathlib import Path
import argparse
import json
import os
import shutil
import subprocess
import sys

parser = argparse.ArgumentParser()
parser.add_argument('--repo', type=Path, required=True)
parser.add_argument('--dotnet-root', type=Path, required=True)
parser.add_argument('--output', type=Path)
args = parser.parse_args()
repo, sdk = args.repo.resolve(), args.dotnet_root.resolve()
source = Path(__file__).resolve().parent
out = (args.output or repo/'.output'/('native-lifetime-probes-' + str(os.getpid()))).resolve()
out.mkdir(parents=True, exist_ok=True)
version = lambda path: tuple(int(part) for part in path.name.split('.'))
sdkdir = max((sdk/'sdk').glob('8.*'), key=version)
refdir = max((sdk/'packs/Microsoft.NETCore.App.Ref').glob('8.*'), key=version)/'ref/net8.0'
runtime = max((sdk/'shared/Microsoft.NETCore.App').glob('8.*'), key=version).name
compiler = sdkdir/'Roslyn/bincore'
dotnet = str(sdk/'dotnet')
references = ['-r:'+str(path) for path in sorted(refdir.glob('*.dll'))]
usings = out/'Usings.cs'
usings.write_text('global using System;\nglobal using System.Linq;\nglobal using System.Collections.Generic;\nglobal using System.Threading.Tasks;\n')

def run(name, sources, extra=(), arguments=()):
    output = out/(name+'.dll')
    options = ['-nologo', '-target:exe', '-nullable:enable', '-optimize+', '-out:'+str(output)]
    options += references + list(extra) + [str(p) for p in sources]
    response = out/(name+'.rsp')
    response.write_text('\n'.join('"'+option+'"' for option in options)+'\n')
    subprocess.run([dotnet, str(compiler/'csc.dll'), '@'+str(response)], check=True, timeout=30)
    output.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {
        'tfm':'net8.0', 'framework':{'name':'Microsoft.NETCore.App','version':runtime}}}))
    result = subprocess.run([dotnet, str(output), *map(str,arguments)], text=True, capture_output=True, timeout=35)
    output.with_suffix('.log').write_text(result.stdout+result.stderr)
    print(name+':\n'+result.stdout, end='')
    if result.returncode:
        print(result.stderr, file=sys.stderr)
        raise SystemExit(result.returncode)

adapter = repo/'Client/Godot/Adapter'
run('LifetimeProbe', [usings, source/'LifetimeProbe.cs', adapter/'GodotApplicationLifetime.cs',
                     adapter/'GodotOwnedResources.cs', repo/'Client/Godot/Shell/ClientRoot.Lifetime.cs'])
subprocess.run([sys.executable, str(source/'make_actor_probe.py'), str(repo), str(out/'ActorExtract.cs')], check=True)
run('ActorProbe', [usings, source/'ActorProbe.cs', out/'ActorExtract.cs',
                  adapter/'GodotOwnedResources.cs', adapter/'GodotMeshGeometry.cs'])
run('SurfaceToolProbe', [usings, source/'SurfaceToolProbe.cs', adapter/'GodotMeshGeometry.cs'])
for name in ['Microsoft.CodeAnalysis.dll', 'Microsoft.CodeAnalysis.CSharp.dll']:
    shutil.copyfile(compiler/name, out/name)
run('SyntaxCheck', [source/'SyntaxCheck.cs'],
    ['-r:'+str(compiler/'Microsoft.CodeAnalysis.dll'), '-r:'+str(compiler/'Microsoft.CodeAnalysis.CSharp.dll')],
    [repo/'Client/Godot'])
print('Evidence:', out)
